using System.Xml.Linq;
using TodoListMcp.Core;
using TodoListMcp.Core.Model;

namespace TodoListMcp.Core.Tests;

/// <summary>
/// Covers authoring recurrence: <see cref="RecurrenceFormat.Build"/> and the
/// <c>SetRecurrence</c>/<c>ClearRecurrence</c> mutations. The headline is a round-trip against the real
/// fixture — for each pristine (never-completed) pattern, the encoder must reproduce ToDoList's own
/// on-disk attributes exactly. Encoding verified against abstractspoon/ToDoList_9.2.
/// </summary>
public class RecurrenceWriteTests
{
    private static TodoListDocument OneTaskDoc() => TodoListDocument.Parse(
        "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
        "<TODOLIST PROJECTNAME=\"W\" NEXTUNIQUEID=\"2\" FILEFORMAT=\"12\">" +
        "<TASK ID=\"1\" TITLE=\"T\" POS=\"0\" POSSTRING=\"1\"/></TODOLIST>", TestData.Clock);

    private static XElement Built(SetRecurrenceRequest req) => RecurrenceFormat.Build(req);

    // ---- Per-pattern encoding ---------------------------------------------

    [Fact]
    public void Every_n_days_encodes_freq_and_interval()
    {
        var e = Built(new() { Pattern = RecurrencePattern.EveryNDays, Interval = 3 });
        Assert.Equal("1", (string?)e.Attribute("RECURFREQ"));
        Assert.Equal("3", (string?)e.Attribute("RECURSPECIFIC1"));
        Assert.Equal("0", (string?)e.Attribute("RECURSPECIFIC2"));
        Assert.Equal("Daily", e.Value);
    }

    [Fact]
    public void Every_weekday_encodes_zero_specifics()
    {
        var e = Built(new() { Pattern = RecurrencePattern.EveryWeekday });
        Assert.Equal("5", (string?)e.Attribute("RECURFREQ"));
        Assert.Equal("0", (string?)e.Attribute("RECURSPECIFIC1"));
        Assert.Equal("0", (string?)e.Attribute("RECURSPECIFIC2"));
    }

    [Fact]
    public void Weekly_on_days_encodes_the_weekday_bitmask()
    {
        var e = Built(new()
        {
            Pattern = RecurrencePattern.WeeklyOnDays,
            Interval = 1,
            DaysOfWeek = new[] { "Monday", "Wednesday", "Friday" },
        });
        Assert.Equal("2", (string?)e.Attribute("RECURFREQ"));
        Assert.Equal("1", (string?)e.Attribute("RECURSPECIFIC1"));
        Assert.Equal("42", (string?)e.Attribute("RECURSPECIFIC2"));   // Mon|Wed|Fri = 2|8|32
    }

    [Fact]
    public void Weekday_names_accept_three_letter_abbreviations()
    {
        var e = Built(new()
        {
            Pattern = RecurrencePattern.WeeklyOnDays,
            Interval = 1,
            DaysOfWeek = new[] { "sun", "SAT" },
        });
        Assert.Equal("65", (string?)e.Attribute("RECURSPECIFIC2"));   // Sun|Sat = 1|64
    }

    [Fact]
    public void Weekly_on_days_interval_defaults_to_one_when_omitted()
    {
        var e = Built(new() { Pattern = RecurrencePattern.WeeklyOnDays, DaysOfWeek = new[] { "Monday" } });
        Assert.Equal("1", (string?)e.Attribute("RECURSPECIFIC1"));
    }

    [Fact]
    public void Monthly_on_day_interval_defaults_to_one_when_omitted()
    {
        var e = Built(new() { Pattern = RecurrencePattern.MonthlyOnDay, DayOfMonth = 15 });
        Assert.Equal("1", (string?)e.Attribute("RECURSPECIFIC1"));
    }

    [Fact]
    public void Monthly_on_day_encodes_interval_and_day()
    {
        var e = Built(new() { Pattern = RecurrencePattern.MonthlyOnDay, Interval = 2, DayOfMonth = 15 });
        Assert.Equal("3", (string?)e.Attribute("RECURFREQ"));
        Assert.Equal("2", (string?)e.Attribute("RECURSPECIFIC1"));
        Assert.Equal("15", (string?)e.Attribute("RECURSPECIFIC2"));
    }

    [Fact]
    public void Yearly_on_date_encodes_the_month_as_a_bitmask_even_for_one_month()
    {
        // Matches ToDoList: a single month is still the DHM bit (March = 0x40 = 64), not index 3.
        var e = Built(new()
        {
            Pattern = RecurrencePattern.YearlyOnDate,
            Months = new[] { "March" },
            DayOfMonth = 14,
        });
        Assert.Equal("4", (string?)e.Attribute("RECURFREQ"));
        Assert.Equal("64", (string?)e.Attribute("RECURSPECIFIC1"));
        Assert.Equal("14", (string?)e.Attribute("RECURSPECIFIC2"));
    }

