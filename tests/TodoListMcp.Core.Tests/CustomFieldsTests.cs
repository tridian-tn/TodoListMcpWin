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

    [Fact]
    public void Updating_a_value_preserves_element_position_and_unowned_attributes()
    {
        // The existing element carries an extra (unowned) attribute and a stale VALUEDISPLAY, and sits
        // before a sibling element. Updating it must edit in place: keep the element (and its extra
        // attribute), drop only the derived mirror, and not move it after the sibling.
        var doc = TodoListDocument.Parse(
            """
            <?xml version="1.0" encoding="utf-16"?>
            <TODOLIST PROJECTNAME="P" NEXTUNIQUEID="2"><CUSTOMATTRIBDEF ID="MR" TYPE="0" ENABLED="1"/><TASK ID="1" TITLE="T"><CUSTOMATTRIB ID="MR" VALUE="old" VALUEDISPLAY="old" NOTE="keep"/><TAG>x</TAG></TASK></TODOLIST>
            """,
            TestData.Clock);

        doc.UpdateTask(1, new UpdateTaskRequest { CustomFields = new[] { new CustomFieldSet { Id = "MR", Value = "new" } } });

        var task = Task(doc, 1);
        var mr = task.Elements("CUSTOMATTRIB").Single();
        Assert.Equal("new", (string?)mr.Attribute("VALUE"));
        Assert.Null(mr.Attribute("VALUEDISPLAY"));                 // stale mirror dropped
        Assert.Equal("keep", (string?)mr.Attribute("NOTE"));       // unowned attribute preserved
        Assert.Equal("CUSTOMATTRIB", ((XElement)task.FirstNode!).Name.LocalName);  // still first child
    }

    // ---- Definition decoding edge cases --------------------------------------

    private static CustomAttributeDefinition Def(string defXml) =>
        TodoListDocument.Parse(
            $"<?xml version=\"1.0\" encoding=\"utf-16\"?><TODOLIST NEXTUNIQUEID=\"2\">{defXml}<TASK ID=\"1\" TITLE=\"t\"/></TODOLIST>",
            TestData.Clock).GetCustomAttributeDefinitions().Single();

    [Theory]
    [InlineData(1, CustomFieldDataType.Date, CustomFieldListType.None, false)]
    [InlineData(3, CustomFieldDataType.Double, CustomFieldListType.None, false)]
    [InlineData(4, CustomFieldDataType.Bool, CustomFieldListType.None, false)]
    [InlineData(5, CustomFieldDataType.Icon, CustomFieldListType.None, false)]
    [InlineData(6, CustomFieldDataType.FileLink, CustomFieldListType.None, false)]
    [InlineData(7, CustomFieldDataType.TimePeriod, CustomFieldListType.None, false)]
    [InlineData(8, CustomFieldDataType.Fraction, CustomFieldListType.None, false)]
    [InlineData(256, CustomFieldDataType.String, CustomFieldListType.Auto, false)]
    [InlineData(512, CustomFieldDataType.String, CustomFieldListType.Fixed, false)]
    [InlineData(768, CustomFieldDataType.String, CustomFieldListType.AutoMulti, true)]
    public void Decodes_each_type_code(int type, CustomFieldDataType data, CustomFieldListType list, bool multi)
    {
        var def = Def($"<CUSTOMATTRIBDEF ID=\"F\" TYPE=\"{type}\" ENABLED=\"1\"/>");
        Assert.Equal(data, def.DataType);
        Assert.Equal(list, def.ListType);
        Assert.Equal(multi, def.IsMultiValued);
        Assert.Equal(type, def.RawType);
    }

    [Fact]
    public void Unknown_or_newer_type_codes_fall_back_to_unknown_and_none()
    {
        var data = Def("<CUSTOMATTRIBDEF ID=\"F\" TYPE=\"10\" ENABLED=\"1\"/>");   // 0x0A: no such data type
        Assert.Equal(CustomFieldDataType.Unknown, data.DataType);
        Assert.Equal(10, data.RawType);

        var listByte = Def("<CUSTOMATTRIBDEF ID=\"F\" TYPE=\"1280\" ENABLED=\"1\"/>");  // 0x0500: no such list type
        Assert.Equal(CustomFieldListType.None, listByte.ListType);
    }

    [Fact]
    public void Decodes_auto_list_default_and_auto_items_deduped_in_order()
    {
        // LISTDATA = default items, a tab, then auto-collected items; a value repeated across both dedupes.
        var def = Def("<CUSTOMATTRIBDEF ID=\"F\" TYPE=\"768\" ENABLED=\"1\" LISTDATA=\"a&#10;b&#9;b&#10;c\"/>");
        Assert.Equal(new[] { "a", "b", "c" }, def.ListItems);
    }

    [Fact]
    public void Scalar_field_ignores_any_list_data()
    {
        var def = Def("<CUSTOMATTRIBDEF ID=\"F\" TYPE=\"0\" ENABLED=\"1\" LISTDATA=\"a&#10;b\"/>");
        Assert.Empty(def.ListItems);
    }

    [Fact]
    public void Definition_reads_unset_label_and_disabled_as_null_and_false()
    {
        var def = Def("<CUSTOMATTRIBDEF ID=\"F\" TYPE=\"0\"/>");   // no LABEL/COLTITLE/ENABLED
        Assert.Null(def.Label);
        Assert.Null(def.ColumnTitle);
        Assert.False(def.Enabled);
    }

    [Fact]
    public void Legacy_root_definition_is_read_only_when_no_modern_defs_exist()
    {
        // A pre-9.2 list wrote its defs as root <CUSTOMATTRIB> elements.
        var legacy = Def("<CUSTOMATTRIB ID=\"MR\" LABEL=\"Merge\" TYPE=\"2\" ENABLED=\"1\"/>");
        Assert.Equal("MR", legacy.Id);
        Assert.Equal(CustomFieldDataType.Integer, legacy.DataType);

        // When both forms are present the modern <CUSTOMATTRIBDEF> wins; the legacy element is ignored.
        var doc = TodoListDocument.Parse(
            "<?xml version=\"1.0\" encoding=\"utf-16\"?><TODOLIST NEXTUNIQUEID=\"2\">"
            + "<CUSTOMATTRIBDEF ID=\"MR\" TYPE=\"0\" ENABLED=\"1\"/><CUSTOMATTRIB ID=\"LEGACY\" TYPE=\"0\"/>"
            + "<TASK ID=\"1\" TITLE=\"t\"/></TODOLIST>",
            TestData.Clock);
        Assert.Equal(new[] { "MR" }, doc.GetCustomAttributeDefinitions().Select(d => d.Id));
    }

    // ---- Value reading edge cases --------------------------------------------

    [Fact]
    public void Ids_are_read_case_insensitively_and_normalised_to_upper()
    {
        var doc = TodoListDocument.Parse(
            """
            <?xml version="1.0" encoding="utf-16"?>
            <TODOLIST NEXTUNIQUEID="2"><CUSTOMATTRIBDEF ID="tags" TYPE="1024" ENABLED="1"/><TASK ID="1" TITLE="t"><CUSTOMATTRIB ID="tags" VALUE="alpha&#10;beta"/></TASK></TODOLIST>
            """,
            TestData.Clock);

        Assert.Equal("TAGS", doc.GetCustomAttributeDefinitions().Single().Id);
        var field = doc.GetTask(1)!.CustomFields.Single();
        Assert.Equal("TAGS", field.Id);
        Assert.Equal(new[] { "alpha", "beta" }, field.Values);   // multi-split resolves through the lower-case def
    }

    [Fact]
    public void Orphan_value_without_a_definition_is_read_whole()
    {
        var doc = TodoListDocument.Parse(
            """
            <?xml version="1.0" encoding="utf-16"?>
            <TODOLIST NEXTUNIQUEID="2"><TASK ID="1" TITLE="t"><CUSTOMATTRIB ID="XX" VALUE="a&#10;b"/></TASK></TODOLIST>
            """,
            TestData.Clock);

        var field = doc.GetTask(1)!.CustomFields.Single();
        Assert.Equal("XX", field.Id);
        Assert.Equal("a\nb", field.Value);
        Assert.Equal(new[] { "a\nb" }, field.Values);   // no def → not multi → kept whole
    }

    [Fact]
    public void Multi_value_read_drops_empty_items()
    {
        var doc = TodoListDocument.Parse(
            """
            <?xml version="1.0" encoding="utf-16"?>
            <TODOLIST NEXTUNIQUEID="2"><CUSTOMATTRIBDEF ID="TAGS" TYPE="1024" ENABLED="1"/><TASK ID="1" TITLE="t"><CUSTOMATTRIB ID="TAGS" VALUE="alpha&#10;&#10;gamma"/></TASK></TODOLIST>
            """,
            TestData.Clock);

        Assert.Equal(new[] { "alpha", "gamma" }, doc.GetTask(1)!.CustomFields.Single().Values);
    }

    [Fact]
    public void Value_element_with_empty_or_missing_id_or_value_is_skipped()
    {
        var doc = TodoListDocument.Parse(
            """
            <?xml version="1.0" encoding="utf-16"?>
            <TODOLIST NEXTUNIQUEID="2"><CUSTOMATTRIBDEF ID="MR" TYPE="0" ENABLED="1"/><TASK ID="1" TITLE="t"><CUSTOMATTRIB ID="MR" VALUE=""/><CUSTOMATTRIB VALUE="x"/></TASK></TODOLIST>
            """,
            TestData.Clock);

        Assert.Empty(doc.GetTask(1)!.CustomFields);
    }

    // ---- Writing / validation edge cases -------------------------------------

    [Fact]
    public void Clearing_a_defined_but_absent_field_adds_no_element()
    {
        var doc = Doc();
        doc.UpdateTask(2, new UpdateTaskRequest { CustomFields = new[] { new CustomFieldSet { Id = "MR", Value = "" } } });

        Assert.Empty(doc.GetTask(2)!.CustomFields);
        Assert.Empty(Task(doc, 2).Elements("CUSTOMATTRIB"));
    }

    [Theory]
    [InlineData("   ")]
    [InlineData(null)]
    public void Setting_a_blank_or_null_id_is_rejected(string? id)
    {
        // A null ID (possible from an external JSON request despite the required modifier) must be a
        // controlled ArgumentException, not a NullReferenceException.
        var doc = Doc();
        Assert.Throws<ArgumentException>(() => doc.UpdateTask(1, new UpdateTaskRequest
        {
            CustomFields = new[] { new CustomFieldSet { Id = id!, Value = "x" } },
        }));
    }

    [Fact]
    public void Add_task_rejects_the_whole_task_when_any_custom_field_is_invalid()
    {
        var doc = Doc();
        var before = doc.NextUniqueId;
        Assert.Throws<ArgumentException>(() => doc.AddTask(new AddTaskRequest
        {
            Title = "New",
            CustomFields = new[]
            {
                new CustomFieldSet { Id = "MR", Value = "1" },
                new CustomFieldSet { Id = "TOTAL", Value = "5" },   // calculation → rejected
            },
        }));

        Assert.Equal(2, doc.GetTasks().Count);         // no task was added
        Assert.Equal(before, doc.NextUniqueId);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void Search_by_value_without_a_field_id_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => Doc().Search(new TaskQuery { CustomFieldValue = "12345" }));
    }

    [Fact]
    public void Search_matches_case_insensitively_and_by_substring()
    {
        var byLowerIdAndSubstring = Doc().Search(new TaskQuery { CustomFieldId = "mr", CustomFieldValue = "234" });
        Assert.Equal(new[] { 1 }, byLowerIdAndSubstring.Select(t => t.Id));

        var byItemDifferentCase = Doc().Search(new TaskQuery { CustomFieldId = "TAGS", CustomFieldValue = "GAMMA" });
        Assert.Equal(new[] { 1 }, byItemDifferentCase.Select(t => t.Id));
    }

    [Fact]
    public void A_sibling_tasks_values_survive_an_unrelated_update()
    {
        // Task 2 carries its own values (including a VALUEDISPLAY this server never writes itself);
        // editing task 1 must leave task 2's element byte-for-byte intact.
        var doc = TodoListDocument.Parse(
            """
            <?xml version="1.0" encoding="utf-16"?>
            <TODOLIST NEXTUNIQUEID="3"><CUSTOMATTRIBDEF ID="MR" TYPE="0" ENABLED="1"/><TASK ID="1" TITLE="T1"><CUSTOMATTRIB ID="MR" VALUE="1"/></TASK><TASK ID="2" TITLE="T2"><CUSTOMATTRIB ID="MR" VALUE="2" VALUEDISPLAY="two"/></TASK></TODOLIST>
            """,
            TestData.Clock);

        doc.UpdateTask(1, new UpdateTaskRequest { CustomFields = new[] { new CustomFieldSet { Id = "MR", Value = "99" } } });

        var task2Mr = Task(doc, 2).Elements("CUSTOMATTRIB").Single();
        Assert.Equal("2", (string?)task2Mr.Attribute("VALUE"));
        Assert.Equal("two", (string?)task2Mr.Attribute("VALUEDISPLAY"));   // untouched, mirror intact
    }
}
