using System.Xml.Linq;
using TodoListMcp.Core;
using TodoListMcp.Core.Model;

namespace TodoListMcp.Core.Tests;

/// <summary>
/// Covers custom fields (ToDoList custom attributes): reading the list-level definitions, reading a
/// task's values, writing/clearing them via add/update, searching by them, and the round-trip
/// fidelity that matters — most notably that a multi-select value's embedded newline survives the
/// attribute-value round-trip as a character reference rather than collapsing to a space.
/// </summary>
public class CustomFieldsTests
{
    // A list defining four fields: a plain string (MR), an integer (POINTS), a fixed multi-select
    // list (TAGS) and a calculation (TOTAL). Task 1 carries two values; the multi-select value stores
    // its two items newline-separated, written as ToDoList writes it — a &#10; character reference.
    private const string Xml =
        """
        <?xml version="1.0" encoding="utf-16"?>
        <TODOLIST PROJECTNAME="P" NEXTUNIQUEID="3"><CUSTOMATTRIBDEF ID="MR" LABEL="Merge Request" COLTITLE="MR" TYPE="0" ENABLED="1" FEATURES="1"/><CUSTOMATTRIBDEF ID="POINTS" LABEL="Points" TYPE="2" ENABLED="1"/><CUSTOMATTRIBDEF ID="TAGS" LABEL="Tags" TYPE="1024" ENABLED="1" LISTDATA="alpha&#10;beta&#10;gamma"/><CUSTOMATTRIBDEF ID="TOTAL" LABEL="Total" TYPE="9" ENABLED="1"/><TASK ID="1" TITLE="T1" POS="0" POSSTRING="1"><CUSTOMATTRIB ID="MR" VALUE="12345" VALUEDISPLAY="12345"/><CUSTOMATTRIB ID="TAGS" VALUE="alpha&#10;gamma" VALUEDISPLAY="alpha, gamma"/></TASK><TASK ID="2" TITLE="T2" POS="1" POSSTRING="2"/></TODOLIST>
        """;

    private static TodoListDocument Doc() => TodoListDocument.Parse(Xml, TestData.Clock);

    private static XElement Task(TodoListDocument doc, int id) =>
        XDocument.Parse(doc.ToXmlString()).Descendants("TASK").First(t => (int)t.Attribute("ID")! == id);

    private static CustomFieldValue? Field(TodoTask t, string id) =>
        t.CustomFields.FirstOrDefault(c => c.Id == id);

    // ---- Reading definitions -------------------------------------------------

