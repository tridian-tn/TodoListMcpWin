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
