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

        if (string.IsNullOrWhiteSpace(alias))
        {
            var def = files.FirstOrDefault(f => f.Default) ?? (files.Count == 1 ? files[0] : null);
            return def ?? throw new InvalidOperationException(
                $"Multiple lists are configured; specify 'list'. Available: {Aliases(files)}.");
        }

        return files.FirstOrDefault(f => string.Equals(f.Alias, alias, StringComparison.OrdinalIgnoreCase))
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
            doc.Save();
            _log.LogInformation("Saved changes to list '{Alias}' ({Path}).", entry.Alias, entry.Path);
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
}
