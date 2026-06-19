using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TodoListMcp.App;
using TodoListMcp.App.Mcp;

namespace TodoListMcp.App.Tests;

/// <summary>
/// Covers the MCP tool layer's handling of <c>commentsFormat</c>: it is validated only when
/// <c>comments</c> is supplied (so an unused/invalid format never throws), and a supplied format
/// authors the task end-to-end through the tool surface.
/// </summary>
public class TodoToolsTests
{
    private const string MinimalTdl =
        "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
        "<TODOLIST PROJECTNAME=\"T\" NEXTUNIQUEID=\"2\">" +
        "<TASK ID=\"1\" TITLE=\"A\" POS=\"0\" POSSTRING=\"1\"/></TODOLIST>";

    private static (TodoTools tools, string dir) NewTools()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tdlmcp_tools_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "list.tdl");
        File.WriteAllText(path, MinimalTdl, Encoding.Unicode);
        var options = new TodoListMcpOptions { Files = new() { new TodoFileEntry { Alias = "work", Path = path } } };
        var manager = new TodoListManager(new StubOptionsMonitor(options), NullLogger<TodoListManager>.Instance);
        return (new TodoTools(manager), dir);
    }

    [Fact]
    public void AddTask_ignores_an_invalid_commentsFormat_when_no_comments_given()
    {
        var (tools, dir) = NewTools();
        try
        {
            var t = tools.AddTask(title: "X", commentsFormat: "bogus", list: "work");   // comments omitted
            Assert.Equal("X", t.Title);   // format is irrelevant here, so it must not throw
            Assert.Null(t.CommentsFormat);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void AddTask_validates_commentsFormat_when_comments_are_given()
    {
        var (tools, dir) = NewTools();
        try
        {
            Assert.Throws<ArgumentException>(
                () => tools.AddTask(title: "X", comments: "hi", commentsFormat: "bogus", list: "work"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void AddTask_authors_markdown_through_the_tool_surface()
    {
        var (tools, dir) = NewTools();
        try
        {
            var t = tools.AddTask(title: "X", comments: "**hi**", commentsFormat: "markdown", list: "work");
            Assert.Equal("markdown", t.CommentsFormat);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void UpdateTask_ignores_an_invalid_commentsFormat_when_no_comments_given()
    {
        var (tools, dir) = NewTools();
        try
        {
            var t = tools.UpdateTask(id: 1, title: "renamed", commentsFormat: "bogus", list: "work");
            Assert.Equal("renamed", t.Title);   // editing unrelated fields must not validate the format
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private sealed class StubOptionsMonitor : IOptionsMonitor<TodoListMcpOptions>
    {
        public StubOptionsMonitor(TodoListMcpOptions value) => CurrentValue = value;
        public TodoListMcpOptions CurrentValue { get; }
        public TodoListMcpOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<TodoListMcpOptions, string?> listener) => null;
    }
}
