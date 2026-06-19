using System.Text;
using TodoListMcp.Core;
using TodoListMcp.Core.Model;

namespace TodoListMcp.Core.Tests;

/// <summary>
/// Covers issue #27: authoring Markdown and HTML comments. The rich source is stored in
/// &lt;CUSTOMCOMMENTS&gt; as base64(UTF-16LE), with a plain-text mirror in &lt;COMMENTS&gt; and the
/// content-control GUID in COMMENTSTYPE — exactly how ToDoList itself stores them.
/// </summary>
public class CommentAuthoringTests
{
    private const string MarkdownId = "BAA4E079-268B-4B9B-B7C8-6D15CCF058A2";
    private const string HtmlId = "FE0B6B6E-2B61-4AEB-AA0D-98DBE5942F02";

    private static string Decode(string base64) =>
        Encoding.Unicode.GetString(Convert.FromBase64String(base64.Trim()));

    private static string? CustomComments(TodoListDocument doc, int id) =>
        System.Xml.Linq.XDocument.Parse(doc.ToXmlString())
            .Descendants("TASK").First(t => (int)t.Attribute("ID")! == id)
            .Element("CUSTOMCOMMENTS")?.Value;

    // ---- authoring on create -----------------------------------------------------------------

    [Fact]
    public void Authoring_markdown_stores_source_type_and_rendered_mirror()
    {
        var doc = TestData.Sample();
        var source = "Plan:\n\n- **a**\n- _b_";

        var t = doc.AddTask(new AddTaskRequest { Title = "N", Comments = source, CommentsFormat = CommentContentFormat.Markdown });

        var read = doc.GetTask(t.Id)!;
        Assert.Equal("markdown", read.CommentsFormat);
        Assert.Equal("Plan:\na\nb", read.Comments);                  // mirror is rendered (markup stripped)
        Assert.Equal(source, Decode(CustomComments(doc, t.Id)!));    // payload decodes back to the source
        Assert.Contains($"COMMENTSTYPE=\"{MarkdownId}\"", doc.ToXmlString());
    }

    [Fact]
    public void Authoring_html_stores_source_but_strips_the_mirror()
    {
        var doc = TestData.Sample();
        var source = "<p>Hello <b>world</b></p>";

        var t = doc.AddTask(new AddTaskRequest { Title = "N", Comments = source, CommentsFormat = CommentContentFormat.Html });

        var read = doc.GetTask(t.Id)!;
        Assert.Equal("html", read.CommentsFormat);
        Assert.Equal(source, Decode(CustomComments(doc, t.Id)!));     // rich source preserved
        Assert.Equal("Hello world", read.Comments);                  // mirror is tag-stripped
        Assert.DoesNotContain("<", read.Comments);
    }

    [Fact]
    public void Plain_format_authors_no_custom_payload()
    {
        var doc = TestData.Sample();
        var t = doc.AddTask(new AddTaskRequest { Title = "N", Comments = "just text" });   // default = plain

        Assert.Equal("plain", doc.GetTask(t.Id)!.CommentsFormat);
        Assert.Null(CustomComments(doc, t.Id));
        Assert.Contains("COMMENTSTYPE=\"PLAIN_TEXT\"", doc.ToXmlString());
    }

    [Fact]
    public void Authored_markdown_survives_save_and_reload()
    {
        var doc = TestData.Sample();
        var source = "# Heading\n\nbody **text**";
        var t = doc.AddTask(new AddTaskRequest { Title = "N", Comments = source, CommentsFormat = CommentContentFormat.Markdown });

        var reloaded = TodoListDocument.Parse(doc.ToXmlString(), TestData.Clock);

        Assert.Equal("markdown", reloaded.GetTask(t.Id)!.CommentsFormat);
        Assert.Equal(source, Decode(CustomComments(reloaded, t.Id)!));
    }

    // ---- authoring on update -----------------------------------------------------------------

    [Fact]
    public void Update_can_upgrade_a_plain_task_to_markdown_without_opt_in()
    {
        var doc = TestData.Sample();   // task 3 has no comments
        doc.UpdateTask(3, new() { Comments = "**bold**", CommentsFormat = CommentContentFormat.Markdown });

        Assert.Equal("markdown", doc.GetTask(3)!.CommentsFormat);
        Assert.Equal("**bold**", Decode(CustomComments(doc, 3)!));
    }

    [Fact]
    public void Update_re_authoring_over_existing_formatted_notes_needs_opt_in()
    {
        var doc = TestData.Sample();
        doc.UpdateTask(3, new() { Comments = "**md**", CommentsFormat = CommentContentFormat.Markdown });

        // Overwriting the markdown with HTML still discards a rich payload, so the guard applies.
        Assert.Throws<FormattedCommentsException>(
            () => doc.UpdateTask(3, new() { Comments = "<p>x</p>", CommentsFormat = CommentContentFormat.Html }));

        doc.UpdateTask(3, new() { Comments = "<p>x</p>", CommentsFormat = CommentContentFormat.Html, ReplaceFormattedComments = true });

        Assert.Equal("html", doc.GetTask(3)!.CommentsFormat);
        Assert.Equal("<p>x</p>", Decode(CustomComments(doc, 3)!));
        Assert.DoesNotContain(MarkdownId, doc.ToXmlString());   // the old markdown payload is gone
    }

    [Fact]
    public void Clearing_authored_notes_removes_every_representation()
    {
        var doc = TestData.Sample();
        doc.UpdateTask(3, new() { Comments = "<p>x</p>", CommentsFormat = CommentContentFormat.Html });

        doc.UpdateTask(3, new() { Comments = "", ReplaceFormattedComments = true });

        Assert.Null(doc.GetTask(3)!.Comments);
        Assert.Null(doc.GetTask(3)!.CommentsFormat);
        var xml = doc.ToXmlString();
        Assert.DoesNotContain("CUSTOMCOMMENTS", xml);
        Assert.DoesNotContain(HtmlId, xml);
    }

    // ---- format parsing ----------------------------------------------------------------------

    [Theory]
    [InlineData("plain", CommentContentFormat.Plain)]
    [InlineData("text", CommentContentFormat.Plain)]
    [InlineData("markdown", CommentContentFormat.Markdown)]
    [InlineData("md", CommentContentFormat.Markdown)]
    [InlineData("HTML", CommentContentFormat.Html)]
    [InlineData("htm", CommentContentFormat.Html)]
    public void TryParseWritable_accepts_authorable_formats(string text, CommentContentFormat expected)
    {
        Assert.True(CommentFormat.TryParseWritable(text, out var f));
        Assert.Equal(expected, f);
    }

    [Theory]
    [InlineData("rich")]
    [InlineData("rtf")]
    [InlineData("spreadsheet")]
    [InlineData("xlsx")]
    [InlineData("")]
    public void TryParseWritable_rejects_non_authorable_or_unknown_formats(string text)
    {
        Assert.False(CommentFormat.TryParseWritable(text, out _));
    }
}
