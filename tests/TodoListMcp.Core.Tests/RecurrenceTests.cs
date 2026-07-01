using TodoListMcp.Core;

namespace TodoListMcp.Core.Tests;

/// <summary>
/// ToDoList stores a task's recurrence as a &lt;RECURRENCE&gt; element with three coded integers
/// (RECURFREQ + RECURSPECIFIC1/2) plus bookkeeping attributes. The server decodes this read-only onto
/// <c>TodoTask.Recurrence</c>, surfacing every frequency (tolerating deprecated/unknown ones) but not
/// authoring or advancing anything. Values verified against abstractspoon/ToDoList_9.2; see
/// <c>docs/recurrence-spike.md</c>.
/// </summary>
public class RecurrenceTests
{
    private static TodoListDocument DocWith(string recurrenceXml) => TodoListDocument.Parse(
        "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
        "<TODOLIST PROJECTNAME=\"R\" NEXTUNIQUEID=\"2\" FILEFORMAT=\"12\">" +
        "<TASK ID=\"1\" TITLE=\"T\" POS=\"0\" POSSTRING=\"1\">" + recurrenceXml + "</TASK>" +
        "</TODOLIST>", TestData.Clock);

    private static Core.Model.TaskRecurrence Read(string recurrenceXml) =>
        DocWith(recurrenceXml).GetTask(1)!.Recurrence
        ?? throw new Xunit.Sdk.XunitException("Expected a recurrence but got null.");

    // ---- Absence -----------------------------------------------------------

    [Fact]
    public void No_recurrence_element_is_null()
    {
        Assert.Null(DocWith("").GetTask(1)!.Recurrence);
    }

    [Fact]
    public void Once_frequency_is_treated_as_not_recurring()
    {
        Assert.Null(DocWith("<RECURRENCE RECURFREQ=\"0\">Once</RECURRENCE>").GetTask(1)!.Recurrence);
    }

    // ---- Daily-family ------------------------------------------------------

    [Fact]
    public void Every_n_days()
    {
        var r = Read("<RECURRENCE RECURFREQ=\"1\" RECURSPECIFIC1=\"3\" RECURSPECIFIC2=\"0\">Daily</RECURRENCE>");
        Assert.Equal("everyNDays", r.Pattern);
        Assert.Equal(3, r.Interval);
        Assert.Equal("Every 3 days", r.Description);
    }

    [Fact]
    public void Every_single_day_reads_as_interval_one()
    {
        var r = Read("<RECURRENCE RECURFREQ=\"1\" RECURSPECIFIC1=\"1\">Daily</RECURRENCE>");
        Assert.Equal(1, r.Interval);
        Assert.Equal("Every day", r.Description);
    }

    [Fact]
    public void Every_weekday()
    {
        var r = Read("<RECURRENCE RECURFREQ=\"5\">Daily</RECURRENCE>");
        Assert.Equal("everyWeekday", r.Pattern);
        Assert.Null(r.Interval);
        Assert.Equal("Every weekday (Monday to Friday)", r.Description);
    }

    [Fact]
    public void Every_n_weekdays()
    {
        // The issue's own example: RECURFREQ=16, every 3 weekdays (labelled "Daily" in the body).
        var r = Read("<RECURRENCE RECURFREQ=\"16\" RECURSPECIFIC1=\"3\" RECURSPECIFIC2=\"0\">Daily</RECURRENCE>");
        Assert.Equal("everyNWeekdays", r.Pattern);
        Assert.Equal(3, r.Interval);
        Assert.Equal("Every 3 weekdays", r.Description);
    }

    // ---- Weekly ------------------------------------------------------------

    [Fact]
    public void Weekly_on_specific_days()
    {
        // spec2 = DHW bitmask: Monday(0x02) | Wednesday(0x08) | Friday(0x20) = 42.
        var r = Read("<RECURRENCE RECURFREQ=\"2\" RECURSPECIFIC1=\"1\" RECURSPECIFIC2=\"42\">Weekly</RECURRENCE>");
        Assert.Equal("weeklyOnDays", r.Pattern);
        Assert.Equal(new[] { "Monday", "Wednesday", "Friday" }, r.DaysOfWeek);
        Assert.Equal(1, r.Interval);
        Assert.Equal("Weekly on Monday, Wednesday, Friday", r.Description);
    }

