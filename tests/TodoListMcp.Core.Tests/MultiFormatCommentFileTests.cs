using System.Text;
using System.Xml.Linq;
using TodoListMcp.Core;
using TodoListMcp.Core.Model;

namespace TodoListMcp.Core.Tests;

/// <summary>
/// Exercises comment-format handling against a real ToDoList 9.1 export (FILEFORMAT 12) that holds
/// one task per comment format — plain/rich/spreadsheet/HTML/markdown, task IDs 26-30. This pins the
/// COMMENTSTYPE→friendly mapping, the read-side <see cref="TodoTask.CommentsFormat"/>, and the
/// overwrite guard to genuine ToDoList payloads rather than hand-written XML.
/// </summary>
public class MultiFormatCommentFileTests
{
    private static TodoListDocument Load() => TodoListDocument.Load(TestData.MultiCommentFormatFilePath());

    private static readonly int[] FormattedIds = { 27, 28, 29, 30 };   // everything except plain (26)

    [Theory]
    [InlineData(26, "plain")]
    [InlineData(27, "rich")]
    [InlineData(28, "spreadsheet")]
    [InlineData(29, "html")]
    [InlineData(30, "markdown")]
    public void Reports_each_real_format_with_its_friendly_name(int id, string expected) =>
        Assert.Equal(expected, Load().GetTask(id)!.CommentsFormat);

    [Fact]
    public void Plain_task_exposes_its_notes_verbatim()
    {
        var t = Load().GetTask(26)!;
        Assert.Equal("plain", t.CommentsFormat);
        Assert.StartsWith("Lorem ipsum dolor", t.Comments);
    }

    [Fact]
    public void Formatted_tasks_expose_a_non_empty_plain_mirror()
    {
        var doc = Load();
        foreach (var id in FormattedIds)
            Assert.False(string.IsNullOrEmpty(doc.GetTask(id)!.Comments), $"task {id} should have a mirror");
    }

    [Theory]
    [InlineData(27)]
    [InlineData(28)]
    [InlineData(29)]
    [InlineData(30)]
    public void Overwriting_a_real_formatted_task_is_refused_without_opt_in(int id)
    {
        var doc = Load();
        var formatBefore = doc.GetTask(id)!.CommentsFormat;
        var payloadBefore = CustomCommentsByIdFromXml(doc.ToXmlString())[id];

        var ex = Assert.Throws<FormattedCommentsException>(() => doc.UpdateTask(id, new() { Comments = "replaced" }));

        Assert.Equal(id, ex.TaskId);
        // The refusal happens before any mutation: *this task's* format and payload are untouched.
        Assert.Equal(formatBefore, doc.GetTask(id)!.CommentsFormat);
        Assert.Equal(payloadBefore, CustomCommentsByIdFromXml(doc.ToXmlString())[id]);
    }

    [Fact]
    public void Overwriting_the_real_plain_task_is_allowed()
    {
        var doc = Load();
        doc.UpdateTask(26, new() { Comments = "replaced" });
        Assert.Equal("replaced", doc.GetTask(26)!.Comments);
    }

    [Fact]
    public void Opt_in_flattens_a_real_markdown_task_and_drops_only_its_payload()
    {
        var doc = Load();

        doc.UpdateTask(30, new() { Comments = "just text", ReplaceFormattedComments = true });

        var t = doc.GetTask(30)!;
        Assert.Equal("just text", t.Comments);
        Assert.Equal("plain", t.CommentsFormat);

        var after = CustomCommentsByIdFromXml(doc.ToXmlString());
        Assert.False(after.ContainsKey(30));                         // markdown payload removed
        Assert.Equal(new[] { 27, 28, 29 }, after.Keys.OrderBy(k => k));   // the others are intact
    }

    [Theory]
    [InlineData(29)]   // HTML
    [InlineData(30)]   // Markdown
    public void Our_encoder_reproduces_the_real_todolist_payload_bytes(int id)
    {
        // Decode ToDoList's own CUSTOMCOMMENTS, recover the source, and confirm our encoder
        // produces byte-identical bytes — i.e. authoring (#27) round-trips against real output.
        var todolistBytes = Convert.FromBase64String(CustomCommentsByIdFromFile(TestData.MultiCommentFormatFilePath())[id]);
        var source = System.Text.Encoding.Unicode.GetString(todolistBytes);
        var ourBytes = Convert.FromBase64String(CommentFormat.EncodeCustomComments(source));
        Assert.Equal(todolistBytes, ourBytes);
    }

    [Theory]
    [InlineData(29, CommentContentFormat.Html)]
    [InlineData(30, CommentContentFormat.Markdown)]
    public void Our_plain_mirror_reproduces_real_todolist_output(int id, CommentContentFormat format)
    {
        // ToDoList derives <COMMENTS> via MSHTML innerText; assert our renderer matches its actual
        // output for these real tasks (the source is recovered from the genuine <CUSTOMCOMMENTS>).
        var source = Encoding.Unicode.GetString(
            Convert.FromBase64String(CustomCommentsByIdFromFile(TestData.MultiCommentFormatFilePath())[id]));
        Assert.Equal(Load().GetTask(id)!.Comments, CommentFormat.ToPlainMirror(format, source));
    }

    [Fact]
    public void Editing_unrelated_fields_preserves_every_rich_payload_across_save_reload()
    {
        var doc = Load();
        var original = CustomCommentsByIdFromFile(TestData.MultiCommentFormatFilePath());
        Assert.Equal(4, original.Count);   // rich / spreadsheet / html / markdown

        // Touch only the plain task's notes and a formatted task's *priority* — never its notes.
        doc.UpdateTask(26, new() { Comments = "edited" });
        doc.UpdateTask(29, new() { Priority = 7 });

        var tmp = Path.Combine(Path.GetTempPath(), "tdlmcp_mcf_" + Guid.NewGuid().ToString("N") + ".tdl");
        try
        {
            doc.SaveAs(tmp);
            var reloaded = TodoListDocument.Load(tmp);
            var after = CustomCommentsByIdFromFile(tmp);

            Assert.Equal(original.Count, after.Count);
            foreach (var (id, payload) in original)
                Assert.Equal(payload, after[id]);   // byte-for-byte identical base64

            foreach (var (id, fmt) in new[] { (27, "rich"), (28, "spreadsheet"), (29, "html"), (30, "markdown") })
                Assert.Equal(fmt, reloaded.GetTask(id)!.CommentsFormat);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    /// <summary>Maps task ID → raw &lt;CUSTOMCOMMENTS&gt; payload by parsing a saved .tdl file.</summary>
    private static Dictionary<int, string> CustomCommentsByIdFromFile(string path) =>
        CustomCommentsById(XDocument.Load(path));

    private static Dictionary<int, string> CustomCommentsByIdFromXml(string xml) =>
        CustomCommentsById(XDocument.Parse(xml));

    private static Dictionary<int, string> CustomCommentsById(XDocument doc) =>
        doc.Descendants("TASK")
            .Where(t => t.Element("CUSTOMCOMMENTS") is not null)
            .ToDictionary(t => (int)t.Attribute("ID")!, t => t.Element("CUSTOMCOMMENTS")!.Value);
}
