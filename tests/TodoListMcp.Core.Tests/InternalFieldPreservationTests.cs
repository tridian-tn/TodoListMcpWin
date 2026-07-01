using System.Xml.Linq;
using TodoListMcp.Core;

namespace TodoListMcp.Core.Tests;

/// <summary>
/// Guards issue #14: internal/plugin fields ToDoList writes but this server deliberately does not
/// expose — <c>REFID</c>, per-plugin <c>&lt;METADATA&gt;</c> blobs (whose attribute names are raw
/// GUIDs), the compressed <c>&lt;CUSTOMCOMMENTS&gt;</c> rich-text mirror, and unknown attributes such
/// as <c>ICONINDEX</c>/<c>COLOR</c> — must survive a load→modify→save round-trip untouched. The
/// mechanism is that every mutation operates on named attributes/elements of the XLinq tree, so
/// anything the code never names rides through verbatim; these tests assert that guarantee rather
/// than relying on it implicitly.
/// </summary>
public class InternalFieldPreservationTests
{
    private const string MetadataGuid = "FA40B83E-E934-D494-8FB3-8EC9748FA4E8";

    /// <summary>A task carrying every internal field of interest, plus a couple of unknown attributes.</summary>
    private static TodoListDocument WithInternalFields() =>
        TodoListDocument.Parse(
            $"""
            <?xml version="1.0" encoding="utf-16"?>
            <TODOLIST PROJECTNAME="P" NEXTUNIQUEID="2"><TASK ID="1" TITLE="T" REFID="42" ICONINDEX="38" COLOR="255" COMMENTSTYPE="PLAIN_TEXT"><COMMENTS>plain mirror</COMMENTS><CUSTOMCOMMENTS>opaque-plugin-blob</CUSTOMCOMMENTS><METADATA {MetadataGuid}="-133,-235||beetle.jpg"/></TASK></TODOLIST>
            """,
            TestData.Clock);

    private static XElement Task(TodoListDocument doc, int id) =>
        XDocument.Parse(doc.ToXmlString()).Descendants("TASK").First(t => (int)t.Attribute("ID")! == id);

    [Fact]
    public void Updating_an_unrelated_field_leaves_internal_fields_untouched()
    {
        var doc = WithInternalFields();

        // Touch only the title: comments aren't in the request, so the notes-rewriting path
        // (which would drop <CUSTOMCOMMENTS>) never runs.
        doc.UpdateTask(1, new() { Title = "Renamed" });

        var task = Task(doc, 1);
        Assert.Equal("Renamed", (string?)task.Attribute("TITLE"));   // the change we asked for
        Assert.Equal("42", (string?)task.Attribute("REFID"));
        Assert.Equal("38", (string?)task.Attribute("ICONINDEX"));
        Assert.Equal("255", (string?)task.Attribute("COLOR"));
        Assert.Equal("opaque-plugin-blob", task.Element("CUSTOMCOMMENTS")?.Value);

        // The <METADATA> element and its GUID-named attribute survive the XLinq round-trip intact.
        var metadata = Assert.Single(task.Elements("METADATA"));
        var attr = Assert.Single(metadata.Attributes());
        Assert.Equal(MetadataGuid, attr.Name.LocalName);
        Assert.Equal("-133,-235||beetle.jpg", attr.Value);
    }

    [Fact]
    public void Replacing_comments_drops_custom_but_keeps_other_internal_fields_and_stays_in_sync()
    {
        var doc = WithInternalFields();

        // Rewriting notes over a rich payload requires opt-in; it collapses <CUSTOMCOMMENTS> and
        // rewrites <COMMENTS> in one pass, so the two can't be left out of sync.
        doc.UpdateTask(1, new() { Comments = "clean text", ReplaceFormattedComments = true });

        var task = Task(doc, 1);
        Assert.Null(task.Element("CUSTOMCOMMENTS"));                  // rich mirror gone
        Assert.Equal("clean text", task.Element("COMMENTS")?.Value); // plain mirror is the source of truth

        // Fields unrelated to comments are still untouched by the comment rewrite.
        Assert.Equal("42", (string?)task.Attribute("REFID"));
        var metadata = Assert.Single(task.Elements("METADATA"));
        Assert.Equal(MetadataGuid, Assert.Single(metadata.Attributes()).Name.LocalName);
    }

    [Fact]
    public void Real_file_metadata_and_refid_survive_load_modify_save_reload()
    {
        // Introduction.tdl is a genuine ToDoList export: task 24 carries a plugin <METADATA> blob
        // with a GUID-named attribute, and every task carries a REFID. Prove they survive a real
        // UTF-16 disk round-trip after an unrelated mutation.
        var doc = TodoListDocument.Load(TestData.SampleFilePath());
        doc.UpdateTask(24, new() { Title = "Touched by MCP" });

        var tmp = Path.Combine(Path.GetTempPath(), "tdlmcp_internal_" + Guid.NewGuid().ToString("N") + ".tdl");
        try
        {
            doc.SaveAs(tmp);
            var task = XDocument.Load(tmp).Descendants("TASK").First(t => (int)t.Attribute("ID")! == 24);

            Assert.Equal("Touched by MCP", (string?)task.Attribute("TITLE"));
            Assert.NotNull((string?)task.Attribute("REFID"));
            var metadata = Assert.Single(task.Elements("METADATA"));
            var attr = Assert.Single(metadata.Attributes());
            Assert.Equal(MetadataGuid, attr.Name.LocalName);
            Assert.Equal("-133,-235||beetle.jpg", attr.Value);
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