    [Fact]
    public void Weekly_every_two_weeks_names_the_interval()
    {
        // Sunday(0x01) | Saturday(0x40) = 65, every 2 weeks.
        var r = Read("<RECURRENCE RECURFREQ=\"2\" RECURSPECIFIC1=\"2\" RECURSPECIFIC2=\"65\">Weekly</RECURRENCE>");
        Assert.Equal(new[] { "Sunday", "Saturday" }, r.DaysOfWeek);
        Assert.Equal("Weekly on Sunday, Saturday, every 2 weeks", r.Description);
    }

    [Fact]
    public void Every_n_weeks_without_specific_days()
    {
        var r = Read("<RECURRENCE RECURFREQ=\"12\" RECURSPECIFIC1=\"3\">Weekly</RECURRENCE>");
        Assert.Equal("everyNWeeks", r.Pattern);
        Assert.Equal(3, r.Interval);
        Assert.Equal("Every 3 weeks", r.Description);
    }

    [Fact]
    public void Weekly_on_every_day_decodes_the_full_mask()
    {
        // DHW_EVERYDAY = 0x7F (127): all seven days, in calendar order (Sunday first).
        var r = Read("<RECURRENCE RECURFREQ=\"2\" RECURSPECIFIC1=\"1\" RECURSPECIFIC2=\"127\">Weekly</RECURRENCE>");
        Assert.Equal(
            new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" },
            r.DaysOfWeek);
    }

    [Fact]
    public void Weekly_with_no_days_set_still_reads_as_weekly()
    {
        // An empty weekday mask (spec2 = 0) — the description omits the day list rather than breaking.
        var r = Read("<RECURRENCE RECURFREQ=\"2\" RECURSPECIFIC1=\"2\" RECURSPECIFIC2=\"0\">Weekly</RECURRENCE>");
        Assert.Equal("weeklyOnDays", r.Pattern);
        Assert.NotNull(r.DaysOfWeek);
        Assert.Empty(r.DaysOfWeek);
        Assert.Equal("Weekly, every 2 weeks", r.Description);
    }

    // ---- Monthly -----------------------------------------------------------

    [Fact]
    public void Monthly_on_day_of_month()
    {
        // spec1 = every N months, spec2 = day of month.
        var r = Read("<RECURRENCE RECURFREQ=\"3\" RECURSPECIFIC1=\"2\" RECURSPECIFIC2=\"15\">Monthly</RECURRENCE>");
        Assert.Equal("monthlyOnDay", r.Pattern);
        Assert.Equal(2, r.Interval);
        Assert.Equal(15, r.DayOfMonth);
        Assert.Equal("Monthly on day 15, every 2 months", r.Description);
    }

    [Fact]
    public void Every_n_months()
    {
        var r = Read("<RECURRENCE RECURFREQ=\"13\" RECURSPECIFIC1=\"6\" RECURSPECIFIC2=\"0\">Monthly</RECURRENCE>");
        Assert.Equal("everyNMonths", r.Pattern);
        Assert.Equal(6, r.Interval);
        Assert.Equal("Every 6 months", r.Description);
        Assert.False(r.PreserveWeekday);
    }

    [Fact]
    public void Every_n_months_preserving_weekday()
    {
        // spec2 is the preserve-weekday flag for this frequency; a set flag must not be dropped.
        var r = Read("<RECURRENCE RECURFREQ=\"13\" RECURSPECIFIC1=\"6\" RECURSPECIFIC2=\"1\">Monthly</RECURRENCE>");
        Assert.True(r.PreserveWeekday);
        Assert.Equal("Every 6 months, preserving weekday", r.Description);
    }

