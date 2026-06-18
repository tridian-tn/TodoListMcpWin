using TodoListMcp.Core;

namespace TodoListMcp.Core.Tests;

/// <summary>
/// Guards the reader against the quirkier inputs a real .tdl can contain: legacy attribute-form
/// notes, rich-text overrides, unparseable/out-of-range dates, and the "-2 = unset" scale sentinel.
/// </summary>
public class ParsingResilienceTests
{
    private static TodoListDocument Parse(string taskAttributesAndChildren) =>
        TodoListDocument.Parse(
            $"""
            <?xml version="1.0" encoding="utf-16"?>
            <TODOLIST PROJECTNAME="P" NEXTUNIQUEID="2"><TASK ID="1" {taskAttributesAndChildren}</TASK></TODOLIST>
            """,
            TestData.Clock);

    [Fact]
    public void Legacy_comments_attribute_is_read_when_no_child_element()
    {
        var doc = Parse("""TITLE="T" COMMENTS="attribute notes">""");
        Assert.Equal("attribute notes", doc.GetTask(1)!.Comments);
    }

    [Fact]
    public void Setting_comments_normalises_legacy_attribute_and_rich_text_to_a_plain_element()
    {
        // A task carrying both the legacy COMMENTS attribute and a CUSTOMCOMMENTS rich-text override.
        var doc = Parse("""TITLE="T" COMMENTS="old"><CUSTOMCOMMENTS>{\rtf}</CUSTOMCOMMENTS>""");

        doc.UpdateTask(1, new() { Comments = "clean text" });

        Assert.Equal("clean text", doc.GetTask(1)!.Comments);
        var xml = doc.ToXmlString();
        Assert.Contains("<COMMENTS>clean text</COMMENTS>", xml);
        Assert.DoesNotContain("COMMENTS=\"old\"", xml);   // legacy attribute dropped
        Assert.DoesNotContain("CUSTOMCOMMENTS", xml);     // rich-text override removed
    }

    [Theory]
    [InlineData("notadate")]   // unparseable
    [InlineData("0")]          // ToDoList's "no date"
    [InlineData("-5")]         // negative serial
    [InlineData("999999999")]  // out of OLE-automation range
    public void Unparseable_or_out_of_range_dates_decode_to_null(string serial)
    {
        var doc = Parse($"""TITLE="T" DUEDATE="{serial}">""");
        Assert.Null(doc.GetTask(1)!.DueDate);   // no throw, just "unset"
    }

    [Fact]
    public void Negative_scale_sentinel_reads_as_null_priority_and_risk()
    {
        // ToDoList writes -2 for "no priority / no risk"; that must surface as null, not -2.
        var doc = Parse("""TITLE="T" PRIORITY="-2" RISK="-2">""");
        var t = doc.GetTask(1)!;
        Assert.Null(t.Priority);
        Assert.Null(t.Risk);
    }
}
