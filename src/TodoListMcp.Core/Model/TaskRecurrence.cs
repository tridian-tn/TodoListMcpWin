namespace TodoListMcp.Core.Model;

/// <summary>
/// A read projection of a ToDoList &lt;RECURRENCE&gt; element — the rule by which a task repeats.
/// Decoded from the on-disk integers (RECURFREQ + RECURSPECIFIC1/2 + the bookkeeping attributes),
/// verified against the abstractspoon/ToDoList_9.2 source; see <c>docs/recurrence-spike.md</c>.
///
/// <para>Read-only for now: this surfaces every frequency ToDoList can store, but the server does not
/// yet author recurrence, nor does completing a task here advance the series (that happens only inside
/// the ToDoList app).</para>
/// </summary>
public sealed class TaskRecurrence
{
    /// <summary>
    /// A stable machine name for the recurrence pattern — one of: <c>everyNDays</c>,
    /// <c>everyWeekday</c>, <c>everyNWeekdays</c>, <c>weeklyOnDays</c>, <c>everyNWeeks</c>,
    /// <c>monthlyOnDay</c>, <c>everyNMonths</c>, <c>monthlyByWeekday</c>,
    /// <c>monthlyOnFirstLastWeekday</c>, <c>yearlyOnDate</c>, <c>everyNYears</c>,
    /// <c>yearlyByWeekday</c>, or <c>unsupported</c> for a deprecated/unknown frequency.
    /// </summary>
    public string Pattern { get; init; } = "";

    /// <summary>A human-readable description of the rule, e.g. "Every 3 weekdays" or
    /// "Monthly on day 15, every 2 months".</summary>
    public string Description { get; init; } = "";

    /// <summary>The interval N for an "every N &lt;unit&gt;" pattern (days/weeks/months/years);
    /// null when the pattern has no interval.</summary>
    public int? Interval { get; init; }

    /// <summary>Weekday names (e.g. "Monday") for <c>weeklyOnDays</c>; null for other patterns.</summary>
    public IReadOnlyList<string>? DaysOfWeek { get; init; }

    /// <summary>Day of the month (1–31) for <c>monthlyOnDay</c> / <c>yearlyOnDate</c>; null otherwise.</summary>
    public int? DayOfMonth { get; init; }

    /// <summary>Month names (e.g. "January") for the yearly patterns; null otherwise.</summary>
    public IReadOnlyList<string>? Months { get; init; }

    /// <summary>Which occurrence in the month — "first"/"second"/"third"/"fourth"/"last" — for the
    /// by-weekday patterns (<c>monthlyByWeekday</c>, <c>yearlyByWeekday</c>); null otherwise.</summary>
    public string? WeekOfMonth { get; init; }

    /// <summary>The single weekday name for the by-weekday patterns; null otherwise.</summary>
    public string? Weekday { get; init; }

    /// <summary>
    /// For the interval-only monthly/yearly patterns (<c>everyNMonths</c>, <c>everyNYears</c>),
    /// whether ToDoList keeps the same weekday rather than the same day-of-month when advancing (the
    /// RECURSPECIFIC2 preserve-weekday flag). Null for patterns where that slot carries other data.
    /// </summary>
    public bool? PreserveWeekday { get; init; }

    /// <summary>What the next occurrence is calculated from: "dueDate" (default), "doneDate", or
    /// "startDate" (the RECURFROM attribute).</summary>
    public string RecalculateFrom { get; init; } = "dueDate";

    /// <summary>What happens when the task recurs: "reuse" the same task (default), "createNew", or
    /// "ask" (the RECURREUSE attribute).</summary>
    public string OnRecur { get; init; } = "reuse";

    /// <summary>Total number of occurrences the series was set to run for; null when unlimited.</summary>
    public int? TotalOccurrences { get; init; }

    /// <summary>Occurrences still remaining; null when unlimited.</summary>
    public int? RemainingOccurrences { get; init; }

    /// <summary>Whether the task's comments are carried across each recurrence (RECURPRESERVECOMMENTS).</summary>
    public bool PreserveComments { get; init; } = true;
}
