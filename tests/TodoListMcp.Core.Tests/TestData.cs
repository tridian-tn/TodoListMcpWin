using TodoListMcp.Core;

namespace TodoListMcp.Core.Tests;

internal static class TestData
{
    /// <summary>A deterministic clock used across tests.</summary>
    public static readonly IClock Clock = new FixedClock(new DateTime(2026, 6, 17, 9, 30, 0));

    /// <summary>
    /// A small but representative document: a parent with notes and a category, plus two
    /// children — one completed with two assignees, one with a single (attribute) assignee.
    /// Includes a few "unknown" attributes (COLOR, ICONINDEX) to guard round-trip fidelity.
    /// </summary>
    public const string SampleXml =
        """
        <?xml version="1.0" encoding="utf-16"?>
        <TODOLIST PROJECTNAME="Test List" NEXTUNIQUEID="4" FILEVERSION="1" FILEFORMAT="12"><TASK ID="1" TITLE="Parent" PRIORITY="5" PERCENTDONE="0" ICONINDEX="10" COLOR="255" POS="0" POSSTRING="1"><COMMENTS>Parent notes</COMMENTS><CATEGORY>Work</CATEGORY><TASK ID="2" TITLE="Child A" PRIORITY="8" PERCENTDONE="100" DONEDATE="45916.50000000" GOODASDONE="1" POS="0" POSSTRING="1.1"><PERSON>Bob</PERSON><PERSON>Jane</PERSON></TASK><TASK ID="3" TITLE="Child B" PRIORITY="2" PERCENTDONE="0" ALLOCATEDTO="Mary" DUEDATE="45916.79166667" DUEDATESTRING="16/9/2025" POS="1" POSSTRING="1.2"/></TASK></TODOLIST>
        """;

    public static TodoListDocument Sample() => TodoListDocument.Parse(SampleXml, Clock);

    public static string SampleFilePath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Introduction.tdl");
        Assert.True(File.Exists(path), $"Sample fixture not found at {path}");
        return path;
    }

    /// <summary>
    /// A real ToDoList 9.1 export with one task per comment format (plain/rich/spreadsheet/HTML/
    /// markdown), task IDs 26-30. Used to exercise comment-format reading and the overwrite guard
    /// against genuine COMMENTSTYPE/CUSTOMCOMMENTS payloads.
    /// </summary>
    public static string MultiCommentFormatFilePath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Introduction-MultipleCommentFormats.tdl");
        Assert.True(File.Exists(path), $"Multi-format fixture not found at {path}");
        return path;
    }
}
