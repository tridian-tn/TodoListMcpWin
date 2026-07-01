namespace TodoListMcp.Core.Model;

/// <summary>Parameters for creating a new task.</summary>
public sealed class AddTaskRequest
{
    public required string Title { get; init; }

    /// <summary>ID of the parent task. Null creates a top-level task.</summary>
    public int? ParentId { get; init; }

    /// <summary>Zero-based insertion index among siblings. Null appends to the end.</summary>
    public int? Index { get; init; }

    public string? Comments { get; init; }

    /// <summary>Format for <see cref="Comments"/> when authoring: plain (default), Markdown or HTML.</summary>
    public CommentContentFormat CommentsFormat { get; init; }

    /// <summary>Priority on the 0–10 scale (values are clamped).</summary>
    public int? Priority { get; init; }

    /// <summary>Risk on the 0–10 scale (values are clamped).</summary>
    public int? Risk { get; init; }

    /// <summary>Initial completion percentage, 0–100 (clamped). Omit to start at 0.</summary>
    public int? PercentDone { get; init; }

    /// <summary>Effort estimate value (in <see cref="TimeEstimateUnit"/>). Negative values clamp to 0.</summary>
    public double? TimeEstimate { get; init; }

    /// <summary>Unit for <see cref="TimeEstimate"/>; defaults to hours when an estimate is given.</summary>
    public TimeUnit? TimeEstimateUnit { get; init; }

    /// <summary>Effort already spent (in <see cref="TimeSpentUnit"/>). Negative values clamp to 0.</summary>
    public double? TimeSpent { get; init; }

    /// <summary>Unit for <see cref="TimeSpent"/>; defaults to hours when a value is given.</summary>
    public TimeUnit? TimeSpentUnit { get; init; }

    public DateTime? DueDate { get; init; }
    public DateTime? StartDate { get; init; }

    /// <summary>Free-text workflow status, e.g. "In Progress".</summary>
    public string? Status { get; init; }

    /// <summary>Target version/release string.</summary>
    public string? Version { get; init; }

    /// <summary>When true, sets the FLAG marker on the new task.</summary>
    public bool Flag { get; init; }

    /// <summary>A caller-defined external reference (e.g. an issue key).</summary>
    public string? ExternalId { get; init; }

    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AllocatedTo { get; init; } = Array.Empty<string>();

    /// <summary>Who assigned the task (single value).</summary>
    public string? AllocatedBy { get; init; }

