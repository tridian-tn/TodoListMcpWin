using TodoListMcp.Core;
using TodoListMcp.Core.Model;

namespace TodoListMcp.Core.Tests;

/// <summary>
/// Validates the recurrence decoder against <c>Introduction-Recurrence.tdl</c> — a real ToDoList 9.1
/// file authored through the app's recurrence UI, one task per pattern. Unlike <see cref="RecurrenceTests"/>
/// (hand-written XML), these pin the decoder to genuine on-disk encodings, so they'd catch a wrong
/// constant that happens to be self-consistent with my synthetic fixtures.
/// </summary>
public class RecurrenceFixtureTests
{
    private static readonly TodoListDocument Doc = TodoListDocument.Load(TestData.RecurrenceFilePath());

    private static TaskRecurrence Recur(int id) =>
        Doc.GetTask(id)?.Recurrence
        ?? throw new Xunit.Sdk.XunitException($"Task {id} had no recurrence.");

    [Fact]
    public void Parent_task_does_not_recur()
    {
        var parent = Doc.GetTask(26)!;
        Assert.Equal("Recurrence Tasks", parent.Title);
        Assert.Null(parent.Recurrence);
        // Sanity: the fixture's recurring tasks are its children.
        Assert.NotEmpty(parent.Subtasks);
    }

    [Fact]
    public void Every_3_days_reuse_from_due_date()
    {
        var r = Recur(27);
        Assert.Equal("everyNDays", r.Pattern);
        Assert.Equal(3, r.Interval);
        Assert.Equal("dueDate", r.RecalculateFrom);
        Assert.Equal("reuse", r.OnRecur);
    }

    [Fact]
    public void Every_weekday_create_new_from_completion()
    {
        var r = Recur(28);
        Assert.Equal("everyWeekday", r.Pattern);
        Assert.Equal("doneDate", r.RecalculateFrom);
        Assert.Equal("createNew", r.OnRecur);
    }

    [Fact]
    public void Every_3_weekdays_ask_from_start_date()
    {
        var r = Recur(29);
        Assert.Equal("everyNWeekdays", r.Pattern);
        Assert.Equal(3, r.Interval);
        Assert.Equal("startDate", r.RecalculateFrom);
        Assert.Equal("ask", r.OnRecur);
    }

    [Fact]
    public void Weekly_mon_wed_fri_every_week()
    {
        var r = Recur(30);
        Assert.Equal("weeklyOnDays", r.Pattern);
        Assert.Equal(new[] { "Monday", "Wednesday", "Friday" }, r.DaysOfWeek);
        Assert.Equal(1, r.Interval);
    }

    [Fact]
    public void Weekly_on_a_single_day()
    {
        // RECURSPECIFIC2 = 2 = DHW_MONDAY only.
        var r = Recur(48);
        Assert.Equal("weeklyOnDays", r.Pattern);
        Assert.Equal(new[] { "Monday" }, r.DaysOfWeek);
        Assert.Equal("Weekly on Monday", r.Description);
    }

    [Fact]
    public void Weekly_mon_wed_fri_every_two_weeks()
    {
        var r = Recur(31);
        Assert.Equal(new[] { "Monday", "Wednesday", "Friday" }, r.DaysOfWeek);
        Assert.Equal(2, r.Interval);
        Assert.Equal("Weekly on Monday, Wednesday, Friday, every 2 weeks", r.Description);
    }

    [Fact]
    public void Weekly_on_all_seven_days()
    {
        var r = Recur(32);
        Assert.Equal(
            new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" },
            r.DaysOfWeek);
    }

    [Fact]
    public void Every_week_with_preserve_comments_off()
    {
        var r = Recur(33);
        Assert.Equal("everyNWeeks", r.Pattern);
        Assert.Equal(1, r.Interval);
        Assert.False(r.PreserveComments);
    }

    [Fact]
    public void Monthly_on_day_15_every_month()
    {
        var r = Recur(34);
        Assert.Equal("monthlyOnDay", r.Pattern);
        Assert.Equal(15, r.DayOfMonth);
        Assert.Equal(1, r.Interval);
    }

    [Fact]
    public void Monthly_on_day_15_every_two_months()
    {
        var r = Recur(36);
        Assert.Equal("monthlyOnDay", r.Pattern);
        Assert.Equal(15, r.DayOfMonth);
        Assert.Equal(2, r.Interval);
    }

    [Fact]
    public void Monthly_on_the_second_tuesday()
    {
        var r = Recur(37);
        Assert.Equal("monthlyByWeekday", r.Pattern);
        Assert.Equal("second", r.WeekOfMonth);
        Assert.Equal("Tuesday", r.Weekday);
    }

    [Fact]
    public void Monthly_on_the_last_friday()
    {
        var r = Recur(39);
        Assert.Equal("monthlyByWeekday", r.Pattern);
        Assert.Equal("last", r.WeekOfMonth);
        Assert.Equal("Friday", r.Weekday);
    }

    [Fact]
    public void Monthly_on_the_first_weekday()
    {
        var r = Recur(40);
        Assert.Equal("monthlyOnFirstLastWeekday", r.Pattern);
        Assert.Equal("first", r.WeekOfMonth);
    }

    [Fact]
    public void Monthly_on_the_last_weekday()
    {
        var r = Recur(41);
        Assert.Equal("monthlyOnFirstLastWeekday", r.Pattern);
        Assert.Equal("last", r.WeekOfMonth);
    }

    [Fact]
    public void Yearly_on_14th_march_uses_the_month_bitmask()
    {
        // Real ToDoList encodes the month as a DHM_* bitmask (March = 0x40 = 64), not a plain index.
        var r = Recur(42);
        Assert.Equal("yearlyOnDate", r.Pattern);
        Assert.Equal(new[] { "March" }, r.Months);
        Assert.Equal(14, r.DayOfMonth);
    }

