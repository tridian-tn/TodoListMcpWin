using System.Text;
using TodoListMcp.Core;

namespace TodoListMcp.Core.Tests;

public class PersistenceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tdlmcp_tests_" + Guid.NewGuid().ToString("N"));

    public PersistenceTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ } }

    [Fact]
    public void Saved_file_is_utf16_with_bom_and_declares_utf16()
    {
        var path = Path.Combine(_dir, "out.tdl");
        var doc = TestData.Sample();
        doc.SaveAs(path);

        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE, "expected UTF-16 LE BOM");

        var text = File.ReadAllText(path, Encoding.Unicode);
        Assert.Contains("encoding=\"utf-16\"", text);
    }

    [Fact]
    public void Save_then_load_preserves_tasks()
    {
        var path = Path.Combine(_dir, "out.tdl");
        var doc = TestData.Sample();
        doc.AddTask(new() { Title = "Added", ParentId = 1 });
        doc.SaveAs(path);

        var reloaded = TodoListDocument.Load(path);
        Assert.Equal("Test List", reloaded.ProjectName);
        Assert.Equal(3, reloaded.GetTask(1)!.Subtasks.Count);
        Assert.Equal("Added", reloaded.GetTask(reloaded.NextUniqueId!.Value - 1)!.Title);
    }

    [Fact]
    public void Save_is_atomic_no_tmp_left_behind()
    {
        var path = Path.Combine(_dir, "out.tdl");
        TestData.Sample().SaveAs(path);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Round_trip_through_string_preserves_structure()
    {
        var doc = TestData.Sample();
        var xml = doc.ToXmlString();
        var reparsed = TodoListDocument.Parse(xml);

        Assert.Equal(doc.GetTasks().Count, reparsed.GetTasks().Count);
        Assert.Equal("Parent notes", reparsed.GetTask(1)!.Comments);
    }

    [Fact]
    public void Invalid_root_is_rejected()
    {
        Assert.Throws<InvalidTdlException>(() =>
            TodoListDocument.Parse("<?xml version=\"1.0\"?><NOTATODOLIST/>"));
    }
}
