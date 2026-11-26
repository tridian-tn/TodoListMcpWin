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
    // One field of every data type, plus every list shape that behaves differently on write: a fixed
    // single-select (STAGE), a fixed multi-select (TAGS), an auto single-select (FREE), an auto
    // multi-select (LOOSE), a fixed single-select with nothing declared (OPEN), a fixed multi-select
    // with nothing declared (OPENTAGS), a fixed list whose declared item carries spaces (PADDED), and
    // a type code no ToDoList version uses (NEWTYPE).
    private const string Xml =
        """
        <?xml version="1.0" encoding="utf-16"?>
        <TODOLIST PROJECTNAME="P" NEXTUNIQUEID="2"><CUSTOMATTRIBDEF ID="TEXT" TYPE="0" ENABLED="1"/><CUSTOMATTRIBDEF ID="WHEN" TYPE="1" ENABLED="1"/><CUSTOMATTRIBDEF ID="COUNT" TYPE="2" ENABLED="1"/><CUSTOMATTRIBDEF ID="RATIO" TYPE="3" ENABLED="1"/><CUSTOMATTRIBDEF ID="DONE" TYPE="4" ENABLED="1"/><CUSTOMATTRIBDEF ID="BADGE" TYPE="5" ENABLED="1"/><CUSTOMATTRIBDEF ID="LINK" TYPE="6" ENABLED="1"/><CUSTOMATTRIBDEF ID="EFFORT" TYPE="7" ENABLED="1"/><CUSTOMATTRIBDEF ID="FRAC" TYPE="8" ENABLED="1"/><CUSTOMATTRIBDEF ID="NEWTYPE" TYPE="12" ENABLED="1"/><CUSTOMATTRIBDEF ID="STAGE" TYPE="512" ENABLED="1" LISTDATA="Alpha&#10;Beta"/><CUSTOMATTRIBDEF ID="TAGS" TYPE="1024" ENABLED="1" LISTDATA="red&#10;green&#10;blue"/><CUSTOMATTRIBDEF ID="FREE" TYPE="256" ENABLED="1" LISTDATA="one&#10;two"/><CUSTOMATTRIBDEF ID="LOOSE" TYPE="768" ENABLED="1" LISTDATA="one&#10;two"/><CUSTOMATTRIBDEF ID="OPEN" TYPE="512" ENABLED="1"/><CUSTOMATTRIBDEF ID="OPENTAGS" TYPE="1024" ENABLED="1"/><CUSTOMATTRIBDEF ID="PADDED" TYPE="512" ENABLED="1" LISTDATA=" Alpha &#10;Beta"/><CUSTOMATTRIBDEF ID="ILIST" TYPE="514" ENABLED="1" LISTDATA="1&#10;2"/><CUSTOMATTRIBDEF ID="DLIST" TYPE="513" ENABLED="1"/><CUSTOMATTRIBDEF ID="MARK" TYPE="517" ENABLED="1" LISTDATA="flag.png:Flag&#10;star.png&#10;dot.png:"/><CUSTOMATTRIBDEF ID="TOTAL" TYPE="9" ENABLED="1"/><TASK ID="1" TITLE="T1" POS="0"/></TODOLIST>
        """;

    private static TodoListDocument Doc() => TodoListDocument.Parse(Xml, TestData.Clock);

    private static XElement Task(TodoListDocument doc) =>
        XDocument.Parse(doc.ToXmlString()).Descendants("TASK").Single();

    /// <summary>
    /// Sets one field on task 1 and returns the VALUE as it was written. Null means the field has no
    /// element OR an element with no VALUE — use <see cref="AssertClears"/> when the claim is that the
    /// field was removed, since a null here doesn't tell those two apart.
    /// </summary>
    private static string? Set(string id, string? value)
    {
        var e = Written(id, value)
            .Elements("CUSTOMATTRIB")
            .FirstOrDefault(x => (string?)x.Attribute("ID") == id.ToUpperInvariant());
        return (string?)e?.Attribute("VALUE");
    }

    /// <summary>Sets one field on task 1 and returns the task element as it was serialised.</summary>
    private static XElement Written(string id, string? value)
    {
        var doc = Doc();
        doc.UpdateTask(1, new UpdateTaskRequest
        {
            CustomFields = new[] { new CustomFieldSet { Id = id, Value = value } },
        });
        return Task(doc);
    }

    /// <summary>
    /// Asserts the field ends up with no element at all — not an element carrying an empty VALUE,
    /// which ToDoList's reader skips and which would therefore look identical through <see cref="Set"/>.
    /// </summary>
    private static void AssertClears(string id, string? value) =>
        Assert.DoesNotContain(
            Written(id, value).Elements("CUSTOMATTRIB"),
            e => string.Equals((string?)e.Attribute("ID"), id, StringComparison.OrdinalIgnoreCase));

    private static ArgumentException Rejects(string id, string value) =>
        Assert.Throws<ArgumentException>(() => Doc().UpdateTask(1, new UpdateTaskRequest
        {
            CustomFields = new[] { new CustomFieldSet { Id = id, Value = value } },
        }));

    /// <summary>
    /// Asserts a value was refused for not fitting its field's type — naming the field, the type and
    /// an expectation. Checking the reason matters: the field ID alone also appears in the "no such
    /// field" message, so asserting that much would pass on a completely different failure.
    /// </summary>
    private static void RejectsAs(string id, string value, string dataType)
    {
        var message = Rejects(id, value).Message;
        Assert.Contains($"custom field '{id}'", message);
        Assert.Contains($"isn't a valid {dataType} value", message);
        Assert.Contains("expected", message);
    }

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
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1899-12-30")]   // the epoch itself converts to serial 0, which reads as no date
    [InlineData("0099-12-31")]   // ToOADate throws below year 100; must surface as a field error
    [InlineData("0100-01-01")]   // in the OLE range but negative, same as a negative serial
    [InlineData("0.0000001")]         // positive, but rounds to 0.000000 once stored
    [InlineData("2958465.9999999")]   // inside the range, but rounds up past the top of it
    [InlineData("2026-02-30")]        // well-formed but no such day
    [InlineData("2025-02-29")]        // not a leap year
    [InlineData("2026-13-01")]
    public void A_value_that_isnt_an_iso_date_is_rejected(string given) =>
        RejectsAs("WHEN", given, "Date");

    [Theory]
    [InlineData("1899-12-31", "1.000000")]              // serial 1: the first storable date
    [InlineData("2958465.999999", "2958465.999999")]    // the last storable serial
    [InlineData("2958465", "2958465.000000")]
    [InlineData("2024-02-29", "45351.000000")]          // a real leap day
    public void The_edges_of_the_storable_range_are_accepted(string given, string stored) =>
        Assert.Equal(stored, Set("WHEN", given));

    [Theory]
    [InlineData("2026-12-01 14:30:45", "46357.604688")]
    [InlineData("2026-12-01T14:30:45.123", "46357.604689")]
    [InlineData("2026-12-01 14:30:45.5", "46357.604693")]
    [InlineData("46357.6041666667", "46357.604167")]    // a finer serial is rounded, not refused
    public void Every_accepted_date_form_is_covered(string given, string stored) =>
        Assert.Equal(stored, Set("WHEN", given));

    [Fact]
    public void A_zoned_iso_date_is_accepted_and_converted_like_a_task_due_date()
    {
        // A client that serialises dates with a zone is the common case, and the same string already
        // works for dueDate (TodoTools.ParseDate). The serial depends on the machine's time zone, so
        // the assertion is that the same instant written two ways lands on the same serial, and that
        // it's a real stored value rather than a refusal.
        var utc = Set("WHEN", "2026-12-01T14:30:00Z")!;
        var offset = Set("WHEN", "2026-12-01T15:30:00+01:00")!;

        Assert.Equal(utc, offset);
        Assert.True(double.Parse(utc, CultureInfo.InvariantCulture) > 0);
        // A different instant must not collapse onto the same serial.
        Assert.NotEqual(utc, Set("WHEN", "2026-12-01T16:30:00+01:00"));
    }

    [Fact]
    public void A_written_date_reads_back_as_the_same_instant()
    {
        var serial = double.Parse(Set("WHEN", "2026-12-01T14:30")!, CultureInfo.InvariantCulture);
        Assert.Equal("2026-12-01 14:30", DateTime.FromOADate(serial).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_date_carries_seconds_but_not_sub_second_precision()
    {
        // Six decimals of a day is about 86ms, so seconds survive the round trip and anything finer
        // doesn't. That's ToDoList's own format rather than a choice made here; stating the drift
        // means nobody has to rediscover it.
        var stored = Set("WHEN", "2026-12-01T14:30:45")!;
        Assert.Equal("46357.604688", stored);

        var read = DateTime.FromOADate(double.Parse(stored, CultureInfo.InvariantCulture));
        Assert.Equal("2026-12-01 14:30:45", read.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        Assert.Equal(43, read.Millisecond);
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
    [InlineData("3:", "3.000000:H")]  // a colon with nothing after it falls back to hours too
    [InlineData(".5d", "0.500000:D")]
    [InlineData("3 HOURS", "3.000000:H")]
    [InlineData("2.5:days", "2.500000:D")]
    // The unit is the trailing letters, so an exponent in the amount isn't mistaken for one — and a
    // bare amount parses the same as the colon form of the same value.
    [InlineData("1e3", "1000.000000:H")]
    [InlineData("1e3h", "1000.000000:H")]
    [InlineData("1e3:H", "1000.000000:H")]
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
    [InlineData("NaN:H")]
    [InlineData("Infinity:D")]
    public void An_unparseable_time_period_is_rejected(string given) =>
        RejectsAs("EFFORT", given, "TimePeriod");

    [Fact]
    public void A_negative_time_period_is_clamped_to_zero() =>
        // SetTime clamps the task's own estimate and time spent the same way, and ToDoList's UI
        // can't produce a negative period at all.
        Assert.Equal("0.000000:H", Set("EFFORT", "-3h"));

    // ---- Bool ----------------------------------------------------------------

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("+")]
    [InlineData("on")]
    [InlineData("y")]
    [InlineData(" true ")]
    public void True_is_stored_as_a_single_plus(string given) => Assert.Equal("+", Set("DONE", given));

    [Theory]
    [InlineData("false")]
    [InlineData("no")]
    [InlineData("n")]
    [InlineData("0")]
    [InlineData("off")]
    public void False_stores_nothing_because_an_absent_value_reads_as_false(string given) =>
        AssertClears("DONE", given);

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
        RejectsAs("DONE", given, "Bool");

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
        RejectsAs("COUNT", given, "Integer");

    [Fact]
    public void A_whole_number_too_large_for_the_field_is_told_the_range()
    {
        // ToDoList reads these with _ttoi, so the 32-bit limit is real — but the message has to say
        // the value is out of range rather than claim it isn't a whole number.
        Assert.Contains("2147483647", Rejects("COUNT", "99999999999").Message);
        Assert.DoesNotContain("2147483647", Rejects("COUNT", "ten").Message);
    }

    [Fact]
    public void The_32_bit_boundary_is_storable_and_one_past_it_is_not()
    {
        // The limit itself, not an order of magnitude past it: this is where an off-by-one or a
        // switch to long/uint parsing would break _ttoi compatibility.
        Assert.Equal("2147483647", Set("COUNT", "2147483647"));
        Assert.Equal("-2147483648", Set("COUNT", "-2147483648"));
        Assert.Contains("2147483647", Rejects("COUNT", "2147483648").Message);
        Assert.Contains("2147483647", Rejects("COUNT", "-2147483649").Message);
    }

    [Theory]
    [InlineData("--5")]
    [InlineData("+-5")]
    [InlineData("5-")]
    public void A_malformed_number_is_not_reported_as_out_of_range(string given) =>
        // Only one leading sign is stripped before the digits check, so these are refused for not
        // being whole numbers rather than for being too large.
        Assert.DoesNotContain("2147483647", Rejects("COUNT", given).Message);

    [Theory]
    [InlineData("3.5", "3.500000")]
    [InlineData("2", "2.000000")]
    [InlineData("-0.25", "-0.250000")]
    public void Double_is_stored_with_the_six_decimals_todolist_writes(string given, string stored) =>
        Assert.Equal(stored, Set("RATIO", given));

    [Theory]
    [InlineData("lots")]
    [InlineData("1/2")]
    // These three parse as doubles but aren't finite, so formatting them would store the literal
    // text "NaN"/"Infinity" in a numeric field — which ToDoList's reader takes as zero.
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void A_non_numeric_double_is_rejected(string given) =>
        RejectsAs("RATIO", given, "Double");

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
    [InlineData("NaN")]
    [InlineData("NaN/2")]
    [InlineData("3/Infinity")]
    public void An_unparseable_fraction_is_rejected(string given) =>
        RejectsAs("FRAC", given, "Fraction");

    [Theory]
    [InlineData("4/2", "4/2")]     // not reduced to 2, and not turned into a decimal
    [InlineData("0/4", "0/4")]
    [InlineData("3/-4", "3/-4")]   // only a zero denominator is refused
    [InlineData(".5", ".5")]
    [InlineData("1e2", "1e2")]
    public void Fraction_text_is_never_re_formatted(string given, string stored) =>
        // "No numeric re-format for this type" is the claim EncodeFraction makes; an unreduced
        // fraction surviving is what proves it.
        Assert.Equal(stored, Set("FRAC", given));

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
        var rejection = Rejects("STAGE", "Gamma");
        Assert.Contains("Gamma", rejection.Message);
        Assert.Contains("Alpha", rejection.Message);   // the message names what the field does allow
        Assert.Contains("Beta", rejection.Message);
        // Every custom-field rejection blames the supplied value, not an internal parameter name.
        Assert.Equal("value", rejection.ParamName);
    }

    [Theory]
    [InlineData("WHEN", "not a date")]
    [InlineData("COUNT", "ten")]
    [InlineData("EFFORT", "soon")]
    public void A_type_mismatch_also_blames_the_supplied_value(string id, string given) =>
        Assert.Equal("value", Rejects(id, given).ParamName);

    [Fact]
    public void A_fixed_multi_select_is_checked_item_by_item()
    {
        Assert.Equal("red\nblue", Set("TAGS", "RED\nBlue"));
        Assert.Contains("purple", Rejects("TAGS", "red\npurple").Message);
    }

    [Fact]
    public void A_multi_select_collapses_duplicate_items()
    {
        // SetMulti collapses duplicates for categories and assignees, so a repeated tag shouldn't
        // show twice here either. Case-only repeats resolve to the list's casing first.
        Assert.Equal("red", Set("TAGS", "red\nRED"));
        Assert.Equal("red\nblue", Set("TAGS", "red\nblue\nRed"));
    }

    [Fact]
    public void Newline_joined_items_on_a_single_select_field_name_the_mistake()
    {
        // Callers are told to newline-join items for multi-select fields, so the error should say the
        // field holds one value rather than report the joined string as an unknown value.
        var message = Rejects("STAGE", "Alpha\nBeta").Message;
        Assert.Contains("single value", message);
        Assert.Contains("multi-select", message);
    }

    [Fact]
    public void A_single_select_auto_list_also_refuses_newline_joined_items() =>
        // The check is about the field holding one value, so it runs before the fixed-list membership
        // check and covers auto lists, which have no declared values to check against.
        Assert.Contains("single value", Rejects("FREE", "one\ntwo").Message);

    [Theory]
    [InlineData("TAGS", "red\r\nblue", "red\nblue")]      // membership-checked multi
    [InlineData("LOOSE", "one\r\ntwo", "one\ntwo")]       // unchecked multi
    [InlineData("FREE", "one\r", "one")]                  // unchecked single-select
    [InlineData("OPEN", "whatever\r", "whatever")]        // fixed single-select, nothing declared
    public void Crlf_joined_items_split_like_newline_joined_ones(string id, string given, string stored) =>
        // A client on Windows is the likeliest source of CRLF, and a stray carriage return would
        // leave ToDoList showing an item called "one\r".
        Assert.Equal(stored, Set(id, given));

    [Theory]
    [InlineData("TAGS", "red\n \nblue", "red\nblue")]
    [InlineData("LOOSE", "one\n\t\ntwo", "one\ntwo")]
    public void A_blank_item_in_a_multi_select_is_dropped(string id, string given, string stored) =>
        // The read path and SetMulti both drop blank items; the checked write path used to send a
        // whitespace-only one through membership and report the empty string as an unknown value.
        Assert.Equal(stored, Set(id, given));

    [Fact]
    public void An_icon_lists_definition_surfaces_images_with_their_labels()
    {
        // The tag is decoded when the definition is read, so get_custom_fields advertises exactly the
        // values a task stores, with the display labels alongside rather than glued on.
        var def = Doc().GetCustomAttributeDefinitions().Single(d => d.Id == "MARK");
        Assert.Equal(new[] { "flag.png", "star.png", "dot.png" }, def.ListItems);
        // ToDoList builds a tag as image + ':' + label, so an unlabelled icon arrives either with no
        // colon (star.png) or with a trailing one (dot.png:). Both fall back to the image, because a
        // blank label would show as a blank option.
        Assert.Equal(new[] { "Flag", "star.png", "dot.png" }, def.ListItemLabels);

        // Every other list type is its own label, so nothing is duplicated into that field.
        Assert.Empty(Doc().GetCustomAttributeDefinitions().Single(d => d.Id == "TAGS").ListItemLabels);
    }

    [Fact]
    public void An_icon_lists_value_is_the_image_not_the_declared_tag()
    {
        // ToDoList declares icon-list items as "image:label" (EncodeImageTag) but stores only the
        // image on the task (DecodeImageTag, then SetSelectedTaskCustomAttributeData with sImage), so
        // the image is the settable value. Verified against ToDoList_9.2.
        Assert.Equal("flag.png", Set("MARK", "flag.png"));
        // Picking the declared tag straight out of get_custom_fields' ListItems works too, and still
        // stores the image rather than the tag.
        Assert.Equal("flag.png", Set("MARK", "flag.png:Flag"));
        // A tag with no colon is all image, so it matches itself; so does one with a trailing colon,
        // whether the caller supplies the bare image or the tag as declared.
        Assert.Equal("star.png", Set("MARK", "star.png"));
        Assert.Equal("dot.png", Set("MARK", "dot.png"));
        Assert.Equal("dot.png", Set("MARK", "dot.png:"));

        // The label alone isn't a value ToDoList would store, and the error lists the images.
        var message = Rejects("MARK", "Flag").Message;
        Assert.Contains("flag.png", message);
        Assert.DoesNotContain("flag.png:Flag", message);
    }

    [Fact]
    public void A_list_fields_data_byte_does_not_coerce_its_items()
    {
        // Encode short-circuits to the list path before any typed coercion, so the data byte only
        // labels the field. ILIST is Fixed+Integer: membership decides, and the integer
        // canonicalisation that would turn 007 into 7 never runs.
        Assert.Equal("1", Set("ILIST", "1"));
        Assert.Contains("007", Rejects("ILIST", "007").Message);

        // DLIST is Fixed+Date but declares nothing, so it takes anything — including a non-date.
        Assert.Equal("not a date", Set("DLIST", "not a date"));
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

    [Fact]
    public void A_declared_item_carrying_spaces_stays_selectable()
    {
        // LISTDATA preserves whitespace, so " Alpha " is a distinct declared value. The exact string
        // get_custom_fields hands back has to be settable, and an exact match beats a trimmed one.
        Assert.Equal(" Alpha ", Set("PADDED", " Alpha "));
        // A trimmed spelling still resolves, as a convenience, to the declared item.
        Assert.Equal(" Alpha ", Set("PADDED", "Alpha"));
    }

    [Theory]
    [InlineData("LOOSE")]      // auto multi-select: no closed set to check against
    [InlineData("OPENTAGS")]   // fixed multi-select that declares nothing yet
    public void An_unchecked_multi_select_is_still_normalised(string id)
    {
        // Normalising is separate from checking membership: these fields accept any item, but the
        // stored value still drops empties and collapses duplicates like every other multi-value field.
        Assert.Equal("one\ntwo", Set(id, "one\n\ntwo\none"));
        AssertClears(id, "\n\n");
    }

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
    public void A_blank_declared_item_is_not_advertised()
    {
        // The write path drops a blank selection, so a blank declared item could never be chosen.
        // Surfacing it would offer a value that can't be set.
        var def = TodoListDocument.Parse(
            """
            <?xml version="1.0" encoding="utf-16"?>
            <TODOLIST NEXTUNIQUEID="2"><CUSTOMATTRIBDEF ID="F" TYPE="1024" ENABLED="1" LISTDATA="red&#10; &#10;blue"/><TASK ID="1" TITLE="t"/></TODOLIST>
            """,
            TestData.Clock).GetCustomAttributeDefinitions().Single();

        Assert.Equal(new[] { "red", "blue" }, def.ListItems);
    }

    // ---- Writing over an existing value --------------------------------------

    [Fact]
    public void A_typed_value_replaces_an_existing_one_in_place()
    {
        // Every other test here writes a new element. This is the other branch of Apply: the element
        // keeps its position and the attributes this server doesn't own, every derived mirror goes, and
        // the new value is encoded the same way.
        var doc = TodoListDocument.Parse(
            """
            <?xml version="1.0" encoding="utf-16"?>
            <TODOLIST NEXTUNIQUEID="2"><CUSTOMATTRIBDEF ID="WHEN" TYPE="1" ENABLED="1"/><TASK ID="1" TITLE="T"><CUSTOMATTRIB ID="WHEN" VALUE="46000.000000" VALUEDISPLAY="old" CALC="5" CALCDISPLAY="five" NOTE="keep"/><TAG>x</TAG></TASK></TODOLIST>
            """,
            TestData.Clock);

        doc.UpdateTask(1, new UpdateTaskRequest
        {
            CustomFields = new[] { new CustomFieldSet { Id = "WHEN", Value = "2026-12-01" } },
        });

        var task = XDocument.Parse(doc.ToXmlString()).Descendants("TASK").Single();
        var when = Assert.Single(task.Elements("CUSTOMATTRIB"));
        Assert.Equal("46357.000000", (string?)when.Attribute("VALUE"));
        Assert.Equal("keep", (string?)when.Attribute("NOTE"));          // unowned attribute survives
        Assert.Null(when.Attribute("VALUEDISPLAY"));                     // all three mirrors dropped
        Assert.Null(when.Attribute("CALC"));
        Assert.Null(when.Attribute("CALCDISPLAY"));
        Assert.Equal("CUSTOMATTRIB", ((XElement)task.FirstNode!).Name.LocalName);   // still first child
    }

    [Fact]
    public void Duplicate_elements_for_one_field_collapse_to_a_single_value()
    {
        // ToDoList keeps one element per field. A file carrying two (the IDs differing only in case,
        // which the lookup matches) must come out with one, and clearing must remove both.
        const string xml =
            """
            <?xml version="1.0" encoding="utf-16"?>
            <TODOLIST NEXTUNIQUEID="2"><CUSTOMATTRIBDEF ID="DONE" TYPE="4" ENABLED="1"/><TASK ID="1" TITLE="T"><CUSTOMATTRIB ID="DONE" VALUE="+"/><CUSTOMATTRIB ID="done" VALUE="+"/></TASK></TODOLIST>
            """;

        var set = TodoListDocument.Parse(xml, TestData.Clock);
        set.UpdateTask(1, new UpdateTaskRequest { CustomFields = new[] { new CustomFieldSet { Id = "DONE", Value = "true" } } });
        var kept = Assert.Single(XDocument.Parse(set.ToXmlString()).Descendants("TASK").Single().Elements("CUSTOMATTRIB"));
        Assert.Equal("+", (string?)kept.Attribute("VALUE"));

        var cleared = TodoListDocument.Parse(xml, TestData.Clock);
        cleared.UpdateTask(1, new UpdateTaskRequest { CustomFields = new[] { new CustomFieldSet { Id = "DONE", Value = "false" } } });
        Assert.Empty(XDocument.Parse(cleared.ToXmlString()).Descendants("TASK").Single().Elements("CUSTOMATTRIB"));
    }

    // ---- Properties that have to hold across every type ----------------------

    [Theory]
    [InlineData("WHEN", "2026-12-01T14:30")]
    [InlineData("EFFORT", "90 mins")]
    [InlineData("DONE", "true")]
    [InlineData("COUNT", "42")]
    [InlineData("RATIO", "3.5")]
    [InlineData("FRAC", "3/4")]
    [InlineData("STAGE", "alpha")]
    [InlineData("TAGS", "red\nblue")]
    [InlineData("MARK", "flag.png")]        // icon list: stored form differs from the declared tag
    [InlineData("MARK", "flag.png:Flag")]
    [InlineData("LOOSE", "one\ntwo")]       // unchecked multi-select: normalised on the way back in
    public void A_stored_value_fed_straight_back_in_is_unchanged(string id, string natural)
    {
        // Read-modify-write has to be safe for a caller that reads a value and writes it back, so
        // whatever is stored must encode to itself. It's also why EncodeDate takes a bare serial.
        var stored = Set(id, natural)!;
        Assert.Equal(stored, Set(id, stored));
    }

    [Fact]
    public void Typed_values_are_parsed_and_formatted_culture_independently()
    {
        // Every InvariantCulture in the encoders is load-bearing for file compatibility. On a
        // comma-decimal culture, dropping one would still pass every other test here while writing a
        // .tdl ToDoList can't read, so this is the only test that would catch it.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.Equal("3.500000", Set("RATIO", "3.5"));
            Assert.Equal("46357.000000", Set("WHEN", "2026-12-01"));
            Assert.Equal("2.500000:D", Set("EFFORT", "2.5d"));
            Assert.Equal("42", Set("COUNT", "42"));
            // A comma decimal is refused rather than read as 35 or as 3.5.
            RejectsAs("RATIO", "3,5", "Double");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
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

    [Theory]
    [InlineData("NOPE")]    // no such field
    [InlineData("TOTAL")]   // a calculation, which can never be set
    public void Add_checks_a_field_id_even_when_its_value_is_empty(string id)
    {
        // An empty value has nothing to clear on a new task, but the ID still has to be real — add
        // used to filter empties out before validating, so a typo passed on add and threw on update.
        var doc = Doc();
        Assert.Throws<ArgumentException>(() => doc.AddTask(new AddTaskRequest
        {
            Title = "New",
            CustomFields = new[] { new CustomFieldSet { Id = id, Value = "" } },
        }));

        Assert.Single(doc.GetTasks());
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