    /// <summary>
    /// File/URL links to attach (the FILEREFPATH field). Stored verbatim (no trimming or path
    /// normalisation); exact duplicates are collapsed case-insensitively, matching ToDoList.
    /// </summary>
    public IReadOnlyList<string> FileLinks { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Parameters for updating a task. A null field means "leave unchanged".
/// The explicit Clear* flags distinguish "clear the value" from "leave unchanged".
/// </summary>
public sealed class UpdateTaskRequest
{
    public string? Title { get; init; }

    /// <summary>Null = unchanged; a value (including empty string) replaces the notes.</summary>
    public string? Comments { get; init; }

    /// <summary>
    /// Format for <see cref="Comments"/> when it is supplied: plain (default), Markdown or HTML.
    /// Ignored when <see cref="Comments"/> is null.
    /// </summary>
    public CommentContentFormat CommentsFormat { get; init; }

    /// <summary>
    /// When false (default), setting <see cref="Comments"/> on a task whose existing notes are
    /// formatted (rich text/HTML/Markdown/spreadsheet) is refused, to avoid discarding ToDoList's
    /// rich <c>CUSTOMCOMMENTS</c> payload. Set true to replace them anyway (in whatever
    /// <see cref="CommentsFormat"/> is given).
    /// </summary>
    public bool ReplaceFormattedComments { get; init; }

    /// <summary>Null = unchanged; a value sets priority (0–10, clamped).</summary>
    public int? Priority { get; init; }

    /// <summary>When true, removes the priority regardless of <see cref="Priority"/>.</summary>
    public bool ClearPriority { get; init; }

    /// <summary>Null = unchanged; a value sets risk (0–10, clamped).</summary>
    public int? Risk { get; init; }

    /// <summary>When true, removes the risk regardless of <see cref="Risk"/>.</summary>
    public bool ClearRisk { get; init; }

    public int? PercentDone { get; init; }

    /// <summary>Null = unchanged; a value sets the estimate (negative clamps to 0).</summary>
    public double? TimeEstimate { get; init; }

    /// <summary>
    /// Null = keep the current unit (or default to hours for a new estimate); a value re-labels
    /// the unit. Supplying only this (no <see cref="TimeEstimate"/>) re-labels an existing estimate.
    /// </summary>
    public TimeUnit? TimeEstimateUnit { get; init; }

    /// <summary>When true, removes the time estimate (value and unit) regardless of the above.</summary>
    public bool ClearTimeEstimate { get; init; }

    /// <summary>Null = unchanged; a value sets time spent (negative clamps to 0).</summary>
    public double? TimeSpent { get; init; }

    /// <summary>
    /// Null = keep the current unit (or default to hours for new time spent); a value re-labels
    /// the unit. Supplying only this (no <see cref="TimeSpent"/>) re-labels existing time spent.
    /// </summary>
    public TimeUnit? TimeSpentUnit { get; init; }

    /// <summary>When true, removes time spent (value and unit) regardless of the above.</summary>
    public bool ClearTimeSpent { get; init; }

    public DateTime? DueDate { get; init; }

    /// <summary>When true, removes the due date regardless of <see cref="DueDate"/>.</summary>
    public bool ClearDueDate { get; init; }

    public DateTime? StartDate { get; init; }

    /// <summary>When true, removes the start date regardless of <see cref="StartDate"/>.</summary>
    public bool ClearStartDate { get; init; }

    /// <summary>Null = unchanged; a value (including empty string) replaces the status.</summary>
    public string? Status { get; init; }

    /// <summary>Null = unchanged; a value (including empty string) replaces the version.</summary>
    public string? Version { get; init; }

    /// <summary>Null = unchanged; true/false sets or removes the FLAG marker.</summary>
    public bool? Flag { get; init; }

    /// <summary>Null = unchanged; a value (including empty string) replaces the external ID.</summary>
    public string? ExternalId { get; init; }

    /// <summary>Null = unchanged; a list (including empty) replaces the categories.</summary>
    public IReadOnlyList<string>? Categories { get; init; }

    /// <summary>Null = unchanged; a list (including empty) replaces the assignees.</summary>
    public IReadOnlyList<string>? AllocatedTo { get; init; }

    /// <summary>Null = unchanged; a value (including empty string) replaces who allocated the task.</summary>
    public string? AllocatedBy { get; init; }

    /// <summary>
    /// Null = unchanged; a list (including empty) replaces the file/URL links. Stored verbatim;
    /// exact duplicates are collapsed case-insensitively, matching ToDoList.
    /// </summary>
    public IReadOnlyList<string>? FileLinks { get; init; }
}

/// <summary>The recurrence patterns this server can author (the common, unambiguous subset). The
/// Kth-weekday and first/last-weekday patterns are read-only for now.</summary>
public enum RecurrencePattern
{
    /// <summary>Every N days (N = <see cref="SetRecurrenceRequest.Interval"/>).</summary>
    EveryNDays,
    /// <summary>Every weekday (Monday–Friday).</summary>
    EveryWeekday,
    /// <summary>Every N weekdays.</summary>
    EveryNWeekdays,
    /// <summary>On the given <see cref="SetRecurrenceRequest.DaysOfWeek"/>, every N weeks.</summary>
    WeeklyOnDays,
    /// <summary>Every N weeks (no specific days).</summary>
    EveryNWeeks,
    /// <summary>On a day of the month, every N months.</summary>
    MonthlyOnDay,
    /// <summary>Every N months.</summary>
    EveryNMonths,
    /// <summary>On a day of the given <see cref="SetRecurrenceRequest.Months"/>, yearly.</summary>
    YearlyOnDate,
    /// <summary>Every N years.</summary>
    EveryNYears,
}

/// <summary>What the next occurrence is calculated from (ToDoList's RECURFROM).</summary>
public enum RecurrenceRecalcFrom { DueDate, DoneDate, StartDate }

/// <summary>What happens when the task recurs (ToDoList's RECURREUSE).</summary>
public enum RecurrenceReuse { Reuse, CreateNew, Ask }

/// <summary>
/// Parameters for setting (or replacing) a task's recurrence rule. Only the fields the chosen
/// <see cref="Pattern"/> needs are read; the rest are ignored. Validated against ToDoList's own rules
/// (<c>CRecurrence::IsValidRegularity</c>) before writing, so an invalid rule is rejected rather than
/// silently dropped by the app.
/// </summary>
public sealed class SetRecurrenceRequest
{
    public required RecurrencePattern Pattern { get; init; }

    /// <summary>
    /// The interval N for the "every N …" patterns (days/weekdays/weeks/months/years), where it is
    /// required and must be ≥ 1. For <see cref="RecurrencePattern.WeeklyOnDays"/> and
    /// <see cref="RecurrencePattern.MonthlyOnDay"/> it is the (optional) weeks/months between
    /// occurrences and defaults to 1; a supplied value must still be ≥ 1.
    /// </summary>
    public int? Interval { get; init; }

    /// <summary>Weekday names (e.g. "Monday", or "Mon") for <see cref="RecurrencePattern.WeeklyOnDays"/>.</summary>
    public IReadOnlyList<string>? DaysOfWeek { get; init; }

    /// <summary>Day of the month (1–31) for <see cref="RecurrencePattern.MonthlyOnDay"/> / <see cref="RecurrencePattern.YearlyOnDate"/>.</summary>
    public int? DayOfMonth { get; init; }

    /// <summary>Month names (e.g. "March", or "Mar") for <see cref="RecurrencePattern.YearlyOnDate"/>.</summary>
    public IReadOnlyList<string>? Months { get; init; }

    /// <summary>What the next occurrence is calculated from. Defaults to the due date.</summary>
    public RecurrenceRecalcFrom RecalcFrom { get; init; } = RecurrenceRecalcFrom.DueDate;

    /// <summary>What happens when the task recurs. Defaults to reusing the same task.</summary>
    public RecurrenceReuse OnRecur { get; init; } = RecurrenceReuse.Reuse;

    /// <summary>Total number of occurrences the series should run for (≥ 1); null means unlimited.</summary>
    public int? Occurrences { get; init; }

    /// <summary>Whether the task's comments carry across each recurrence. Defaults to true.</summary>
    public bool PreserveComments { get; init; } = true;
}

/// <summary>
/// Parameters for appending one entry to the time-log sidecar. Mirrors ToDoList's "Add Logged
/// Time" action: an entry is valid when it has a comment, or a non-zero period (hours with
/// <see cref="From"/> ≤ <see cref="To"/>). A task-less, comment-only entry is allowed.
/// </summary>
public sealed class LogTimeRequest
{
    /// <summary>The task to log against; omit or 0 for a task-less entry.</summary>
    public int TaskId { get; init; }

    /// <summary>Hours logged. Negative values clamp to 0.</summary>
    public double Hours { get; init; }

    /// <summary>
    /// End of the logged period (local). When <see cref="From"/> is not given, the period is
    /// <c>[When − Hours, When]</c>, matching ToDoList's dialog. Defaults to now when omitted.
    /// </summary>
    public DateTime? When { get; init; }

    /// <summary>Explicit start of the period (local); overrides the <see cref="When"/>-derived start.</summary>
    public DateTime? From { get; init; }

    /// <summary>Explicit end of the period (local); overrides <see cref="When"/>.</summary>
    public DateTime? To { get; init; }

    /// <summary>Free-text comment. Required when there is no timed period.</summary>
    public string? Comment { get; init; }

    /// <summary>Who logged the time. Defaults to the current OS user when omitted.</summary>
    public string? Person { get; init; }

    /// <summary>Entry type; defaults to "Adjusted" (a manual entry) when omitted.</summary>
    public string? Type { get; init; }

    /// <summary>
    /// When a task is given, also increment that task's TIMESPENT by <see cref="Hours"/> (keeping
    /// the task's existing unit), mirroring the dialog's "Add to time spent" checkbox.
    /// </summary>
    public bool AddToTimeSpent { get; init; }
}

/// <summary>Filters for reading time-log entries. All criteria are combined with AND.</summary>
public sealed class TimeLogQuery
{
    /// <summary>Only entries for this task ID. Use 0 to select task-less entries.</summary>
    public int? TaskId { get; init; }

    /// <summary>Only entries whose period ends on or after this instant (local).</summary>
    public DateTime? Since { get; init; }

    /// <summary>Only entries whose period starts on or before this instant (local).</summary>
    public DateTime? Until { get; init; }

    /// <summary>Only entries logged by this person (case-insensitive, exact).</summary>
    public string? Person { get; init; }
}

/// <summary>
/// Identifies a single existing time-log entry to edit or delete. The format has no stable row ID,
/// so an entry is addressed by a match on its salient fields; every supplied criterion is
/// AND-combined and the match must resolve to <em>exactly one</em> entry. Zero matches or an
/// ambiguous match (more than one) is an error, so an edit/delete never silently touches the wrong
/// row. Timestamps match at minute precision (the sidecar's resolution). At least one criterion is
/// required.
/// </summary>
public sealed class TimeLogSelector
{
    /// <summary>Match the task ID. Use 0 to select task-less entries.</summary>
    public int? TaskId { get; init; }

    /// <summary>Match the period start (compared to the minute).</summary>
    public DateTime? From { get; init; }

    /// <summary>Match the period end (compared to the minute).</summary>
    public DateTime? To { get; init; }

    /// <summary>Match the person (case-insensitive, exact).</summary>
    public string? Person { get; init; }

    /// <summary>Match the comment (case-sensitive, exact). Use "" to match a blank comment.</summary>
    public string? Comment { get; init; }

    /// <summary>Match the logged hours (to 3 decimals, the stored precision).</summary>
    public double? Hours { get; init; }

    /// <summary>True when at least one criterion is set.</summary>
    public bool HasAnyCriterion =>
        TaskId is not null || From is not null || To is not null
        || Person is not null || Comment is not null || Hours is not null;
}

/// <summary>
/// The new field values for an edited time-log entry. Any property left null keeps the entry's
/// existing value; set a property to change it. The task ID, title and path are not editable here —
/// changing the task would mean re-snapshotting its title/path. The edited row is re-serialised in
/// the latest layout.
/// </summary>
public sealed class TimeLogEdit
{
    /// <summary>New period start; null keeps the current one.</summary>
    public DateTime? From { get; init; }

    /// <summary>New period end; null keeps the current one.</summary>
    public DateTime? To { get; init; }

    /// <summary>New logged hours; null keeps the current value.</summary>
    public double? Hours { get; init; }

    /// <summary>New comment; null keeps the current one, "" clears it.</summary>
    public string? Comment { get; init; }

    /// <summary>New person; null keeps the current one, "" clears it.</summary>
    public string? Person { get; init; }

    /// <summary>New entry type ("Adjusted"/"Tracked"); null keeps the current one, "" clears it.</summary>
    public string? Type { get; init; }

    /// <summary>True when at least one field is set.</summary>
    public bool HasAnyChange =>
        From is not null || To is not null || Hours is not null
        || Comment is not null || Person is not null || Type is not null;
}

/// <summary>Filters for searching tasks. All criteria are combined with AND.</summary>
public sealed class TaskQuery
{
    /// <summary>Case-insensitive substring matched against title and comments.</summary>
    public string? Text { get; init; }

    public string? Category { get; init; }

    public string? Person { get; init; }

    /// <summary>Null = any; true = only done; false = only not-done.</summary>
    public bool? Completed { get; init; }

    /// <summary>Null = any; true = only flagged; false = only un-flagged.</summary>
    public bool? Flagged { get; init; }

    /// <summary>Minimum priority (inclusive) on the 0–10 scale.</summary>
    public int? MinPriority { get; init; }

    /// <summary>Minimum risk (inclusive) on the 0–10 scale.</summary>
    public int? MinRisk { get; init; }

    /// <summary>Exact (case-insensitive) workflow status to match.</summary>
    public string? Status { get; init; }

    /// <summary>Exact (case-insensitive) external ID to match.</summary>
    public string? ExternalId { get; init; }

    /// <summary>Exact (case-insensitive) version/release string to match.</summary>
    public string? Version { get; init; }

    /// <summary>Exact (case-insensitive) allocated-by person to match.</summary>
    public string? AllocatedBy { get; init; }

    /// <summary>Minimum time estimate (inclusive), in hours; mixed units are normalised at 8h/day.</summary>
    public double? MinEstimateHours { get; init; }

    /// <summary>Maximum time estimate (inclusive), in hours; mixed units are normalised at 8h/day.</summary>
    public double? MaxEstimateHours { get; init; }

    /// <summary>Minimum time spent (inclusive), in hours; mixed units are normalised at 8h/day.</summary>
    public double? MinSpentHours { get; init; }

    /// <summary>Maximum time spent (inclusive), in hours; mixed units are normalised at 8h/day.</summary>
    public double? MaxSpentHours { get; init; }
}