    [Fact]
    public void Yearly_on_multiple_months_ors_the_bitmask()
    {
        var e = Built(new()
        {
            Pattern = RecurrencePattern.YearlyOnDate,
            Months = new[] { "January", "February", "November" },
            DayOfMonth = 14,
        });
        Assert.Equal("16432", (string?)e.Attribute("RECURSPECIFIC1"));   // 16|32|16384
    }

    // ---- Bookkeeping -------------------------------------------------------

    [Fact]
    public void Bookkeeping_defaults_match_todolist()
    {
        var e = Built(new() { Pattern = RecurrencePattern.EveryNDays, Interval = 1 });
        Assert.Equal("0", (string?)e.Attribute("RECURREUSE"));            // reuse
        Assert.Equal("1", (string?)e.Attribute("RECURFROM"));            // due date
        Assert.Equal("-1", (string?)e.Attribute("RECURNUM"));            // infinite
        Assert.Equal("-1", (string?)e.Attribute("RECURREMAINING"));
        Assert.Equal("1", (string?)e.Attribute("RECURPRESERVECOMMENTS"));
    }

    [Fact]
    public void Bookkeeping_options_are_encoded()
    {
        var e = Built(new()
        {
            Pattern = RecurrencePattern.EveryNDays,
            Interval = 1,
            RecalcFrom = RecurrenceRecalcFrom.DoneDate,
            OnRecur = RecurrenceReuse.CreateNew,
            Occurrences = 5,
            PreserveComments = false,
        });
        Assert.Equal("1", (string?)e.Attribute("RECURREUSE"));           // createNew
        Assert.Equal("0", (string?)e.Attribute("RECURFROM"));           // done date
        Assert.Equal("5", (string?)e.Attribute("RECURNUM"));
        Assert.Equal("5", (string?)e.Attribute("RECURREMAINING"));
        Assert.Equal("0", (string?)e.Attribute("RECURPRESERVECOMMENTS"));
    }

    // ---- Validation --------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_interval_is_rejected(int interval) =>
        Assert.Throws<ArgumentException>(() =>
            Built(new() { Pattern = RecurrencePattern.EveryNDays, Interval = interval }));

    [Fact]
    public void Missing_interval_is_rejected() =>
        Assert.Throws<ArgumentException>(() => Built(new() { Pattern = RecurrencePattern.EveryNWeeks }));

    [Fact]
    public void Weekly_without_days_is_rejected() =>
        Assert.Throws<ArgumentException>(() =>
            Built(new() { Pattern = RecurrencePattern.WeeklyOnDays, Interval = 1 }));

    [Fact]
    public void Unknown_weekday_name_is_rejected() =>
        Assert.Throws<ArgumentException>(() =>
            Built(new() { Pattern = RecurrencePattern.WeeklyOnDays, Interval = 1, DaysOfWeek = new[] { "Funday" } }));

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public void Day_of_month_out_of_range_is_rejected(int day) =>
        Assert.Throws<ArgumentException>(() =>
            Built(new() { Pattern = RecurrencePattern.MonthlyOnDay, Interval = 1, DayOfMonth = day }));

    [Fact]
    public void Yearly_without_months_is_rejected() =>
        Assert.Throws<ArgumentException>(() =>
            Built(new() { Pattern = RecurrencePattern.YearlyOnDate, DayOfMonth = 1 }));

    [Fact]
    public void Unknown_month_name_is_rejected() =>
        Assert.Throws<ArgumentException>(() =>
            Built(new() { Pattern = RecurrencePattern.YearlyOnDate, Months = new[] { "Smarch" }, DayOfMonth = 1 }));

    [Fact]
    public void Zero_occurrences_is_rejected() =>
        Assert.Throws<ArgumentException>(() =>
            Built(new() { Pattern = RecurrencePattern.EveryNDays, Interval = 1, Occurrences = 0 }));

    [Fact]
    public void Weekly_with_an_explicit_non_positive_interval_is_rejected() =>
        Assert.Throws<ArgumentException>(() =>
            Built(new() { Pattern = RecurrencePattern.WeeklyOnDays, Interval = 0, DaysOfWeek = new[] { "Monday" } }));

    [Fact]
    public void Monthly_on_day_with_an_explicit_non_positive_interval_is_rejected() =>
        Assert.Throws<ArgumentException>(() =>
            Built(new() { Pattern = RecurrencePattern.MonthlyOnDay, Interval = 0, DayOfMonth = 15 }));

