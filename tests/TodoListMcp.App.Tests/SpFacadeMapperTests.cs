using System.Text.Json;
using System.Text.Json.Nodes;
using TodoListMcp.App.Sp;
using TodoListMcp.Core;
using Xunit;

namespace TodoListMcp.App.Tests;

/// <summary>
/// Pins the SP v1 contract shape produced by <see cref="SpIssueMapper"/>. The host is a WinForms+Web
/// hybrid so we test the mapper directly against a parsed fixture rather than booting Kestrel.
/// Fixture: Parent(1, open, category Work, 2 subtasks) → Child A(2, done, Bob+Jane) and
/// Child B(3, open, Mary, due 2025-09-16).
/// </summary>
public sealed class SpFacadeMapperTests
{
    private const string Xml =
        """
        <?xml version="1.0" encoding="utf-16"?>
        <TODOLIST PROJECTNAME="Test List" NEXTUNIQUEID="4" FILEVERSION="1" FILEFORMAT="12"><TASK ID="1" TITLE="Parent" PRIORITY="5" PERCENTDONE="0" CREATIONDATE="45900.5" LASTMOD="45901.25" POS="0" POSSTRING="1"><COMMENTS>Parent notes</COMMENTS><CATEGORY>Work</CATEGORY><TASK ID="2" TITLE="Child A" PRIORITY="8" PERCENTDONE="100" DONEDATE="45916.50000000" GOODASDONE="1" POS="0" POSSTRING="1.1"><PERSON>Bob</PERSON><PERSON>Jane</PERSON></TASK><TASK ID="3" TITLE="Child B" PRIORITY="2" PERCENTDONE="0" ALLOCATEDTO="Mary" DUEDATE="45916.79166667" DUEDATESTRING="16/9/2025" POS="1" POSSTRING="1.2"/></TASK></TODOLIST>
        """;

    private static TodoListDocument Doc() => TodoListDocument.Parse(Xml);

    [Fact]
    public void OpenSummaries_excludeDone_andFlattenSubtasks()
    {
        var open = SpIssueMapper.Summaries(Doc(), "open", null, 200);
        Assert.Equal(new[] { "1", "3" }, open.Select(s => s.Id).ToArray());
    }

    [Fact]
    public void DoneAndAll_filterByCompletion()
    {
        Assert.Equal(new[] { "2" }, SpIssueMapper.Summaries(Doc(), "done", null, 200).Select(s => s.Id).ToArray());
        Assert.Equal(3, SpIssueMapper.Summaries(Doc(), "all", null, 200).Count);
    }

    [Fact]
    public void Query_matchesTitle_caseInsensitive_withinStatus()
    {
        Assert.Equal(new[] { "2", "3" },
            SpIssueMapper.Summaries(Doc(), "all", "child", 200).Select(s => s.Id).ToArray());
        Assert.Equal(new[] { "3" },
            SpIssueMapper.Summaries(Doc(), "open", "CHILD", 200).Select(s => s.Id).ToArray());
    }

    [Fact]
    public void Limit_capsResults()
    {
        Assert.Single(SpIssueMapper.Summaries(Doc(), "all", null, 1));
    }

    [Fact]
    public void Detail_parent_mapsTreeCategoriesAndCreatedAt()
    {
        var p = SpIssueMapper.Detail(Doc(), 1)!;
        Assert.Null(p.ParentName);
        Assert.Equal(2, p.NumSubtasks);
        Assert.Equal(new[] { "Work" }, p.Labels.ToArray());
        Assert.Equal("Parent notes", p.Body);
        Assert.Equal(5, p.Priority);
        Assert.False(p.IsDone);
        Assert.Equal("open", p.State);
        Assert.NotNull(p.CreatedAt);
    }

    [Fact]
    public void Detail_doneChild_joinsAssignees()
    {
        var c = SpIssueMapper.Detail(Doc(), 2)!;
        Assert.True(c.IsDone);
        Assert.Equal("done", c.State);
        Assert.Equal("Bob, Jane", c.Assignee);
    }

    [Fact]
    public void Detail_unknownId_returnsNull()
    {
        Assert.Null(SpIssueMapper.Detail(Doc(), 999));
    }

    [Fact]
    public void Detail_childB_matchesGoldenJson()
    {
        var issue = SpIssueMapper.Detail(Doc(), 3)!;
        var actual = JsonNode.Parse(JsonSerializer.Serialize(issue, SpContracts.Json));
        var expected = JsonNode.Parse(
            """
            {
              "id": "3", "title": "Child B", "body": "", "isDone": false, "state": "open", "url": "",
              "lastUpdated": null, "createdAt": null, "dueOn": "2025-09-16", "assignee": "Mary",
              "labels": [], "priority": 2, "projectNames": "Test List", "parentName": "Parent",
              "numSubtasks": 0
            }
            """);
        Assert.True(JsonNode.DeepEquals(expected, actual), $"actual: {actual?.ToJsonString()}");
    }