    [Fact]
    public void Monthly_by_weekday()
    {
        // spec1 packs which(LOWORD) | dow(HIWORD): second (2) Tuesday (OLE 3) = 2 | (3<<16) = 196610.
        // spec2 = every N months.
        var r = Read("<RECURRENCE RECURFREQ=\"8\" RECURSPECIFIC1=\"196610\" RECURSPECIFIC2=\"1\">Monthly</RECURRENCE>");
        Assert.Equal("monthlyByWeekday", r.Pattern);
        Assert.Equal("second", r.WeekOfMonth);
        Assert.Equal("Tuesday", r.Weekday);
        Assert.Equal(1, r.Interval);
        Assert.Equal("Monthly on the second Tuesday", r.Description);
    }

    [Fact]
    public void Monthly_last_weekday_decodes_which_five_as_last()
    {
        // which = 5 (last), dow = Friday (OLE 6): 5 | (6<<16) = 393221; every 2 months.
        var r = Read("<RECURRENCE RECURFREQ=\"8\" RECURSPECIFIC1=\"393221\" RECURSPECIFIC2=\"2\">Monthly</RECURRENCE>");
        Assert.Equal("last", r.WeekOfMonth);
        Assert.Equal("Friday", r.Weekday);
        Assert.Equal("Monthly on the last Friday, every 2 months", r.Description);
    }

    [Fact]
    public void Monthly_first_last_weekday()
    {
        // spec1 = 0 (first) / non-zero (last); spec2 = every N months.
        var first = Read("<RECURRENCE RECURFREQ=\"15\" RECURSPECIFIC1=\"0\" RECURSPECIFIC2=\"1\">Monthly</RECURRENCE>");
        Assert.Equal("monthlyOnFirstLastWeekday", first.Pattern);
        Assert.Equal("first", first.WeekOfMonth);
        Assert.Equal("Monthly on the first weekday", first.Description);

        var last = Read("<RECURRENCE RECURFREQ=\"15\" RECURSPECIFIC1=\"1\" RECURSPECIFIC2=\"3\">Monthly</RECURRENCE>");
        Assert.Equal("last", last.WeekOfMonth);
        Assert.Equal("Monthly on the last weekday, every 3 months", last.Description);
    }

    // ---- Yearly ------------------------------------------------------------

    [Fact]
    public void Yearly_on_date_with_plain_month_index()
    {
        // spec1 = month (1–12), spec2 = day of month.
        var r = Read("<RECURRENCE RECURFREQ=\"4\" RECURSPECIFIC1=\"3\" RECURSPECIFIC2=\"14\">Yearly</RECURRENCE>");
        Assert.Equal("yearlyOnDate", r.Pattern);
        Assert.Equal(new[] { "March" }, r.Months);
        Assert.Equal(14, r.DayOfMonth);
        Assert.Equal("Yearly on day 14 of March", r.Description);
    }

    [Fact]
    public void Yearly_on_date_with_dhm_month_bitmask()
    {
        // spec1 = DHM_JANUARY(0x10) | DHM_JULY(0x400) = 1040; spec2 = day 1.
        var r = Read("<RECURRENCE RECURFREQ=\"4\" RECURSPECIFIC1=\"1040\" RECURSPECIFIC2=\"1\">Yearly</RECURRENCE>");
        Assert.Equal(new[] { "January", "July" }, r.Months);
        Assert.Equal(1, r.DayOfMonth);
    }

    [Fact]
    public void Yearly_with_no_valid_month_falls_back_in_the_description()
    {
        // spec1 = 0 is neither a plain index (1–12) nor a DHM bit — no month is surfaced.
        var r = Read("<RECURRENCE RECURFREQ=\"4\" RECURSPECIFIC1=\"0\" RECURSPECIFIC2=\"5\">Yearly</RECURRENCE>");
        Assert.NotNull(r.Months);
        Assert.Empty(r.Months);
        Assert.Equal(5, r.DayOfMonth);
        Assert.Equal("Yearly on day 5 of the given month", r.Description);
    }

