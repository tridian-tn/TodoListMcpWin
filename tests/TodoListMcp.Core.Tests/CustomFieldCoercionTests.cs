using System.Globalization;
using System.Xml.Linq;
using TodoListMcp.Core;
using TodoListMcp.Core.Model;

namespace TodoListMcp.Core.Tests;

/// <summary>
/// Covers the typed encoding applied when a custom-field value is written: the caller passes a natural
/// value and the server stores what ToDoList stores for that field's data type. The expected on-disk
/// forms come from ToDoList 9.2's <c>TDCCADATA</c> accessors, whose string is what lands in VALUE.
/// </summary>
public class CustomFieldCoercionTests
{
    // One field of every data type, plus a fixed single-select, a fixed multi-select, an auto list, a
    // fixed list with nothing declared yet, and a type code no ToDoList version uses (NEWTYPE).
    private const string Xml =
        """
        <?xml version="1.0" encoding="utf-16"?>
        <TODOLIST PROJECTNAME="P" NEXTUNIQUEID="2"><CUSTOMATTRIBDEF ID="TEXT" TYPE="0" ENABLED="1"/><CUSTOMATTRIBDEF ID="WHEN" TYPE="1" ENABLED="1"/><CUSTOMATTRIBDEF ID="COUNT" TYPE="2" ENABLED="1"/><CUSTOMATTRIBDEF ID="RATIO" TYPE="3" ENABLED="1"/><CUSTOMATTRIBDEF ID="DONE" TYPE="4" ENABLED="1"/><CUSTOMATTRIBDEF ID="BADGE" TYPE="5" ENABLED="1"/><CUSTOMATTRIBDEF ID="LINK" TYPE="6" ENABLED="1"/><CUSTOMATTRIBDEF ID="EFFORT" TYPE="7" ENABLED="1"/><CUSTOMATTRIBDEF ID="FRAC" TYPE="8" ENABLED="1"/><CUSTOMATTRIBDEF ID="NEWTYPE" TYPE="12" ENABLED="1"/><CUSTOMATTRIBDEF ID="STAGE" TYPE="512" ENABLED="1" LISTDATA="Alpha&#10;Beta"/><CUSTOMATTRIBDEF ID="TAGS" TYPE="1024" ENABLED="1" LISTDATA="red&#10;green&#10;blue"/><CUSTOMATTRIBDEF ID="FREE" TYPE="256" ENABLED="1" LISTDATA="one&#10;two"/><CUSTOMATTRIBDEF ID="OPEN" TYPE="512" ENABLED="1"/><TASK ID="1" TITLE="T1" POS="0"/></TODOLIST>
        """;

    private static TodoListDocument Doc() => TodoListDocument.Parse(Xml, TestData.Clock);

    private static XElement Task(TodoListDocument doc) =>
        XDocument.Parse(doc.ToXmlString()).Descendants("TASK").Single();

    /// <summary>Sets one field on task 1 and returns the VALUE as it was written, or null if no element exists.</summary>
    private static string? Set(string id, string? value)
    {
        var doc = Doc();
        doc.UpdateTask(1, new UpdateTaskRequest
        {
            CustomFields = new[] { new CustomFieldSet { Id = id, Value = value } },
        });

        var e = Task(doc).Elements("CUSTOMATTRIB")
            .FirstOrDefault(x => (string?)x.Attribute("ID") == id.ToUpperInvariant());
        return (string?)e?.Attribute("VALUE");
    }

    private static ArgumentException Rejects(string id, string value) =>
        Assert.Throws<ArgumentException>(() => Doc().UpdateTask(1, new UpdateTaskRequest
        {
            CustomFields = new[] { new CustomFieldSet { Id = id, Value = value } },
        }));

    // ---- Date ----------------------------------------------------------------

    [Theory]
    [InlineData("2026-12-01", "46357.000000")]
    [InlineData("  2026-07-16  ", "46219.000000")]
    [InlineData("2026-12-01T14:30", "46357.604167")]
    [InlineData("2026-12-01T14:30:00", "46357.604167")]
    [InlineData("2026-12-01 14:30", "46357.604167")]
    [InlineData("46357.000000", "46357.000000")]   // a serial read off a task round-trips unchanged
    [InlineData("46357", "46357.000000")]
    public void Date_is_stored_as_an_ole_serial(string given, string stored) =>
        Assert.Equal(stored, Set("WHEN", given));

    [Theory]
    [InlineData("12/01/2026")]   // ambiguous: refused rather than guessed at
    [InlineData("1 Dec 2026")]
    [InlineData("tomorrow")]
    [InlineData("0")]            // a non-positive serial is no date at all, matching the reader
    [InlineData("-5")]
    [InlineData("99999999")]     // past the OLE range: refused rather than stored unreadable
    public void A_value_that_isnt_an_iso_date_is_rejected(string given) =>
        Assert.Contains("WHEN", Rejects("WHEN", given).Message);

