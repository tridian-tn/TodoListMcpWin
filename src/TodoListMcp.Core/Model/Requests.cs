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
    /// When false (default), setting <see cref="Comments"/> on a task whose existing notes are
    /// formatted (rich text/HTML/Markdown/spreadsheet) is refused, to avoid discarding ToDoList's
    /// rich <c>CUSTOMCOMMENTS</c> payload. Set true to replace them with plain text anyway.
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