    [Fact]
    public void Every_n_years()
    {
        var r = Read("<RECURRENCE RECURFREQ=\"14\" RECURSPECIFIC1=\"2\" RECURSPECIFIC2=\"0\">Yearly</RECURRENCE>");
        Assert.Equal("everyNYears", r.Pattern);
        Assert.Equal(2, r.Interval);
        Assert.Equal("Every 2 years", r.Description);
        Assert.False(r.PreserveWeekday);
    }

    [Fact]
    public void Every_n_years_preserving_weekday()
    {
        var r = Read("<RECURRENCE RECURFREQ=\"14\" RECURSPECIFIC1=\"2\" RECURSPECIFIC2=\"1\">Yearly</RECURRENCE>");
        Assert.True(r.PreserveWeekday);
        Assert.Equal("Every 2 years, preserving weekday", r.Description);
    }

    [Fact]
    public void Preserve_weekday_is_null_for_patterns_that_do_not_use_it()
    {
        var r = Read("<RECURRENCE RECURFREQ=\"1\" RECURSPECIFIC1=\"3\">Daily</RECURRENCE>");
        Assert.Null(r.PreserveWeekday);
    }

    [Fact]
    public void Yearly_by_weekday()
    {
        // spec1 = which/dow: first (1) Monday (OLE 2) = 1 | (2<<16) = 131073.
        // spec2 = month, plain index 11 (November).
        var r = Read("<RECURRENCE RECURFREQ=\"10\" RECURSPECIFIC1=\"131073\" RECURSPECIFIC2=\"11\">Yearly</RECURRENCE>");
        Assert.Equal("yearlyByWeekday", r.Pattern);
        Assert.Equal("first", r.WeekOfMonth);
        Assert.Equal("Monday", r.Weekday);
        Assert.Equal(new[] { "November" }, r.Months);
        Assert.Equal("Yearly on the first Monday of November", r.Description);
    }

    // ---- Bookkeeping attributes -------------------------------------------

    [Fact]
    public void Bookkeeping_defaults_when_attributes_absent()
    {
        var r = Read("<RECURRENCE RECURFREQ=\"1\" RECURSPECIFIC1=\"1\">Daily</RECURRENCE>");
        Assert.Equal("dueDate", r.RecalculateFrom);
        Assert.Equal("reuse", r.OnRecur);
        Assert.Null(r.TotalOccurrences);
        Assert.Null(r.RemainingOccurrences);
        Assert.True(r.PreserveComments);
    }

    [Fact]
    public void Bookkeeping_attributes_are_decoded()
    {
        var r = Read("<RECURRENCE RECURFREQ=\"1\" RECURSPECIFIC1=\"1\" RECURFROM=\"0\" RECURREUSE=\"1\" " +
                     "RECURNUM=\"10\" RECURREMAINING=\"4\" RECURPRESERVECOMMENTS=\"0\">Daily</RECURRENCE>");
        Assert.Equal("doneDate", r.RecalculateFrom);
        Assert.Equal("createNew", r.OnRecur);
        Assert.Equal(10, r.TotalOccurrences);
        Assert.Equal(4, r.RemainingOccurrences);
        Assert.False(r.PreserveComments);
    }

    [Fact]
    public void Infinite_occurrence_counts_map_to_null()
    {
        var r = Read("<RECURRENCE RECURFREQ=\"1\" RECURSPECIFIC1=\"1\" RECURNUM=\"-1\" RECURREMAINING=\"-1\">Daily</RECURRENCE>");
        Assert.Null(r.TotalOccurrences);
        Assert.Null(r.RemainingOccurrences);
    }

    [Fact]
    public void Start_date_recalc_basis()
    {
        var r = Read("<RECURRENCE RECURFREQ=\"1\" RECURSPECIFIC1=\"1\" RECURFROM=\"2\">Daily</RECURRENCE>");
        Assert.Equal("startDate", r.RecalculateFrom);
    }