    [Fact]
    public void A_written_date_reads_back_as_the_same_instant()
    {
        var serial = double.Parse(Set("WHEN", "2026-12-01T14:30")!, CultureInfo.InvariantCulture);
        Assert.Equal("2026-12-01 14:30", DateTime.FromOADate(serial).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
    }

    // ---- Time period ---------------------------------------------------------

    [Theory]
    [InlineData("3h", "3.000000:H")]
    [InlineData("3 hours", "3.000000:H")]
    [InlineData("2.5:D", "2.500000:D")]
    [InlineData("90 mins", "90.000000:I")]
    [InlineData("3 weekdays", "3.000000:K")]
    [InlineData("1.5W", "1.500000:W")]
    [InlineData("4", "4.000000:H")]   // no unit → hours, ToDoList's own default
    public void Time_period_is_stored_as_the_amount_then_its_unit_letter(string given, string stored) =>
        Assert.Equal(stored, Set("EFFORT", given));

    [Fact]
    public void Time_period_units_follow_todolists_letters_minutes_being_i_and_months_m()
    {
        // The trap in ToDoList's unit codes: a bare "m" is months, so minutes has to be asked for by
        // name (or as "i"). Worth stating outright because "90m" reading as 90 months is surprising.
        Assert.Equal("90.000000:M", Set("EFFORT", "90m"));
        Assert.Equal("90.000000:I", Set("EFFORT", "90mins"));
        Assert.Equal("90.000000:I", Set("EFFORT", "90i"));
    }

    [Theory]
    [InlineData("soon")]
    [InlineData("3 fortnights")]
    [InlineData("3:Z")]
    [InlineData(":H")]
    public void An_unparseable_time_period_is_rejected(string given) =>
        Assert.Contains("EFFORT", Rejects("EFFORT", given).Message);

    // ---- Bool ----------------------------------------------------------------

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("+")]
    [InlineData("on")]
    public void True_is_stored_as_a_single_plus(string given) => Assert.Equal("+", Set("DONE", given));

    [Theory]
    [InlineData("false")]
    [InlineData("no")]
    [InlineData("0")]
    [InlineData("off")]
    public void False_stores_nothing_because_an_absent_value_reads_as_false(string given) =>
        Assert.Null(Set("DONE", given));

    [Fact]
    public void False_removes_a_value_that_was_already_true()
    {
        var doc = Doc();
        doc.UpdateTask(1, new UpdateTaskRequest { CustomFields = new[] { new CustomFieldSet { Id = "DONE", Value = "true" } } });
        doc.UpdateTask(1, new UpdateTaskRequest { CustomFields = new[] { new CustomFieldSet { Id = "DONE", Value = "false" } } });

        Assert.Empty(doc.GetTask(1)!.CustomFields);
        Assert.Empty(Task(doc).Elements("CUSTOMATTRIB"));
    }

    [Theory]
    [InlineData("maybe")]
    [InlineData("2")]
    [InlineData("t")]
    public void A_non_boolean_value_is_rejected(string given) =>
        Assert.Contains("DONE", Rejects("DONE", given).Message);

    // ---- Integer, double, fraction -------------------------------------------

    [Theory]
    [InlineData("42", "42")]
    [InlineData("  007 ", "7")]   // canonicalised the way ToDoList's "%d" would write it
    [InlineData("-3", "-3")]
    public void Integer_is_stored_canonically(string given, string stored) =>
        Assert.Equal(stored, Set("COUNT", given));

    [Theory]
    [InlineData("3.7")]           // truncating silently would lose the caller's intent
    [InlineData("ten")]
    [InlineData("12 points")]
    public void A_non_integer_is_rejected_rather_than_truncated(string given) =>
        Assert.Contains("COUNT", Rejects("COUNT", given).Message);

    [Theory]
    [InlineData("3.5", "3.500000")]
    [InlineData("2", "2.000000")]
    [InlineData("-0.25", "-0.250000")]
    public void Double_is_stored_with_the_six_decimals_todolist_writes(string given, string stored) =>
        Assert.Equal(stored, Set("RATIO", given));

    [Theory]
    [InlineData("lots")]
    [InlineData("1/2")]
    public void A_non_numeric_double_is_rejected(string given) =>
        Assert.Contains("RATIO", Rejects("RATIO", given).Message);

    [Theory]
    [InlineData("3/4", "3/4")]
    [InlineData(" 3 / 4 ", "3/4")]
    [InlineData("0.75", "0.75")]   // no slash → ToDoList falls back to reading it as a number
    public void Fraction_is_stored_as_given(string given, string stored) =>
        Assert.Equal(stored, Set("FRAC", given));

    [Theory]
    [InlineData("3/0")]
    [InlineData("a/b")]
    [InlineData("/4")]
    [InlineData("3/")]
    [InlineData("half")]
    public void An_unparseable_fraction_is_rejected(string given) =>
        Assert.Contains("FRAC", Rejects("FRAC", given).Message);

    // ---- Types stored as given -----------------------------------------------

