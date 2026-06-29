using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TodoListMcp.Core;

namespace TodoListMcp.App;

/// <summary>
/// Resolves configured list aliases to files and serialises read/write access per file.
/// Each operation loads the document fresh from disk so changes made in the ToDoList
/// application (when it last saved) are always reflected.
/// </summary>
public sealed class TodoListManager
{
    private readonly IOptionsMonitor<TodoListMcpOptions> _options;
    private readonly ILogger<TodoListManager> _log;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public TodoListManager(IOptionsMonitor<TodoListMcpOptions> options, ILogger<TodoListManager> log)
    {
        _options = options;
        _log = log;
    }

    public IReadOnlyList<TodoFileEntry> Files => _options.CurrentValue.Files;

    /// <summary>Resolves the alias to a configured file, applying default/single-file rules.</summary>
    public TodoFileEntry Resolve(string? alias)
    {
        var files = _options.CurrentValue.Files;
        if (files.Count == 0)
            throw new InvalidOperationException(
                "No ToDoList files are configured. Open the tray menu → 'Open configuration' and add at least one file.");

        ValidateConfig(files);

        if (string.IsNullOrWhiteSpace(alias))
        {
            var def = files.FirstOrDefault(f => f.Default) ?? (files.Count == 1 ? files[0] : null);
            return def ?? throw new InvalidOperationException(
                $"Multiple lists are configured; specify 'list'. Available: {Aliases(files)}.");
        }

        // Match on trimmed aliases so a validated config (ValidateConfig trims too) is always
        // resolvable, even if an entry or the caller carries stray surrounding whitespace.
        var needle = alias.Trim();
        return files.FirstOrDefault(f => string.Equals(f.Alias.Trim(), needle, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Unknown list '{alias}'. Available: {Aliases(files)}.");
    }

    /// <summary>Loads the file and runs a read-only projection under the file lock.</summary>
    public T Read<T>(string? alias, Func<TodoListDocument, T> read) =>
        WithLock(alias, entry => read(LoadOrThrow(entry)));

    /// <summary>Loads the file, applies a mutation, and saves it atomically under the file lock.</summary>
    public T Write<T>(string? alias, Func<TodoListDocument, T> mutate) =>
        WithLock(alias, entry =>
        {
            var doc = LoadOrThrow(entry);
            var result = mutate(doc);
            if (doc.IsDirty)
            {
                doc.Save();
                _log.LogInformation("Saved changes to list '{Alias}' ({Path}).", entry.Alias, entry.Path);
            }
            else
            {
                _log.LogInformation("No changes to save for list '{Alias}' ({Path}).", entry.Alias, entry.Path);
            }
            return result;
        });

    /// <summary>Reads (and filters) the time-log sidecar for a list, under the list's file lock.</summary>
    public IReadOnlyList<Core.Model.TimeLogEntry> ReadLog(string? alias, Core.Model.TimeLogQuery query) =>
        WithLock(alias, entry => TimeLogDocument.Load(LogPath(entry)).Read(query));

    /// <summary>
    /// Appends one entry to the time-log sidecar, optionally also incrementing the task's TIMESPENT.
    /// Both writes happen under the same per-list lock. The sidecar is written before the .tdl, so a
    /// failure mid-sequence leaves the log entry without its TIMESPENT bump (harmless extra data)
    /// rather than an inflated TIMESPENT with no audit-trail entry.
    /// </summary>
    public Core.Model.TimeLogEntry LogTime(string? alias, Core.Model.LogTimeRequest req) =>
        WithLock(alias, entry =>
        {
            var hours = Math.Max(0, req.Hours);

            // Snapshot the task title and, if asked, stage its TIMESPENT increment — both touch the
            // .tdl, so load it once. A task entry must reference a task that exists in this list.
            TodoListDocument? doc = null;
            var title = "";
            string? path = null;
            if (req.TaskId > 0)
            {
                doc = LoadOrThrow(entry);
                title = (doc.GetTask(req.TaskId) ?? throw new TaskNotFoundException(req.TaskId)).Title;
                path = doc.GetTaskPath(req.TaskId); // snapshot the ancestor path, as ToDoList does
                if (req.AddToTimeSpent)
                    doc.IncrementTimeSpent(req.TaskId, hours);
            }

            // The sidecar stores HH:mm, so truncate to the minute up front — otherwise the returned
            // entry would carry seconds the persisted row (and a re-read) silently drops.
            var to = TruncateToMinute(req.To ?? req.When ?? DateTime.Now);
            var from = TruncateToMinute(req.From ?? to.AddHours(-hours));
            var logEntry = new Core.Model.TimeLogEntry
            {
                TaskId = req.TaskId,
                TaskTitle = title,
                Person = string.IsNullOrWhiteSpace(req.Person)
                    ? Environment.UserName
                    : req.Person.Trim(),
                From = from,
                To = to,
                Hours = hours,
                Comment = string.IsNullOrWhiteSpace(req.Comment) ? null : req.Comment,
                Type = string.IsNullOrWhiteSpace(req.Type) ? "Adjusted" : req.Type.Trim(),
                Path = string.IsNullOrEmpty(path) ? null : path,
            };

            // Write the sidecar first, then commit the .tdl increment (see the method remark).
            var log = TimeLogDocument.Load(LogPath(entry));
            log.Append(logEntry);
            log.Save();
            if (doc is not null && doc.IsDirty) doc.Save();
            _log.LogInformation("Logged {Hours}h to list '{Alias}' ({Path}).", hours, entry.Alias, LogPath(entry));
            return logEntry;
        });

    /// <summary>
    /// Edits the single time-log entry matched by <paramref name="selector"/> and saves the sidecar.
    /// This touches only the <c>_Log.csv</c>; a task's TIMESPENT is never adjusted to follow an edit.
    /// New From/To values are truncated to the minute, the sidecar's precision.
    /// </summary>
    public Core.Model.TimeLogEntry UpdateLogEntry(
        string? alias, Core.Model.TimeLogSelector selector, Core.Model.TimeLogEdit edit) =>
        WithLock(alias, entry =>
        {
            var normalised = new Core.Model.TimeLogEdit
            {
                From = edit.From is DateTime f ? TruncateToMinute(f) : null,
                To = edit.To is DateTime t ? TruncateToMinute(t) : null,
                Hours = edit.Hours,
                Comment = edit.Comment,
                Person = edit.Person,
                Type = edit.Type,
            };
            var log = TimeLogDocument.Load(LogPath(entry));
            var updated = log.Update(selector, normalised);
            log.Save();
            _log.LogInformation("Edited a time-log entry in list '{Alias}' ({Path}).", entry.Alias, LogPath(entry));
            return updated;
        });

    /// <summary>
    /// Deletes the single time-log entry matched by <paramref name="selector"/> and saves the sidecar.
    /// This touches only the <c>_Log.csv</c>; a task's TIMESPENT is never adjusted to follow a delete.
    /// </summary>
    public Core.Model.TimeLogEntry DeleteLogEntry(string? alias, Core.Model.TimeLogSelector selector) =>
        WithLock(alias, entry =>
        {
            var log = TimeLogDocument.Load(LogPath(entry));
            var removed = log.Delete(selector);
            log.Save();
            _log.LogInformation("Deleted a time-log entry from list '{Alias}' ({Path}).", entry.Alias, LogPath(entry));
            return removed;
        });

    /// <summary>Drops the seconds/sub-second part of a timestamp (the log stores minute precision).</summary>
    private static DateTime TruncateToMinute(DateTime dt) =>
        dt.AddTicks(-(dt.Ticks % TimeSpan.TicksPerMinute));

    /// <summary>Resolves the time-log sidecar path (<c>&lt;listname&gt;_Log.csv</c>) beside the .tdl.</summary>
    private static string LogPath(TodoFileEntry entry)
    {
        var full = Path.GetFullPath(entry.Path);
        var dir = Path.GetDirectoryName(full) ?? "";
        var name = Path.GetFileNameWithoutExtension(full);
        return Path.Combine(dir, name + "_Log.csv");
    }

    private T WithLock<T>(string? alias, Func<TodoFileEntry, T> action)
    {
        var entry = Resolve(alias);
        var key = Path.GetFullPath(entry.Path);
        var sem = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        sem.Wait();
        try
        {
            return action(entry);
        }
        finally
        {
            sem.Release();
        }
    }

    private TodoListDocument LoadOrThrow(TodoFileEntry entry)
    {
        if (!File.Exists(entry.Path))
            throw new FileNotFoundException(
                $"The file configured for list '{entry.Alias}' does not exist: {entry.Path}");

        var doc = TodoListDocument.Load(entry.Path);
        doc.ModifiedBy = _options.CurrentValue.ModifiedBy;
        return doc;
    }

    private static string Aliases(IEnumerable<TodoFileEntry> files) =>
        string.Join(", ", files.Select(f => $"'{f.Alias}'"));

    /// <summary>
    /// Guards against ambiguous hand-edited config. A duplicate alias or a second default would
    /// otherwise silently send reads and writes to whichever entry happened to be listed first,
    /// so we fail loudly instead of acting on the wrong .tdl.
    /// </summary>
    private static void ValidateConfig(IReadOnlyList<TodoFileEntry> files)
    {
        if (files.Any(f => string.IsNullOrWhiteSpace(f.Alias)))
            throw new InvalidOperationException(
                "Configuration error: every configured list needs a non-empty 'Alias'.");

        var blankPath = files.FirstOrDefault(f => string.IsNullOrWhiteSpace(f.Path));
        if (blankPath is not null)
            throw new InvalidOperationException(
                $"Configuration error: list '{blankPath.Alias}' has no 'Path'.");

        var duplicateAliases = files
            .GroupBy(f => f.Alias.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"'{g.Key}'")
            .ToList();
        if (duplicateAliases.Count > 0)
            throw new InvalidOperationException(
                $"Configuration error: duplicate list alias(es) {string.Join(", ", duplicateAliases)}. Each list needs a unique alias.");

        var defaults = files.Where(f => f.Default).ToList();
        if (defaults.Count > 1)
            throw new InvalidOperationException(
                $"Configuration error: {defaults.Count} lists are marked Default ({Aliases(defaults)}); mark only one as the default.");
    }
}