    // ---- SetRecurrence / ClearRecurrence mutations -------------------------

    [Fact]
    public void Set_recurrence_adds_the_element_and_is_readable()
    {
        var doc = OneTaskDoc();
        doc.SetRecurrence(1, new() { Pattern = RecurrencePattern.WeeklyOnDays, Interval = 2, DaysOfWeek = new[] { "Tuesday" } });
        var r = doc.GetTask(1)!.Recurrence!;
        Assert.Equal("weeklyOnDays", r.Pattern);
        Assert.Equal(2, r.Interval);
        Assert.Equal(new[] { "Tuesday" }, r.DaysOfWeek);
        Assert.True(doc.IsDirty);
    }

    [Fact]
    public void Set_recurrence_replaces_an_existing_rule()
    {
        var doc = OneTaskDoc();
        doc.SetRecurrence(1, new() { Pattern = RecurrencePattern.EveryNDays, Interval = 3 });
        doc.SetRecurrence(1, new() { Pattern = RecurrencePattern.EveryNYears, Interval = 1 });

        // Exactly one RECURRENCE element remains, carrying the new rule.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(doc.ToXmlString(), "<RECURRENCE"));
        Assert.Equal("everyNYears", doc.GetTask(1)!.Recurrence!.Pattern);
    }

    [Fact]
    public void Invalid_request_throws_and_leaves_the_document_untouched()
    {
        var doc = OneTaskDoc();
        Assert.Throws<ArgumentException>(() =>
            doc.SetRecurrence(1, new() { Pattern = RecurrencePattern.WeeklyOnDays, Interval = 1 }));
        Assert.Null(doc.GetTask(1)!.Recurrence);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void Set_recurrence_on_a_locked_task_is_refused()
    {
        var doc = TodoListDocument.Parse(
            "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
            "<TODOLIST PROJECTNAME=\"W\" NEXTUNIQUEID=\"2\">" +
            "<TASK ID=\"1\" TITLE=\"T\" LOCK=\"1\" POS=\"0\" POSSTRING=\"1\"/></TODOLIST>", TestData.Clock);
        Assert.Throws<TaskLockedException>(() =>
            doc.SetRecurrence(1, new() { Pattern = RecurrencePattern.EveryNDays, Interval = 1 }));
    }

    [Fact]
    public void Clear_recurrence_removes_the_element()
    {
        var doc = OneTaskDoc();
        doc.SetRecurrence(1, new() { Pattern = RecurrencePattern.EveryNDays, Interval = 1 });
        doc.ClearRecurrence(1);
        Assert.Null(doc.GetTask(1)!.Recurrence);
        Assert.DoesNotContain("<RECURRENCE", doc.ToXmlString());
    }

    [Fact]
    public void Clear_recurrence_on_a_non_recurring_task_is_a_clean_no_op()
    {
        var doc = OneTaskDoc();
        doc.ClearRecurrence(1);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void Set_recurrence_on_a_missing_task_throws()
    {
        var doc = OneTaskDoc();
        Assert.Throws<TaskNotFoundException>(() =>
            doc.SetRecurrence(99, new() { Pattern = RecurrencePattern.EveryNDays, Interval = 1 }));
    }

    [Fact]
    public void Clear_recurrence_on_a_missing_task_throws()
    {
        var doc = OneTaskDoc();
        Assert.Throws<TaskNotFoundException>(() => doc.ClearRecurrence(99));
    }

    [Fact]
    public void Authored_rule_reads_back_through_the_decoder()
    {
        // Write↔read symmetry: what the writer emits must decode to the same rule (the fixture pins the
        // raw bytes; this pins the semantic round-trip, including the bookkeeping fields).
        var doc = OneTaskDoc();
        doc.SetRecurrence(1, new()
        {
            Pattern = RecurrencePattern.MonthlyOnDay,
            Interval = 2,
            DayOfMonth = 15,
            RecalcFrom = RecurrenceRecalcFrom.DoneDate,
            OnRecur = RecurrenceReuse.Ask,
            Occurrences = 5,
            PreserveComments = false,
        });

        var r = doc.GetTask(1)!.Recurrence!;
        Assert.Equal("monthlyOnDay", r.Pattern);
        Assert.Equal(2, r.Interval);
        Assert.Equal(15, r.DayOfMonth);
        Assert.Equal("doneDate", r.RecalculateFrom);
        Assert.Equal("ask", r.OnRecur);
        Assert.Equal(5, r.TotalOccurrences);
        Assert.Equal(5, r.RemainingOccurrences);
        Assert.False(r.PreserveComments);
    }

    // ---- Fixture round-trip: encoder reproduces ToDoList's own bytes -------

    public static IEnumerable<object[]> PristinePatterns() => new[]
    {
        new object[] { 27, new SetRecurrenceRequest { Pattern = RecurrencePattern.EveryNDays, Interval = 3 } },
        new object[] { 28, new SetRecurrenceRequest { Pattern = RecurrencePattern.EveryWeekday, RecalcFrom = RecurrenceRecalcFrom.DoneDate, OnRecur = RecurrenceReuse.CreateNew } },
        new object[] { 29, new SetRecurrenceRequest { Pattern = RecurrencePattern.EveryNWeekdays, Interval = 3, RecalcFrom = RecurrenceRecalcFrom.StartDate, OnRecur = RecurrenceReuse.Ask } },
        new object[] { 30, new SetRecurrenceRequest { Pattern = RecurrencePattern.WeeklyOnDays, Interval = 1, DaysOfWeek = new[] { "Monday", "Wednesday", "Friday" } } },
        new object[] { 31, new SetRecurrenceRequest { Pattern = RecurrencePattern.WeeklyOnDays, Interval = 2, DaysOfWeek = new[] { "Monday", "Wednesday", "Friday" } } },
        new object[] { 32, new SetRecurrenceRequest { Pattern = RecurrencePattern.WeeklyOnDays, Interval = 1, DaysOfWeek = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" } } },
        new object[] { 48, new SetRecurrenceRequest { Pattern = RecurrencePattern.WeeklyOnDays, Interval = 1, DaysOfWeek = new[] { "Monday" } } },
        new object[] { 33, new SetRecurrenceRequest { Pattern = RecurrencePattern.EveryNWeeks, Interval = 1, PreserveComments = false } },
        new object[] { 34, new SetRecurrenceRequest { Pattern = RecurrencePattern.MonthlyOnDay, Interval = 1, DayOfMonth = 15 } },
        new object[] { 36, new SetRecurrenceRequest { Pattern = RecurrencePattern.MonthlyOnDay, Interval = 2, DayOfMonth = 15 } },
        new object[] { 45, new SetRecurrenceRequest { Pattern = RecurrencePattern.EveryNMonths, Interval = 3 } },
        new object[] { 42, new SetRecurrenceRequest { Pattern = RecurrencePattern.YearlyOnDate, Months = new[] { "March" }, DayOfMonth = 14 } },
        new object[] { 47, new SetRecurrenceRequest { Pattern = RecurrencePattern.YearlyOnDate, Months = new[] { "January", "February", "November" }, DayOfMonth = 14 } },
        new object[] { 44, new SetRecurrenceRequest { Pattern = RecurrencePattern.EveryNYears, Interval = 2 } },
    };

    [Theory]
    [MemberData(nameof(PristinePatterns))]
    public void Encoder_reproduces_the_real_fixture_encoding(int taskId, SetRecurrenceRequest req)
    {
        var expected = FixtureRecurrenceElement(taskId);
        var actual = RecurrenceFormat.Build(req);

        // Same attribute set and values (order-independent), and the same label body.
        var expectedAttrs = expected.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value);
        var actualAttrs = actual.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value);
        Assert.Equal(expectedAttrs, actualAttrs);
        Assert.Equal(expected.Value.Trim(), actual.Value.Trim());
    }

    private static XElement FixtureRecurrenceElement(int taskId)
    {
        var doc = XDocument.Load(TestData.RecurrenceFilePath());
        var task = doc.Descendants("TASK").FirstOrDefault(t => (int?)t.Attribute("ID") == taskId)
            ?? throw new Xunit.Sdk.XunitException($"Task {taskId} not found in fixture.");
        return task.Element("RECURRENCE")
            ?? throw new Xunit.Sdk.XunitException($"Task {taskId} has no RECURRENCE element.");
    }

    // ---- Persistence -------------------------------------------------------

    [Fact]
    public void Set_recurrence_survives_save_and_reload()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tdlmcp_recur_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "out.tdl");
            var doc = OneTaskDoc();
            doc.SetRecurrence(1, new()
            {
                Pattern = RecurrencePattern.YearlyOnDate,
                Months = new[] { "March" },
                DayOfMonth = 14,
            });
            doc.SaveAs(path);

            var r = TodoListDocument.Load(path).GetTask(1)!.Recurrence!;
            Assert.Equal("yearlyOnDate", r.Pattern);
            Assert.Equal(new[] { "March" }, r.Months);
            Assert.Equal(14, r.DayOfMonth);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ } }
    }
}
