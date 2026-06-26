namespace TodoListMcp.Core.Model;

/// <summary>
/// A read projection of a ToDoList &lt;TASK&gt; element. Mutations are performed against
/// the underlying <see cref="TodoListDocument"/>, not against instances of this type.
/// </summary>
public sealed class TodoTask
{
    public int Id { get; init; }
    public string Title { get; init; } = "";

    /// <summary>
    /// A caller-defined external reference (the EXTERNALID attribute), e.g. an issue key from
    /// another system. Null when unset. Not interpreted by ToDoList beyond storage/display.
    /// </summary>
    public string? ExternalId { get; init; }

    /// <summary>
    /// Notes text (the &lt;COMMENTS&gt; child element). For any non-plain format this is ToDoList's
    /// flattened plain-text mirror of the formatted content — retained for search/display; see
    /// <see cref="CommentsFormat"/>. For Markdown/HTML the editable source is in
    /// <see cref="CommentsSource"/>.
    /// </summary>
    public string? Comments { get; init; }

    /// <summary>
    /// The editable comment source decoded from &lt;CUSTOMCOMMENTS&gt;, populated only for the
    /// formats this server can re-author — Markdown and HTML — so a read → edit → write round-trip
    /// is lossless. Null for plain (use <see cref="Comments"/>), and for rich/spreadsheet/unknown
    /// formats whose payloads are opaque (RTF / a ReoGrid workbook) and can't be written back; for
    /// those, <see cref="Comments"/> remains the only available text.
    /// </summary>
    public string? CommentsSource { get; init; }

    /// <summary>
    /// The comment format (ToDoList's <c>COMMENTSTYPE</c>) as a friendly name: "plain", "rich",
    /// "html", "markdown" or "spreadsheet"; the raw id for an unrecognised content control; null
    /// when the task has no comments. <see cref="Comments"/> is always the plain-text mirror; for
    /// "markdown"/"html" the editable source is also surfaced in <see cref="CommentsSource"/>, while
    /// "rich"/"spreadsheet"/unknown formats expose only the (read-only) mirror.
    /// </summary>
    public string? CommentsFormat { get; init; }

    /// <summary>ToDoList priority on its native 0–10 scale; null when unset (-2 in the file).</summary>
    public int? Priority { get; init; }

    /// <summary>ToDoList risk on its native 0–10 scale; null when unset (-2 in the file).</summary>
    public int? Risk { get; init; }

    /// <summary>Free-text workflow status (the STATUS attribute), e.g. "In Progress"; null when unset.</summary>
    public string? Status { get; init; }

    /// <summary>Target version/release string (the VERSION attribute); null when unset.</summary>
    public string? Version { get; init; }

    /// <summary>True when the task carries the FLAG attribute (ToDoList's star/flag marker).</summary>
    public bool IsFlagged { get; init; }

    /// <summary>
    /// True when the task is locked in ToDoList (the LOCK attribute), marking it read-only. This
    /// server refuses to update, complete, reopen, move, or delete a locked task.
    /// </summary>
    public bool IsLocked { get; init; }

    public int PercentDone { get; init; }

    /// <summary>Estimated effort value, in <see cref="TimeEstimateUnit"/>; null when unset (or zero).</summary>
    public double? TimeEstimate { get; init; }

    /// <summary>Unit for <see cref="TimeEstimate"/> (e.g. "hours", "days"); null when no estimate.</summary>
    public string? TimeEstimateUnit { get; init; }

    /// <summary>Effort spent so far, in <see cref="TimeSpentUnit"/>; null when unset (or zero).</summary>
    public double? TimeSpent { get; init; }

    /// <summary>Unit for <see cref="TimeSpent"/> (e.g. "hours", "days"); null when nothing spent.</summary>
    public string? TimeSpentUnit { get; init; }

    /// <summary>True when the task carries a DONEDATE (explicitly completed).</summary>
    public bool IsDone { get; init; }

    /// <summary>
    /// ToDoList's calculated "treat as done" flag (the GOODASDONE attribute). True when the task
    /// is completed, or — if the user enabled "treat parents with all subtasks completed as done" —
    /// when it is such a parent. Derived/cached by ToDoList; <see cref="IsDone"/> is the source of truth.
    /// </summary>
    public bool IsGoodAsDone { get; init; }

    public DateTime? DueDate { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? DoneDate { get; init; }
    public DateTime? CreationDate { get; init; }
    public DateTime? LastModified { get; init; }

    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AllocatedTo { get; init; } = Array.Empty<string>();

    /// <summary>Who assigned the task (the single-value ALLOCATEDBY attribute); null when unset.</summary>
    public string? AllocatedBy { get; init; }

    /// <summary>
    /// File and URL links attached to the task (the &lt;FILEREFPATH&gt; child elements; the UI's
    /// "File Link" field). Each may be a local/network path, an http(s)/mailto URL, an Outlook
    /// folder, or a tdl://&lt;id&gt; task link. Surfaced and stored verbatim — relative paths are
    /// relative to the .tdl file's folder and are not normalised. Exact duplicates are collapsed
    /// case-insensitively (as ToDoList does); empty/whitespace-only entries are dropped.
    /// </summary>
    public IReadOnlyList<string> FileLinks { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Task-ordering dependencies (the &lt;DEPENDS&gt; child elements): tasks this one should follow,
    /// each with an optional lead-in/lag in days. Only local (same-list) dependencies are surfaced;
    /// cross-tasklist <c>tasklist?id</c> references are preserved on disk but not reported. Duplicate
    /// references (same dependee) are collapsed, keeping the first. References to a task no longer in
    /// the list are weeded out, mirroring ToDoList (the stale element stays on disk until a delete
    /// prunes it).
    /// </summary>
    public IReadOnlyList<TaskDependency> Dependencies { get; init; } = Array.Empty<TaskDependency>();

    /// <summary>1-based dotted hierarchy path, e.g. "2.1.3" (the POSSTRING attribute).</summary>
    public string Position { get; init; } = "";

    public IReadOnlyList<TodoTask> Subtasks { get; init; } = Array.Empty<TodoTask>();
}