    [Fact]
    public void Detail_dates_reconstructToLocalWallClock()
    {
        var p = SpIssueMapper.Detail(Doc(), 1)!;
        Assert.NotNull(p.CreatedAt);
        Assert.NotNull(p.LastUpdated);
        // Reconstruct-and-compare: pins scale (ms), base (Unix epoch) and kind (Local) without
        // hard-coding a timezone-dependent literal.
        Assert.Equal(DateTime.FromOADate(45900.5),
            DateTimeOffset.FromUnixTimeMilliseconds(p.CreatedAt!.Value).LocalDateTime);
        Assert.Equal(DateTime.FromOADate(45901.25),
            DateTimeOffset.FromUnixTimeMilliseconds(p.LastUpdated!.Value).LocalDateTime);
    }

    [Fact]
    public void Detail_unsetPriority_isNull()
    {
        var doc = TodoListDocument.Parse(
            """
            <?xml version="1.0" encoding="utf-16"?>
            <TODOLIST PROJECTNAME="P" NEXTUNIQUEID="2" FILEVERSION="1" FILEFORMAT="12"><TASK ID="1" TITLE="No priority" PERCENTDONE="0" POS="0" POSSTRING="1"/></TODOLIST>
            """);
        Assert.Null(SpIssueMapper.Detail(doc, 1)!.Priority);
    }

    [Fact]
    public void Detail_multipleCategories_allBecomeLabels()
    {
        var doc = TodoListDocument.Parse(
            """
            <?xml version="1.0" encoding="utf-16"?>
            <TODOLIST PROJECTNAME="P" NEXTUNIQUEID="2" FILEVERSION="1" FILEFORMAT="12"><TASK ID="1" TITLE="T" PERCENTDONE="0" POS="0" POSSTRING="1"><CATEGORY>Work</CATEGORY><CATEGORY>Home</CATEGORY></TASK></TODOLIST>
            """);
        Assert.Equal(new[] { "Work", "Home" }, SpIssueMapper.Detail(doc, 1)!.Labels.ToArray());
    }

    [Fact]
    public void Summaries_blankQuery_returnsAll()
    {
        Assert.Equal(3, SpIssueMapper.Summaries(Doc(), "all", "", 200).Count);
        Assert.Equal(3, SpIssueMapper.Summaries(Doc(), "all", "   ", 200).Count);
    }

    [Fact]
    public void Detail_grandchild_parentIsImmediateParent()
    {
        var doc = TodoListDocument.Parse(
            """
            <?xml version="1.0" encoding="utf-16"?>
            <TODOLIST PROJECTNAME="P" NEXTUNIQUEID="4" FILEVERSION="1" FILEFORMAT="12"><TASK ID="1" TITLE="Root" PERCENTDONE="0" POS="0" POSSTRING="1"><TASK ID="2" TITLE="Mid" PERCENTDONE="0" POS="0" POSSTRING="1.1"><TASK ID="3" TITLE="Leaf" PERCENTDONE="0" POS="0" POSSTRING="1.1.1"/></TASK></TASK></TODOLIST>
            """);
        var leaf = SpIssueMapper.Detail(doc, 3)!;
        Assert.Equal("Mid", leaf.ParentName);
        Assert.Equal(0, leaf.NumSubtasks);
        var mid = SpIssueMapper.Detail(doc, 2)!;
        Assert.Equal("Root", mid.ParentName);
        Assert.Equal(1, mid.NumSubtasks);
    }

    [Fact]
    public void Summaries_goodAsDoneButNoDoneDate_staysOpen()
    {
        // Pins the DONEDATE-only completion rule: a parent flagged GOODASDONE with no DONEDATE is open.
        var doc = TodoListDocument.Parse(
            """
            <?xml version="1.0" encoding="utf-16"?>
            <TODOLIST PROJECTNAME="P" NEXTUNIQUEID="3" FILEVERSION="1" FILEFORMAT="12"><TASK ID="1" TITLE="Rollup" PERCENTDONE="100" GOODASDONE="1" POS="0" POSSTRING="1"><TASK ID="2" TITLE="Kid" PERCENTDONE="100" DONEDATE="45916.5" POS="0" POSSTRING="1.1"/></TASK></TODOLIST>
            """);
        var open = SpIssueMapper.Summaries(doc, "open", null, 200).Select(s => s.Id).ToArray();
        Assert.Contains("1", open);
        Assert.DoesNotContain("2", open);
    }

    [Fact]
    public void Summary_shape_keepsNullFieldsPresent()
    {
        var doc = TodoListDocument.Parse(
            """
            <?xml version="1.0" encoding="utf-16"?>
            <TODOLIST PROJECTNAME="P" NEXTUNIQUEID="2" FILEVERSION="1" FILEFORMAT="12"><TASK ID="1" TITLE="Bare" PERCENTDONE="0" POS="0" POSSTRING="1"/></TODOLIST>
            """);
        var s = SpIssueMapper.Summaries(doc, "all", null, 200).Single();
        var actual = JsonNode.Parse(JsonSerializer.Serialize(s, SpContracts.Json));
        var expected = JsonNode.Parse(
            """
            {"id":"1","title":"Bare","status":"open","isDone":false,"assignee":null,"dueDate":null,"url":""}
            """);
        Assert.True(JsonNode.DeepEquals(expected, actual), $"actual: {actual?.ToJsonString()}");
    }
}
