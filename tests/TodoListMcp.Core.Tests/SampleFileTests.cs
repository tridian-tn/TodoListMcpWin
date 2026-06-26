using TodoListMcp.Core;
using TodoListMcp.Core.Model;

namespace TodoListMcp.Core.Tests;

/// <summary>
/// Exercises the parser against a real ToDoList file exported by the application
/// (AbstractSpoon ToDoList 9.0, FILEFORMAT 12), not a hand-written fixture.
/// </summary>
public class SampleFileTests
{
    private static TodoListDocument LoadSample() => TodoListDocument.Load(TestData.SampleFilePath());

    [Fact]
    public void Loads_real_file_and_reads_project_metadata()
    {
        var doc = LoadSample();
        Assert.Equal("Sample Tasklist", doc.ProjectName);
        Assert.NotEmpty(doc.GetTasks());
    }

    [Fact]
    public void Reads_nested_subtask_hierarchy()
    {
        var doc = LoadSample();
        Assert.Contains(doc.GetTasks(), t => t.Subtasks.Count > 0);
    }

    [Fact]
    public void Detects_completed_tasks_via_done_date()
    {
        var doc = LoadSample();
        var all = Flatten(doc.GetTasks()).ToList();
        Assert.Contains(all, t => t.IsDone);
        Assert.Contains(all, t => !t.IsDone);
    }

    [Fact]
    public void Root_level_master_lists_are_not_leaked_onto_tasks()
    {
        // The file defines global CATEGORY/PERSON pick-lists as children of <TODOLIST>
        // ("Work", "Bob", ...). None of the tasks in this sample have assignments, so a
        // correct, element-scoped reader must report every task as having none — a naive
        // descendant search would wrongly attribute the master lists to tasks.
        var doc = LoadSample();
        foreach (var t in Flatten(doc.GetTasks()))
        {
            Assert.Empty(t.Categories);
            Assert.Empty(t.AllocatedTo);
        }
    }

    [Fact]
    public void Reads_file_links_from_real_file()
    {
        var doc = LoadSample();
        var all = Flatten(doc.GetTasks()).ToList();

        // The fixture attaches Evidence Board photos to several tasks via <FILEREFPATH>.
        Assert.Contains(all, t => t.FileLinks.Contains(@".\Evidence Board Photos\doors.jpg"));
        Assert.Equal(5, all.Sum(t => t.FileLinks.Count));
    }

    [Fact]
    public void File_links_survive_load_save_reload()
    {
        var doc = LoadSample();
        var linked = Flatten(doc.GetTasks()).First(t => t.FileLinks.Count > 0);
        var expected = linked.FileLinks;

        var tmp = Path.Combine(Path.GetTempPath(), "tdlmcp_links_" + Guid.NewGuid().ToString("N") + ".tdl");
        try
        {
            doc.SaveAs(tmp);
            var reloaded = TodoListDocument.Load(tmp);
            Assert.Equal(expected, reloaded.GetTask(linked.Id)!.FileLinks);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Real_file_survives_load_modify_save_reload()
    {
        var doc = LoadSample();
        var first = doc.GetTasks()[0];

        doc.UpdateTask(first.Id, new() { Comments = "Touched by MCP" });

        var tmp = Path.Combine(Path.GetTempPath(), "tdlmcp_sample_" + Guid.NewGuid().ToString("N") + ".tdl");
        try
        {
            doc.SaveAs(tmp);
            var reloaded = TodoListDocument.Load(tmp);
            Assert.Equal("Touched by MCP", reloaded.GetTask(first.Id)!.Comments);
            // Project-level metadata is intact after the round-trip.
            Assert.Equal("Sample Tasklist", reloaded.ProjectName);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    private static IEnumerable<TodoTask> Flatten(IEnumerable<TodoTask> tasks)
    {
        foreach (var t in tasks)
        {
            yield return t;
            foreach (var c in Flatten(t.Subtasks)) yield return c;
        }
    }
}
