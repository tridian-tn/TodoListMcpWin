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

    private sealed class StubOptionsMonitor : IOptionsMonitor<TodoListMcpOptions>
    {
        public StubOptionsMonitor(TodoListMcpOptions value) => CurrentValue = value;
        public TodoListMcpOptions CurrentValue { get; }
        public TodoListMcpOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<TodoListMcpOptions, string?> listener) => null;
    }
}