    [Theory]
    [InlineData("TEXT", "anything at all")]
    [InlineData("BADGE", "flag.png")]
    [InlineData("LINK", @"C:\docs\spec.docx")]
    [InlineData("NEWTYPE", "whatever a later ToDoList puts here")]
    public void Untyped_and_unrecognised_fields_are_stored_verbatim(string id, string given) =>
        Assert.Equal(given, Set(id, given));

    // ---- List fields ---------------------------------------------------------

    [Fact]
    public void A_fixed_lists_value_is_stored_in_the_lists_own_casing() =>
        Assert.Equal("Alpha", Set("STAGE", "alpha"));

    [Fact]
    public void A_value_outside_a_fixed_list_is_rejected()
    {
        var message = Rejects("STAGE", "Gamma").Message;
        Assert.Contains("Gamma", message);
        Assert.Contains("Alpha", message);   // the message names what the field does allow
        Assert.Contains("Beta", message);
    }

    [Fact]
    public void A_fixed_multi_select_is_checked_item_by_item()
    {
        Assert.Equal("red\nblue", Set("TAGS", "RED\nBlue"));
        Assert.Contains("purple", Rejects("TAGS", "red\npurple").Message);
    }

    [Fact]
    public void A_multi_select_that_selects_nothing_clears_the_field()
    {
        var doc = Doc();
        doc.UpdateTask(1, new UpdateTaskRequest { CustomFields = new[] { new CustomFieldSet { Id = "TAGS", Value = "red" } } });
        // Newlines with no items between them select nothing, so the field is cleared rather than
        // left holding an empty value ToDoList would have to skip.
        doc.UpdateTask(1, new UpdateTaskRequest { CustomFields = new[] { new CustomFieldSet { Id = "TAGS", Value = "\n\n" } } });

        Assert.Empty(doc.GetTask(1)!.CustomFields);
        Assert.Empty(Task(doc).Elements("CUSTOMATTRIB"));
    }

    [Fact]
    public void An_auto_list_accepts_a_value_it_hasnt_seen() =>
        // An auto list accumulates whatever gets typed into it, so there's nothing to check against.
        Assert.Equal("three", Set("FREE", "three"));

    [Fact]
    public void A_fixed_list_with_no_declared_values_accepts_anything() =>
        Assert.Equal("whatever", Set("OPEN", "whatever"));

    // ---- Add, and refusal being atomic ---------------------------------------

    [Fact]
    public void Add_task_encodes_typed_values_too()
    {
        var task = Doc().AddTask(new AddTaskRequest
        {
            Title = "New",
            CustomFields = new[]
            {
                new CustomFieldSet { Id = "WHEN", Value = "2026-12-01" },
                new CustomFieldSet { Id = "EFFORT", Value = "3h" },
                new CustomFieldSet { Id = "DONE", Value = "true" },
            },
        });

        Assert.Equal("46357.000000", task.CustomFields.Single(f => f.Id == "WHEN").Value);
        Assert.Equal("3.000000:H", task.CustomFields.Single(f => f.Id == "EFFORT").Value);
        Assert.Equal("+", task.CustomFields.Single(f => f.Id == "DONE").Value);
    }

    [Fact]
    public void Add_task_with_a_false_bool_writes_no_element()
    {
        // "false" isn't an empty value, so it survives the add's empty-value filter, but it encodes to
        // nothing — the new task must come out with no element rather than an empty one.
        var doc = Doc();
        var task = doc.AddTask(new AddTaskRequest
        {
            Title = "New",
            CustomFields = new[] { new CustomFieldSet { Id = "DONE", Value = "false" } },
        });

        Assert.Empty(task.CustomFields);
        Assert.Empty(XDocument.Parse(doc.ToXmlString()).Descendants("TASK")
            .Single(t => (int)t.Attribute("ID")! == task.Id).Elements("CUSTOMATTRIB"));
    }

    [Fact]
    public void A_value_that_doesnt_fit_its_type_refuses_the_whole_update()
    {
        var doc = Doc();
        Assert.Throws<ArgumentException>(() => doc.UpdateTask(1, new UpdateTaskRequest
        {
            Title = "Renamed",
            CustomFields = new[]
            {
                new CustomFieldSet { Id = "TEXT", Value = "fine" },
                new CustomFieldSet { Id = "WHEN", Value = "not a date" },
            },
        }));

        var t = doc.GetTask(1)!;
        Assert.Equal("T1", t.Title);        // the title change rolled back with it
        Assert.Empty(t.CustomFields);       // and the valid assignment was not half-applied
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void Add_task_with_a_bad_typed_value_burns_no_id()
    {
        var doc = Doc();
        var before = doc.NextUniqueId;
        Assert.Throws<ArgumentException>(() => doc.AddTask(new AddTaskRequest
        {
            Title = "New",
            CustomFields = new[] { new CustomFieldSet { Id = "EFFORT", Value = "ages" } },
        }));

        Assert.Single(doc.GetTasks());
        Assert.Equal(before, doc.NextUniqueId);
        Assert.False(doc.IsDirty);
    }
}
