using TodoListMcp.Core;

namespace TodoListMcp.Core.Tests;

/// <summary>
/// Covers issue #25 parts 1 and 2: surfacing the comment format on read, and refusing to
/// silently overwrite formatted (non-plain-text) comments unless the caller opts in.
/// </summary>
public class CommentFormatTests
{
    private const string HtmlId = "FE0B6B6E-2B61-4AEB-AA0D-98DBE5942F02";
    private const string MarkdownId = "BAA4E079-268B-4B9B-B7C8-6D15CCF058A2";
    private const string SpreadsheetId = "BBDCAEDF-B297-4E09-BBFB-B308358628B9";
    private const string RichId = "849CF988-79FE-418A-A40D-01FE3AFCAB2C";

    private static TodoListDocument Parse(string taskAttributesAndChildren) =>
        TodoListDocument.Parse(
            $"""
            <?xml version="1.0" encoding="utf-16"?>
            <TODOLIST PROJECTNAME="P" NEXTUNIQUEID="2"><TASK ID="1" {taskAttributesAndChildren}</TASK></TODOLIST>
            """,
            TestData.Clock);

    // ---- Part 2: format surfaced on read ----------------------------------------------------

    [Fact]
    public void Plain_text_task_reports_plain_format()
    {
        var doc = Parse("""TITLE="T" COMMENTSTYPE="PLAIN_TEXT"><COMMENTS>hi</COMMENTS>""");
        Assert.Equal("plain", doc.GetTask(1)!.CommentsFormat);
    }

    [Theory]
    [InlineData(HtmlId, "html")]
    [InlineData(MarkdownId, "markdown")]
    [InlineData(SpreadsheetId, "spreadsheet")]
    [InlineData(RichId, "rich")]
    public void Known_content_controls_report_friendly_format(string typeId, string friendly)
    {
        var doc = Parse($"""TITLE="T" COMMENTSTYPE="{typeId}"><COMMENTS>flattened</COMMENTS><CUSTOMCOMMENTS>blob</CUSTOMCOMMENTS>""");
        Assert.Equal(friendly, doc.GetTask(1)!.CommentsFormat);
    }

    [Fact]
    public void Unknown_content_control_reports_its_raw_id()
    {
        var doc = Parse("""TITLE="T" COMMENTSTYPE="SOME-OTHER-PLUGIN-GUID"><COMMENTS>x</COMMENTS>""");
        Assert.Equal("SOME-OTHER-PLUGIN-GUID", doc.GetTask(1)!.CommentsFormat);
    }

    [Fact]
    public void Task_without_comments_reports_null_format()
    {
        var doc = Parse("""TITLE="T">""");
        Assert.Null(doc.GetTask(1)!.CommentsFormat);
    }

    [Fact]
    public void Comments_without_explicit_type_report_plain()
    {
        var doc = Parse("""TITLE="T"><COMMENTS>untyped</COMMENTS>""");
        Assert.Equal("plain", doc.GetTask(1)!.CommentsFormat);
    }

    // ---- Part 1: overwrite guard ------------------------------------------------------------

    [Fact]
    public void Overwriting_html_comments_without_opt_in_throws_and_preserves_payload()
    {
        var doc = Parse($"""TITLE="T" COMMENTSTYPE="{HtmlId}"><COMMENTS>plain mirror</COMMENTS><CUSTOMCOMMENTS>html blob</CUSTOMCOMMENTS>""");

        var ex = Assert.Throws<FormattedCommentsException>(() => doc.UpdateTask(1, new() { Comments = "new" }));
        Assert.Equal("html", ex.Format);

        var xml = doc.ToXmlString();
        Assert.Contains("<CUSTOMCOMMENTS>html blob</CUSTOMCOMMENTS>", xml);
        Assert.Contains(HtmlId, xml);                       // COMMENTSTYPE untouched
        Assert.Equal("plain mirror", doc.GetTask(1)!.Comments);
    }

    [Fact]
    public void Refused_overwrite_leaves_other_fields_unchanged()
    {
        var doc = Parse($"""TITLE="Original" COMMENTSTYPE="{HtmlId}"><COMMENTS>mirror</COMMENTS><CUSTOMCOMMENTS>blob</CUSTOMCOMMENTS>""");

        // A request mixing a title change with a (refused) comment overwrite must not mutate the
        // title before throwing — the whole update is rejected atomically.
        Assert.Throws<FormattedCommentsException>(
            () => doc.UpdateTask(1, new() { Title = "Renamed", Comments = "new" }));

        Assert.Equal("Original", doc.GetTask(1)!.Title);
    }

    [Fact]
    public void Clearing_formatted_comments_without_opt_in_throws()
    {
        var doc = Parse($"""TITLE="T" COMMENTSTYPE="{MarkdownId}"><COMMENTS>md mirror</COMMENTS><CUSTOMCOMMENTS>md blob</CUSTOMCOMMENTS>""");
        // Even an empty string (a clear) would discard the payload, so it is gated too.
        Assert.Throws<FormattedCommentsException>(() => doc.UpdateTask(1, new() { Comments = "" }));
    }

    [Fact]
    public void Overwriting_formatted_comments_with_opt_in_flattens_to_plain()
    {
        var doc = Parse($"""TITLE="T" COMMENTSTYPE="{SpreadsheetId}"><COMMENTS>grid mirror</COMMENTS><CUSTOMCOMMENTS>workbook</CUSTOMCOMMENTS>""");

        doc.UpdateTask(1, new() { Comments = "just text", ReplaceFormattedComments = true });

        var t = doc.GetTask(1)!;
        Assert.Equal("just text", t.Comments);
        Assert.Equal("plain", t.CommentsFormat);
        var xml = doc.ToXmlString();
        Assert.DoesNotContain("CUSTOMCOMMENTS", xml);
        Assert.DoesNotContain(SpreadsheetId, xml);
        Assert.Contains("COMMENTSTYPE=\"PLAIN_TEXT\"", xml);
    }

    [Fact]
    public void Editing_other_fields_on_a_formatted_task_is_not_gated_and_preserves_payload()
    {
        var doc = Parse($"""TITLE="T" COMMENTSTYPE="{HtmlId}"><COMMENTS>mirror</COMMENTS><CUSTOMCOMMENTS>blob</CUSTOMCOMMENTS>""");

        doc.UpdateTask(1, new() { Title = "Renamed", Priority = 7 });   // no Comments supplied

        var t = doc.GetTask(1)!;
        Assert.Equal("Renamed", t.Title);
        Assert.Equal("html", t.CommentsFormat);
        Assert.Contains("<CUSTOMCOMMENTS>blob</CUSTOMCOMMENTS>", doc.ToXmlString());
    }

    [Fact]
    public void Setting_comments_on_a_plain_task_is_allowed()
    {
        var doc = Parse("""TITLE="T" COMMENTSTYPE="PLAIN_TEXT"><COMMENTS>old</COMMENTS>""");
        doc.UpdateTask(1, new() { Comments = "new" });   // no opt-in needed
        Assert.Equal("new", doc.GetTask(1)!.Comments);
    }
}