    [Fact]
    public void Reads_definitions_with_types_and_list_data()
    {
        var defs = Doc().GetCustomAttributeDefinitions();
        Assert.Equal(4, defs.Count);

        var mr = defs.Single(d => d.Id == "MR");
        Assert.Equal("Merge Request", mr.Label);
        Assert.Equal("MR", mr.ColumnTitle);
        Assert.Equal(CustomFieldDataType.String, mr.DataType);
        Assert.Equal(CustomFieldListType.None, mr.ListType);
        Assert.False(mr.IsList);
        Assert.True(mr.Enabled);

        Assert.Equal(CustomFieldDataType.Integer, defs.Single(d => d.Id == "POINTS").DataType);
        Assert.Equal(CustomFieldDataType.Calculation, defs.Single(d => d.Id == "TOTAL").DataType);

        var tags = defs.Single(d => d.Id == "TAGS");
        Assert.Equal(CustomFieldDataType.String, tags.DataType);
        Assert.Equal(CustomFieldListType.FixedMulti, tags.ListType);
        Assert.True(tags.IsMultiValued);
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, tags.ListItems);
    }

    // ---- Reading task values -------------------------------------------------

    [Fact]
    public void Reads_scalar_and_multi_value_task_values()
    {
        var t = Doc().GetTask(1)!;
        Assert.Equal(2, t.CustomFields.Count);

        var mr = Field(t, "MR")!;
        Assert.Equal("12345", mr.Value);
        Assert.Equal(new[] { "12345" }, mr.Values);      // scalar → single element
        Assert.Equal("12345", mr.Display);

        var tags = Field(t, "TAGS")!;
        Assert.Equal("alpha\ngamma", tags.Value);         // raw newline-joined
        Assert.Equal(new[] { "alpha", "gamma" }, tags.Values);  // split per the multi-select def
        Assert.Equal("alpha, gamma", tags.Display);
    }

    [Fact]
    public void Task_without_custom_values_reads_empty()
    {
        Assert.Empty(Doc().GetTask(2)!.CustomFields);
    }

    // ---- Writing -------------------------------------------------------------

    [Fact]
    public void Add_task_sets_custom_field_and_drops_display()
    {
        var doc = Doc();
        var task = doc.AddTask(new AddTaskRequest
        {
            Title = "New",
            CustomFields = new[] { new CustomFieldSet { Id = "mr", Value = "999" } },  // lower-case ID accepted
        });

        Assert.Equal("999", Field(task, "MR")!.Value);

        var e = Task(doc, task.Id).Elements("CUSTOMATTRIB").Single();
        Assert.Equal("MR", (string?)e.Attribute("ID"));           // stored upper-cased
        Assert.Equal("999", (string?)e.Attribute("VALUE"));
        Assert.Null(e.Attribute("VALUEDISPLAY"));                  // derived mirror dropped
    }

    [Fact]
    public void Update_sets_value_and_drops_stale_display()
    {
        var doc = Doc();
        doc.UpdateTask(1, new UpdateTaskRequest
        {
            CustomFields = new[] { new CustomFieldSet { Id = "MR", Value = "67890" } },
        });

        var mr = Task(doc, 1).Elements("CUSTOMATTRIB").Single(e => (string?)e.Attribute("ID") == "MR");
        Assert.Equal("67890", (string?)mr.Attribute("VALUE"));
        Assert.Null(mr.Attribute("VALUEDISPLAY"));
    }

    [Fact]
    public void Update_leaves_other_custom_fields_untouched()
    {
        var doc = Doc();
        doc.UpdateTask(1, new UpdateTaskRequest
        {
            CustomFields = new[] { new CustomFieldSet { Id = "MR", Value = "67890" } },
        });

        // TAGS was not in the request, so it survives unchanged.
        var tags = doc.GetTask(1)!.CustomFields.Single(c => c.Id == "TAGS");
        Assert.Equal(new[] { "alpha", "gamma" }, tags.Values);
    }

    [Fact]
    public void Empty_value_clears_the_field()
    {
        var doc = Doc();
        doc.UpdateTask(1, new UpdateTaskRequest
        {
            CustomFields = new[] { new CustomFieldSet { Id = "MR", Value = "" } },
        });

        Assert.Null(Field(doc.GetTask(1)!, "MR"));
        Assert.DoesNotContain(Task(doc, 1).Elements("CUSTOMATTRIB"), e => (string?)e.Attribute("ID") == "MR");
    }

    [Fact]
    public void Multi_value_newline_survives_the_attribute_round_trip()
    {
        var doc = Doc();
        doc.UpdateTask(1, new UpdateTaskRequest
        {
            CustomFields = new[] { new CustomFieldSet { Id = "TAGS", Value = "beta\ngamma" } },
        });

        // The critical check: serialise + reload from a string and confirm the embedded newline was
        // written as a character reference, not normalised to a space (which would merge the items).
        var reloaded = TodoListDocument.Parse(doc.ToXmlString(), TestData.Clock);
        var tags = reloaded.GetTask(1)!.CustomFields.Single(c => c.Id == "TAGS");
        Assert.Equal("beta\ngamma", tags.Value);
        Assert.Equal(new[] { "beta", "gamma" }, tags.Values);
    }

    // ---- Write validation ----------------------------------------------------

    [Fact]
    public void Setting_an_undefined_field_is_rejected()
    {
        var doc = Doc();
        var ex = Assert.Throws<ArgumentException>(() => doc.UpdateTask(1, new UpdateTaskRequest
        {
            CustomFields = new[] { new CustomFieldSet { Id = "NOPE", Value = "x" } },
        }));
        Assert.Contains("NOPE", ex.Message);
    }

    [Fact]
    public void Setting_a_calculation_field_is_rejected()
    {
        var doc = Doc();
        Assert.Throws<ArgumentException>(() => doc.UpdateTask(1, new UpdateTaskRequest
        {
            CustomFields = new[] { new CustomFieldSet { Id = "TOTAL", Value = "5" } },
        }));
    }

    [Fact]
    public void A_rejected_field_leaves_the_document_untouched()
    {
        var doc = Doc();
        // A valid assignment followed by an invalid one: the whole update must be refused, so the
        // valid one is not half-applied.
        Assert.Throws<ArgumentException>(() => doc.UpdateTask(1, new UpdateTaskRequest
        {
            Title = "Renamed",
            CustomFields = new[]
            {
                new CustomFieldSet { Id = "MR", Value = "changed" },
                new CustomFieldSet { Id = "TOTAL", Value = "5" },
            },
        }));

        var t = doc.GetTask(1)!;
        Assert.Equal("T1", t.Title);                          // title change rolled back
        Assert.Equal("12345", Field(t, "MR")!.Value);         // MR untouched
        Assert.False(doc.IsDirty);
    }

    // ---- Search --------------------------------------------------------------

    [Fact]
    public void Search_matches_by_custom_field_value()
    {
        var byValue = Doc().Search(new TaskQuery { CustomFieldId = "MR", CustomFieldValue = "12345" });
        Assert.Equal(new[] { 1 }, byValue.Select(t => t.Id));

        var byItem = Doc().Search(new TaskQuery { CustomFieldId = "TAGS", CustomFieldValue = "gamma" });
        Assert.Equal(new[] { 1 }, byItem.Select(t => t.Id));

        var presence = Doc().Search(new TaskQuery { CustomFieldId = "MR" });
        Assert.Equal(new[] { 1 }, presence.Select(t => t.Id));

        var none = Doc().Search(new TaskQuery { CustomFieldId = "MR", CustomFieldValue = "0000" });
        Assert.Empty(none);
    }

    // ---- Round-trip fidelity -------------------------------------------------

    [Fact]
    public void Unknown_definition_attributes_and_other_tasks_survive_a_write()
    {
        var doc = Doc();
        doc.UpdateTask(1, new UpdateTaskRequest
        {
            CustomFields = new[] { new CustomFieldSet { Id = "MR", Value = "changed" } },
        });

        var root = XDocument.Parse(doc.ToXmlString()).Root!;
        // The FEATURES attribute on the MR definition is one this server never names; it must ride through.
        var mrDef = root.Elements("CUSTOMATTRIBDEF").Single(d => (string?)d.Attribute("ID") == "MR");
        Assert.Equal("1", (string?)mrDef.Attribute("FEATURES"));
        // Definitions are not touched by a task write.
        Assert.Equal(4, root.Elements("CUSTOMATTRIBDEF").Count());
    }
}
