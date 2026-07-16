using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using TodoListMcp.App;
using TodoListMcp.App.Mcp;
using TodoListMcp.Core;

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

    [Fact]
    public void File_links_round_trip_through_the_tool_surface_and_disk()
    {
        // The manager persists to the .tdl file on every write and reloads on every read, so adding
        // then reading file links through the tools is a genuine write-to-file / read-back round-trip.
        var (tools, dir) = NewTools();
        try
        {
            tools.AddTask(
                title: "Linked",
                fileLinks: new[] { @".\Evidence\doors.jpg", "https://example.com/?a=1&b=2" },
                list: "work");

            var added = tools.GetTask(id: 2, list: "work");
            Assert.Equal(
                new[] { @".\Evidence\doors.jpg", "https://example.com/?a=1&b=2" },
                added.FileLinks);

            // Replacing through update_task also persists and reads back.
            tools.UpdateTask(id: 2, fileLinks: new[] { @"\\server\share\plan.pdf" }, list: "work");
            Assert.Equal(new[] { @"\\server\share\plan.pdf" }, tools.GetTask(id: 2, list: "work").FileLinks);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void GetTask_surfaces_the_editable_source_for_an_authored_markdown_task()
    {
        // Issue #30: the source authored through the tool surface comes back via CommentsSource on a
        // subsequent read (distinct from the flattened Comments mirror), closing the round-trip loop.
        var (tools, dir) = NewTools();
        try
        {
            var source = "# Plan\n\n- **a**\n- _b_";
            tools.AddTask(title: "X", comments: source, commentsFormat: "markdown", list: "work");

            var read = tools.GetTask(id: 2, list: "work");
            Assert.Equal("markdown", read.CommentsFormat);
            Assert.Equal(source, read.CommentsSource);
            Assert.NotEqual(source, read.Comments);   // mirror is the flattened text, not the source
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void GetTimeLog_single_day_range_includes_entries_logged_that_day()
    {
        // Regression: a bare "until" date was treated as midnight, so since=until=today (the natural
        // "what did I log today?" query) excluded every entry whose start time was after 00:00.
        var (tools, dir) = NewTools();
        try
        {
            tools.LogTime(hours: 2, taskId: 1, when: "2026-06-26 14:00", comment: "afternoon work", list: "work");

            var sameDay = tools.GetTimeLog(since: "2026-06-26", until: "2026-06-26", list: "work");
            var entry = Assert.Single(sameDay);
            Assert.Equal(2.0, entry.Hours, 3);

            // An explicit upper-bound time is honoured verbatim (not bumped to end of day): the entry
            // starts at 12:00 (when − hours), so an 11:00 bound excludes it.
            Assert.Empty(tools.GetTimeLog(since: "2026-06-26", until: "2026-06-26 11:00", list: "work"));

            // Even an explicit midnight is verbatim — it must not be mistaken for a bare date and
            // bumped to end-of-day, so it excludes the 12:00-start entry.
            Assert.Empty(tools.GetTimeLog(since: "2026-06-26", until: "2026-06-26 00:00", list: "work"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void UpdateTimeLogEntry_moves_an_entry_through_the_tool_surface()
    {
        var (tools, dir) = NewTools();
        try
        {
            tools.LogTime(hours: 8, taskId: 1, from: "2026-06-29 09:08", to: "2026-06-29 17:08",
                comment: "NOTHING DONE", list: "work");

            var updated = tools.UpdateTimeLogEntry(
                taskId: 1, comment: "NOTHING DONE",
                newFrom: "2026-06-29 10:00", newTo: "2026-06-29 18:00", list: "work");
            Assert.Equal(new DateTime(2026, 6, 29, 10, 0, 0), updated.From);

            var read = Assert.Single(tools.GetTimeLog(taskId: 1, list: "work"));
            Assert.Equal(new DateTime(2026, 6, 29, 10, 0, 0), read.From);
            Assert.Equal(new DateTime(2026, 6, 29, 18, 0, 0), read.To);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void DeleteTimeLogEntry_removes_an_entry_through_the_tool_surface()
    {
        var (tools, dir) = NewTools();
        try
        {
            tools.LogTime(hours: 0, comment: "scratch", list: "work");
            var removed = tools.DeleteTimeLogEntry(taskId: 0, comment: "scratch", list: "work");
            Assert.Equal("scratch", removed.Comment);
            Assert.Empty(tools.GetTimeLog(list: "work"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void SetRecurrence_authors_a_rule_through_the_tool_surface()
    {
        var (tools, dir) = NewTools();
        try
        {
            var t = tools.SetRecurrence(id: 1, pattern: "weeklyOnDays", interval: 2,
                daysOfWeek: new[] { "Mon", "Thu" }, recalcFrom: "startDate", onRecur: "createNew", list: "work");
            Assert.Equal("weeklyOnDays", t.Recurrence!.Pattern);
            Assert.Equal(new[] { "Monday", "Thursday" }, t.Recurrence.DaysOfWeek);
            Assert.Equal(2, t.Recurrence.Interval);
            Assert.Equal("startDate", t.Recurrence.RecalculateFrom);
            Assert.Equal("createNew", t.Recurrence.OnRecur);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void SetRecurrence_rejects_an_unknown_pattern()
    {
        var (tools, dir) = NewTools();
        try
        {
            Assert.Throws<ArgumentException>(() => tools.SetRecurrence(id: 1, pattern: "fortnightly", list: "work"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void SetRecurrence_rejects_a_deferred_pattern_with_guidance()
    {
        var (tools, dir) = NewTools();
        try
        {
            // The by-weekday patterns are readable but not authorable yet.
            var ex = Assert.Throws<ArgumentException>(
                () => tools.SetRecurrence(id: 1, pattern: "monthlyByWeekday", list: "work"));
            Assert.Contains("read but not authored", ex.Message);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void SetRecurrence_rejects_an_unknown_recalcFrom()
    {
        var (tools, dir) = NewTools();
        try
        {
            Assert.Throws<ArgumentException>(() =>
                tools.SetRecurrence(id: 1, pattern: "everyNDays", interval: 1, recalcFrom: "whenever", list: "work"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void SetRecurrence_rejects_an_unknown_onRecur()
    {
        var (tools, dir) = NewTools();
        try
        {
            Assert.Throws<ArgumentException>(() =>
                tools.SetRecurrence(id: 1, pattern: "everyNDays", interval: 1, onRecur: "clone", list: "work"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void CompleteTask_refuses_a_recurring_task_through_the_tool_surface()
    {
        var (tools, dir) = NewTools();
        try
        {
            tools.SetRecurrence(id: 1, pattern: "everyNDays", interval: 1, list: "work");
            Assert.Throws<RecurringTaskCompletionException>(() => tools.CompleteTask(id: 1, list: "work"));
            // After clearing the recurrence, completion goes through.
            tools.ClearRecurrence(id: 1, list: "work");
            Assert.True(tools.CompleteTask(id: 1, list: "work").IsDone);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ClearRecurrence_removes_a_rule_through_the_tool_surface()
    {
        var (tools, dir) = NewTools();
        try
        {
            tools.SetRecurrence(id: 1, pattern: "everyNDays", interval: 3, list: "work");
            var t = tools.ClearRecurrence(id: 1, list: "work");
            Assert.Null(t.Recurrence);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ---- Custom fields -------------------------------------------------------

    // A list defining an MR (string) field, a TAGS fixed multi-select and a TOTAL calculation.
    private const string CustomFieldTdl =
        "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
        "<TODOLIST PROJECTNAME=\"T\" NEXTUNIQUEID=\"2\">" +
        "<CUSTOMATTRIBDEF ID=\"MR\" LABEL=\"Merge Request\" TYPE=\"0\" ENABLED=\"1\"/>" +
        "<CUSTOMATTRIBDEF ID=\"TAGS\" LABEL=\"Tags\" TYPE=\"1024\" ENABLED=\"1\"/>" +
        "<CUSTOMATTRIBDEF ID=\"TOTAL\" LABEL=\"Total\" TYPE=\"9\" ENABLED=\"1\"/>" +
        "<TASK ID=\"1\" TITLE=\"A\" POS=\"0\" POSSTRING=\"1\"/></TODOLIST>";

    private static (TodoTools tools, string dir) NewToolsWithCustomFields()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tdlmcp_cf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "list.tdl");
        File.WriteAllText(path, CustomFieldTdl, Encoding.Unicode);
        var options = new TodoListMcpOptions { Files = new() { new TodoFileEntry { Alias = "work", Path = path } } };
        var manager = new TodoListManager(new StubOptionsMonitor(options), NullLogger<TodoListManager>.Instance);
        return (new TodoTools(manager), dir);
    }

    [Fact]
    public void AddTask_sets_a_custom_field_through_the_tool_surface()
    {
        var (tools, dir) = NewToolsWithCustomFields();
        try
        {
            var t = tools.AddTask(
                title: "X",
                customFields: new Dictionary<string, string> { ["MR"] = "12345" },
                list: "work");
            var mr = Assert.Single(t.CustomFields);
            Assert.Equal("MR", mr.Id);
            Assert.Equal("12345", mr.Value);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void UpdateTask_clears_a_custom_field_with_an_empty_value()
    {
        var (tools, dir) = NewToolsWithCustomFields();
        try
        {
            tools.UpdateTask(id: 1, customFields: new Dictionary<string, string> { ["MR"] = "99" }, list: "work");
            var cleared = tools.UpdateTask(id: 1, customFields: new Dictionary<string, string> { ["MR"] = "" }, list: "work");
            Assert.Empty(cleared.CustomFields);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void UpdateTask_rejects_a_calculation_custom_field_through_the_tool_surface()
    {
        var (tools, dir) = NewToolsWithCustomFields();
        try
        {
            Assert.Throws<ArgumentException>(() => tools.UpdateTask(
                id: 1, customFields: new Dictionary<string, string> { ["TOTAL"] = "5" }, list: "work"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void SearchTasks_filters_by_a_custom_field_value()
    {
        var (tools, dir) = NewToolsWithCustomFields();
        try
        {
            tools.UpdateTask(id: 1, customFields: new Dictionary<string, string> { ["MR"] = "12345" }, list: "work");
            Assert.Single(tools.SearchTasks(customFieldId: "MR", customFieldValue: "12345", list: "work"));
            Assert.Empty(tools.SearchTasks(customFieldId: "MR", customFieldValue: "nope", list: "work"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void AddTask_round_trips_a_multi_select_value_through_the_tool_surface()
    {
        // The newline-joined dictionary value must survive the write-to-disk + reload at the tool
        // boundary (the &#xA; character-reference round-trip), not just in memory.
        var (tools, dir) = NewToolsWithCustomFields();
        try
        {
            var added = tools.AddTask(
                title: "X",
                customFields: new Dictionary<string, string> { ["TAGS"] = "alpha\ngamma" },
                list: "work");
            var reread = tools.GetTask(id: added.Id, list: "work");
            Assert.Equal(new[] { "alpha", "gamma" }, reread.CustomFields.Single(c => c.Id == "TAGS").Values);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void GetCustomFields_lists_the_definitions()
    {
        var (tools, dir) = NewToolsWithCustomFields();
        try
        {
            var defs = tools.GetCustomFields(list: "work");
            Assert.Equal(new[] { "MR", "TAGS", "TOTAL" }, defs.Select(d => d.Id).OrderBy(x => x).ToArray());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Theory]
    [InlineData(nameof(TodoTools.AddTask))]
    [InlineData(nameof(TodoTools.UpdateTask))]
    public void Tool_with_customFields_param_builds_a_valid_schema(string methodName)
    {
        // The server registers each tool at startup by building an McpServerTool from the method; if the
        // Dictionary<string,string> customFields parameter had no JSON-schema mapping this would throw,
        // and the server would fail to start. Prove it builds and exposes the parameter.
        var (tools, dir) = NewToolsWithCustomFields();
        try
        {
            var method = typeof(TodoTools).GetMethod(methodName)!;
            var tool = McpServerTool.Create(method, tools);
            var schema = tool.ProtocolTool.InputSchema.ToString();
            Assert.Contains("customFields", schema);
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
