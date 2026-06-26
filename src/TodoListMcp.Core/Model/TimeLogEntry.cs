namespace TodoListMcp.Core.Model;

/// <summary>
/// A read projection of one row in a ToDoList time-log sidecar (<c>&lt;listname&gt;_Log.csv</c>).
/// Each entry is an individual logged session or manual adjustment; it is independent of the
/// per-task TIMESPENT attribute (see <see cref="TodoTask.TimeSpent"/>).
/// </summary>
public sealed class TimeLogEntry
{
    /// <summary>The task this entry is against; <c>0</c> means a task-less entry (no task).</summary>
    public int TaskId { get; init; }

    /// <summary>The task's title snapshotted when the entry was logged; empty for a task-less entry.</summary>
    public string TaskTitle { get; init; } = "";

    /// <summary>Who logged the time (ToDoList's User ID column); null when blank.</summary>
    public string? Person { get; init; }

    /// <summary>Start of the logged period (local time).</summary>
    public DateTime From { get; init; }

    /// <summary>End of the logged period (local time).</summary>
    public DateTime To { get; init; }

    /// <summary>Hours logged (3-decimal precision, as ToDoList stores them).</summary>
    public double Hours { get; init; }

    /// <summary>Free-text comment; null when blank.</summary>
    public string? Comment { get; init; }

    /// <summary>
    /// Entry type: "Tracked" (a timer session) or "Adjusted" (a manual entry). May be blank on
    /// rows ToDoList wrote without one; this server writes "Adjusted" for manual log entries.
    /// </summary>
    public string? Type { get; init; }

    /// <summary>The task's display path snapshotted at log time; null when blank.</summary>
    public string? Path { get; init; }
}
