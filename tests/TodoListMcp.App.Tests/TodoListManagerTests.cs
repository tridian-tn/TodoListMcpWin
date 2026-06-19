using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TodoListMcp.App;

namespace TodoListMcp.App.Tests;

/// <summary>
/// Covers the alias-resolution rules in <see cref="TodoListManager.Resolve"/>: which file a tool call
/// lands on when the caller omits the alias, and the error paths for ambiguous/unknown/empty config.
/// </summary>
public class TodoListManagerTests
{
    private static TodoFileEntry Entry(string alias, bool @default = false) =>
        new() { Alias = alias, Path = $@"C:\lists\{alias}.tdl", Default = @default };

    private static TodoListManager Manager(params TodoFileEntry[] files)
    {
        var options = new TodoListMcpOptions { Files = files.ToList() };
        return new TodoListManager(new StubOptionsMonitor(options), NullLogger<TodoListManager>.Instance);
    }

    [Fact]
    public void Resolve_single_file_without_alias_returns_that_file()
    {
        var manager = Manager(Entry("work"));
        Assert.Equal("work", manager.Resolve(null).Alias);
    }

    [Fact]
    public void Resolve_without_alias_prefers_the_default_among_many()
    {
        var manager = Manager(Entry("work"), Entry("home", @default: true), Entry("side"));
        Assert.Equal("home", manager.Resolve(null).Alias);
    }

    [Fact]
    public void Resolve_without_alias_when_many_and_no_default_throws()
    {
        var manager = Manager(Entry("work"), Entry("home"));
        var ex = Assert.Throws<InvalidOperationException>(() => manager.Resolve(null));
        Assert.Contains("specify 'list'", ex.Message);
    }

    [Fact]
    public void Resolve_matches_alias_case_insensitively()
    {
        var manager = Manager(Entry("Work"));
        Assert.Equal("Work", manager.Resolve("wOrK").Alias);
    }

    [Fact]
    public void Resolve_matches_alias_ignoring_surrounding_whitespace()
    {
        // A padded alias passes validation (it trims), so it must also be resolvable.
        var manager = Manager(new TodoFileEntry { Alias = "work ", Path = @"C:\lists\work.tdl" });
        Assert.Equal("work ", manager.Resolve("work").Alias);
    }

    [Fact]
    public void Resolve_unknown_alias_throws_and_lists_available()
    {
        var manager = Manager(Entry("work"), Entry("home"));
        var ex = Assert.Throws<InvalidOperationException>(() => manager.Resolve("missing"));
        Assert.Contains("Unknown list 'missing'", ex.Message);
        Assert.Contains("'work'", ex.Message);
        Assert.Contains("'home'", ex.Message);
    }

    [Fact]
    public void Resolve_with_no_files_configured_throws()
    {
        var manager = Manager();
        var ex = Assert.Throws<InvalidOperationException>(() => manager.Resolve(null));
        Assert.Contains("No ToDoList files are configured", ex.Message);
    }

    [Fact]
    public void Files_exposes_the_configured_entries()
    {
        var manager = Manager(Entry("work"), Entry("home"));
        Assert.Equal(new[] { "work", "home" }, manager.Files.Select(f => f.Alias));
    }

    [Fact]
    public void Resolve_with_duplicate_alias_throws()
    {
        var manager = Manager(Entry("work"), Entry("WORK"));
        var ex = Assert.Throws<InvalidOperationException>(() => manager.Resolve("work"));
        Assert.Contains("duplicate", ex.Message);
        Assert.Contains("'work'", ex.Message);
    }

    [Fact]
    public void Resolve_with_two_defaults_throws()
    {
        var manager = Manager(Entry("work", @default: true), Entry("home", @default: true));
        var ex = Assert.Throws<InvalidOperationException>(() => manager.Resolve(null));
        Assert.Contains("Default", ex.Message);
    }

    [Fact]
    public void Resolve_with_blank_alias_throws()
    {
        var manager = Manager(Entry("work"), new TodoFileEntry { Alias = "  ", Path = @"C:\lists\x.tdl" });
        var ex = Assert.Throws<InvalidOperationException>(() => manager.Resolve("work"));
        Assert.Contains("Alias", ex.Message);
    }

    [Fact]
    public void Resolve_with_blank_path_throws()
    {
        var manager = Manager(new TodoFileEntry { Alias = "work", Path = "" });
        var ex = Assert.Throws<InvalidOperationException>(() => manager.Resolve("work"));
        Assert.Contains("Path", ex.Message);
        Assert.Contains("'work'", ex.Message);
    }

    [Fact]
    public void Write_skips_save_when_the_mutation_changed_nothing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tdlmcp_apptests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "list.tdl");
            File.WriteAllText(path, MinimalTdl, Encoding.Unicode);
            var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(path, stamp);

            var manager = Manager(new TodoFileEntry { Alias = "work", Path = path });

            // Deleting a non-existent ID is a no-op; the file must not be rewritten.
            var deleted = manager.Write("work", d => d.DeleteTask(999));
            Assert.False(deleted);
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));

            // A real delete does rewrite the file, so the timestamp moves off the sentinel.
            var reallyDeleted = manager.Write("work", d => d.DeleteTask(1));
            Assert.True(reallyDeleted);
            Assert.NotEqual(stamp, File.GetLastWriteTimeUtc(path));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    private const string MinimalTdl =
        "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
        "<TODOLIST PROJECTNAME=\"T\" NEXTUNIQUEID=\"2\">" +
        "<TASK ID=\"1\" TITLE=\"A\" POS=\"0\" POSSTRING=\"1\"/></TODOLIST>";

    private sealed class StubOptionsMonitor : IOptionsMonitor<TodoListMcpOptions>
    {
        public StubOptionsMonitor(TodoListMcpOptions value) => CurrentValue = value;
        public TodoListMcpOptions CurrentValue { get; }
        public TodoListMcpOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<TodoListMcpOptions, string?> listener) => null;
    }
}
