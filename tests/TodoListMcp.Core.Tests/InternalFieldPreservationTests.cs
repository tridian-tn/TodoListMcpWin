using System.Xml.Linq;
using TodoListMcp.Core;
using TodoListMcp.Core.Model;

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
    private const string MetadataGuid2 = "B1C2D3E4-F5A6-4789-ABCD-EF0123456789";
    private const string MetadataGuid3 = "C0FFEE00-DEAD-4BEE-8FAD-1234567890AB";

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
    public void Opaque_custom_comments_survive_unchanged_across_an_unrelated_update()
    {
        // The risk case: task 27 carries a genuine rich (RTF) <CUSTOMCOMMENTS> payload this server
        // can't re-author. Touching an unrelated field must leave it unchanged across a real disk
        // round-trip — if it were ever corrupted, the content is unrecoverable.
        var path = TestData.MultiCommentFormatFilePath();
        var source = XDocument.Load(path);
        var originalPayload = RawCustomComments(source, 27);
        var originalRefId = (string?)Task27(source).Attribute("REFID");
        Assert.False(string.IsNullOrEmpty(originalPayload));

        var doc = TodoListDocument.Load(path);
        doc.UpdateTask(27, new() { Title = "Touched by MCP" });   // no comments in the request

        var tmp = Path.Combine(Path.GetTempPath(), "tdlmcp_opaque_" + Guid.NewGuid().ToString("N") + ".tdl");
        try
        {
            doc.SaveAs(tmp);
            var reloaded = XDocument.Load(tmp);

            Assert.Equal("Touched by MCP", (string?)Task27(reloaded).Attribute("TITLE"));
            Assert.Equal(originalPayload, RawCustomComments(reloaded, 27));   // payload identical
            Assert.Equal(originalRefId, (string?)Task27(reloaded).Attribute("REFID"));
        }
        finally
        {
            File.Delete(tmp);
        }

        static XElement Task27(XDocument d) => d.Descendants("TASK").First(t => (int)t.Attribute("ID")! == 27);
    }

    private static string? RawCustomComments(XDocument doc, int id) =>
        doc.Descendants("TASK").First(t => (int)t.Attribute("ID")! == id).Element("CUSTOMCOMMENTS")?.Value;

    // ---- multiple <METADATA> entries and mutations other than update -------------------------

    /// <summary>
    /// A two-task document whose first task carries the internal fields plus multiple plugin
    /// metadata entries: two GUID-named attributes on one &lt;METADATA&gt; element and a second
    /// &lt;METADATA&gt; element — the shape ToDoList produces when several plugins annotate a task.
    /// The sibling (ID 2) gives move/dependency mutations something to target.
    /// </summary>
    private static TodoListDocument MultiMetadataDoc() =>
        TodoListDocument.Parse(
            $"""
            <?xml version="1.0" encoding="utf-16"?>
            <TODOLIST PROJECTNAME="P" NEXTUNIQUEID="3"><TASK ID="1" TITLE="T" REFID="42" ICONINDEX="38" COLOR="255" COMMENTSTYPE="PLAIN_TEXT"><COMMENTS>plain mirror</COMMENTS><CUSTOMCOMMENTS>opaque-plugin-blob</CUSTOMCOMMENTS><METADATA {MetadataGuid}="v1" {MetadataGuid2}="v2"/><METADATA {MetadataGuid3}="v3"/></TASK><TASK ID="2" TITLE="Other"/></TODOLIST>
            """,
            TestData.Clock);

    /// <summary>Collects every GUID-named attribute across all &lt;METADATA&gt; elements of a task.</summary>
    private static Dictionary<string, string> MetadataAttrs(XElement task) =>
        task.Elements("METADATA")
            .SelectMany(m => m.Attributes())
            .ToDictionary(a => a.Name.LocalName, a => a.Value);

    private static void AssertInternalFieldsIntact(TodoListDocument doc)
    {
        var task = Task(doc, 1);
        Assert.Equal("42", (string?)task.Attribute("REFID"));
        Assert.Equal("38", (string?)task.Attribute("ICONINDEX"));
        Assert.Equal("255", (string?)task.Attribute("COLOR"));
        Assert.Equal("opaque-plugin-blob", task.Element("CUSTOMCOMMENTS")?.Value);

        // Both <METADATA> elements and all three GUID-named attributes survive.
        Assert.Equal(2, task.Elements("METADATA").Count());
        Assert.Equal(
            new Dictionary<string, string> { [MetadataGuid] = "v1", [MetadataGuid2] = "v2", [MetadataGuid3] = "v3" },
            MetadataAttrs(task));
    }

    [Fact]
    public void Multiple_metadata_entries_survive_an_update()
    {
        var doc = MultiMetadataDoc();
        doc.UpdateTask(1, new() { Title = "Renamed" });
        AssertInternalFieldsIntact(doc);
    }

    // Every mutation edits the same XLinq tree in place, so the preservation guarantee is not
    // specific to update_task; assert it holds through the structural and lifecycle mutations too.
    [Fact]
    public void Internal_fields_survive_complete() =>
        AssertSurvives(d => d.CompleteTask(1));

    [Fact]
    public void Internal_fields_survive_reopen() =>
        AssertSurvives(d => d.ReopenTask(1));

    [Fact]
    public void Internal_fields_survive_move() =>
        AssertSurvives(d => d.MoveTask(1, newParentId: 2));

    [Fact]
    public void Internal_fields_survive_add_dependency() =>
        AssertSurvives(d => d.AddDependency(1, dependsOnId: 2));

    [Fact]
    public void Internal_fields_survive_increment_time_spent() =>
        AssertSurvives(d => d.IncrementTimeSpent(1, deltaHours: 2));

    [Fact]
    public void Internal_fields_survive_set_recurrence() =>
        AssertSurvives(d => d.SetRecurrence(1, new() { Pattern = RecurrencePattern.EveryNDays, Interval = 3 }));

    private static void AssertSurvives(Action<TodoListDocument> mutate)
    {
        var doc = MultiMetadataDoc();
        mutate(doc);
        AssertInternalFieldsIntact(doc);
    }

    [Fact]
    public void Real_file_metadata_and_refid_survive_load_modify_save_reload()
    {
        // Introduction.tdl is a genuine ToDoList export: task 24 carries a plugin <METADATA> blob
        // with a GUID-named attribute, and every task carries a REFID. Prove they survive a real
        // UTF-16 disk round-trip after an unrelated mutation.
        var originalRefId = (string?)XDocument.Load(TestData.SampleFilePath())
            .Descendants("TASK").First(t => (int)t.Attribute("ID")! == 24).Attribute("REFID");

        var doc = TodoListDocument.Load(TestData.SampleFilePath());
        doc.UpdateTask(24, new() { Title = "Touched by MCP" });

        var tmp = Path.Combine(Path.GetTempPath(), "tdlmcp_internal_" + Guid.NewGuid().ToString("N") + ".tdl");
        try
        {
            doc.SaveAs(tmp);
            var task = XDocument.Load(tmp).Descendants("TASK").First(t => (int)t.Attribute("ID")! == 24);

            Assert.Equal("Touched by MCP", (string?)task.Attribute("TITLE"));
            Assert.Equal(originalRefId, (string?)task.Attribute("REFID"));
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