    [Fact]
    public void Yearly_across_multiple_months_decodes_the_combined_bitmask()
    {
        // Real multi-month selection: SPEC1 = 16432 = DHM_JANUARY(0x10) | FEBRUARY(0x20) | NOVEMBER(0x4000).
        var r = Recur(47);
        Assert.Equal("yearlyOnDate", r.Pattern);
        Assert.Equal(new[] { "January", "February", "November" }, r.Months);
        Assert.Equal(14, r.DayOfMonth);
        Assert.Equal("Yearly on day 14 of January, February, November", r.Description);
    }

    [Fact]
    public void Yearly_on_first_monday_of_november_uses_the_month_bitmask()
    {
        // Month slot is again a bitmask (November = 0x4000 = 16384).
        var r = Recur(43);
        Assert.Equal("yearlyByWeekday", r.Pattern);
        Assert.Equal("first", r.WeekOfMonth);
        Assert.Equal("Monday", r.Weekday);
        Assert.Equal(new[] { "November" }, r.Months);
    }

    [Fact]
    public void Every_2_years()
    {
        var r = Recur(44);
        Assert.Equal("everyNYears", r.Pattern);
        Assert.Equal(2, r.Interval);
        Assert.False(r.PreserveWeekday);
    }

    [Fact]
    public void Every_3_months()
    {
        var r = Recur(45);
        Assert.Equal("everyNMonths", r.Pattern);
        Assert.Equal(3, r.Interval);
        Assert.False(r.PreserveWeekday);
    }

    // ---- Occurrence counts & completion interaction ------------------------

    [Fact]
    public void Finite_series_completed_once_reports_remaining_count()
    {
        // Set to run twice, completed once in-app: RECURNUM=2, RECURREMAINING=1.
        var r = Recur(46);
        Assert.Equal(2, r.TotalOccurrences);
        Assert.Equal(1, r.RemainingOccurrences);
    }

    [Fact]
    public void Completing_a_reuse_recurrence_in_app_advances_rather_than_marking_done()
    {
        // "Completed recurring task": completed in the app, but because it reuses the task and recurs
        // infinitely, ToDoList advanced it and left it OPEN — no DONEDATE, still recurring. This is the
        // real behaviour our own CompleteTask does NOT reproduce (it just stamps DONEDATE): the phase-2
        // completion decision hangs on exactly this.
        var task = Doc.GetTask(49)!;
        Assert.False(task.IsDone);
        Assert.NotNull(task.Recurrence);
        Assert.Null(task.Recurrence!.RemainingOccurrences);   // still infinite
    }

    [Fact]
    public void Completing_a_create_new_recurrence_marks_the_original_done_and_keeps_the_rule()
    {
        // "on completion create new" (RECURREUSE=1): completing marks the ORIGINAL done — DONEDATE
        // set, 100% — while keeping its recurrence rule. This is the one recurrence shape that IS
        // genuinely done on disk (unlike the reuse case above, which advances and stays open).
        var task = Doc.GetTask(50)!;
        Assert.True(task.IsDone);
        Assert.NotNull(task.DoneDate);
        Assert.Equal("createNew", task.Recurrence!.OnRecur);
        Assert.Equal("everyNDays", task.Recurrence.Pattern);
    }

    [Fact]
    public void Completing_a_create_new_recurrence_leaves_a_done_original_and_an_open_copy()
    {
        // ToDoList spawns a fresh copy for the next occurrence, so the file holds two same-titled
        // tasks carrying the same rule: one done (the original) and one open (the new instance).
        var matches = Doc.Search(new TaskQuery { Text = "on completion create new" });
        Assert.Equal(2, matches.Count);
        Assert.Single(matches, t => t.IsDone);
        Assert.Single(matches, t => !t.IsDone);
        Assert.All(matches, t => Assert.Equal("createNew", t.Recurrence!.OnRecur));
    }

    [Fact]
    public void Finite_series_final_occurrence_completes_as_done_with_remaining_frozen_at_one()
    {
        // "max completions of 2", completed twice. ToDoList only advances a recurrence while more than
        // one occurrence remains (CanRecur: m_nRemainingOccur > 1), so the final occurrence is a normal
        // terminal completion: the task is marked done and RECURREMAINING stays at 1 — a finite series
        // never counts down to 0 on disk.
        var task = Doc.GetTask(53)!;
        Assert.True(task.IsDone);
        Assert.NotNull(task.DoneDate);
        Assert.Equal("reuse", task.Recurrence!.OnRecur);
        Assert.Equal(2, task.Recurrence.TotalOccurrences);
        Assert.Equal(1, task.Recurrence.RemainingOccurrences);
    }

    [Fact]
    public void Remaining_count_alone_does_not_reveal_series_position()
    {
        // Task 46 was completed once (remaining 2->1, then advanced and reopened); task 53 was completed
        // twice (the terminal occurrence, left done). Both land on RECURREMAINING=1 with identical rules,
        // so how far a series has progressed can't be read from the counts — DONEDATE is what tells you
        // it has ended, which is why the reader keeps IsDone and Recurrence independent.
        var open = Doc.GetTask(46)!;
        var done = Doc.GetTask(53)!;
        Assert.Equal(open.Recurrence!.TotalOccurrences, done.Recurrence!.TotalOccurrences);
        Assert.Equal(open.Recurrence!.RemainingOccurrences, done.Recurrence!.RemainingOccurrences);
        Assert.False(open.IsDone);
        Assert.True(done.IsDone);
    }
}