    [Fact]
    public void Ask_reuse_option_is_decoded()
    {
        var r = Read("<RECURRENCE RECURFREQ=\"1\" RECURSPECIFIC1=\"1\" RECURREUSE=\"2\">Daily</RECURRENCE>");
        Assert.Equal("ask", r.OnRecur);
    }

    // ---- Resilience --------------------------------------------------------

    [Theory]
    [InlineData("")]                              // RECURFREQ absent entirely
    [InlineData(" RECURFREQ=\"\"")]               // present but empty
    [InlineData(" RECURFREQ=\"abc\"")]            // non-numeric
    [InlineData(" RECURFREQ=\"-1\"")]             // TDIR_NONE sentinel
    public void Malformed_or_missing_frequency_is_not_recurring(string freqAttr)
    {
        var doc = DocWith($"<RECURRENCE{freqAttr} RECURSPECIFIC1=\"1\">?</RECURRENCE>");
        Assert.Null(doc.GetTask(1)!.Recurrence);
    }

    [Fact]
    public void Stray_text_node_in_a_task_does_not_break_recurrence_reading()
    {
        // The real recurrence fixture contains a couple of stray "0" text nodes directly inside TASK
        // elements (not seen in a clean app export). They're mixed content we ignore; guard that a
        // bare numeric text node beside the RECURRENCE element doesn't derail the decode.
        var r = Read("<RECURRENCE RECURFREQ=\"1\" RECURSPECIFIC1=\"3\">Daily</RECURRENCE>0");
        Assert.Equal("everyNDays", r.Pattern);
        Assert.Equal(3, r.Interval);
    }

    [Fact]
    public void By_weekday_tolerates_out_of_range_which_and_dow()
    {
        // which = 7 (no ordinal name) and dow = 0 (no OLE day) — surfaced without throwing.
        var spec1 = 7 | (0 << 16);
        var r = Read($"<RECURRENCE RECURFREQ=\"8\" RECURSPECIFIC1=\"{spec1}\" RECURSPECIFIC2=\"1\">Monthly</RECURRENCE>");
        Assert.Equal("monthlyByWeekday", r.Pattern);
        Assert.Equal("7", r.WeekOfMonth);
        Assert.Equal("weekday", r.Weekday);
    }

    [Fact]
    public void Recurrence_element_survives_an_unrelated_edit_and_save()
    {
        // Recurrence is read-only: editing another field must not drop or rewrite the <RECURRENCE>
        // element (including attributes the decoder doesn't model), mirroring the round-trip fidelity
        // guarantee for anything this server doesn't author.
        var doc = DocWith("<RECURRENCE RECURFREQ=\"16\" RECURSPECIFIC1=\"3\" RECURSPECIFIC2=\"0\" " +
                          "RECURREUSE=\"0\" RECURFROM=\"1\" RECURNUM=\"-1\" RECURREMAINING=\"-1\" " +
                          "RECURPRESERVECOMMENTS=\"1\">Daily</RECURRENCE>");
        doc.UpdateTask(1, new() { Title = "Renamed" });

        var xml = doc.ToXmlString();
        Assert.Contains("RECURFREQ=\"16\"", xml);
        Assert.Contains("RECURSPECIFIC1=\"3\"", xml);
        Assert.Contains("RECURPRESERVECOMMENTS=\"1\"", xml);

        // And it still decodes identically after the edit.
        var r = doc.GetTask(1)!.Recurrence!;
        Assert.Equal("everyNWeekdays", r.Pattern);
        Assert.Equal(3, r.Interval);
    }

    // ---- Deprecated / unknown ---------------------------------------------

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(11)]
    [InlineData(99)]
    public void Deprecated_or_unknown_frequency_is_surfaced_as_unsupported(int freq)
    {
        var r = Read($"<RECURRENCE RECURFREQ=\"{freq}\" RECURSPECIFIC1=\"1\">?</RECURRENCE>");
        Assert.Equal("unsupported", r.Pattern);
        Assert.Contains(freq.ToString(), r.Description);
        // Bookkeeping still decodes even when the pattern itself isn't understood.
        Assert.Equal("reuse", r.OnRecur);
    }
}
