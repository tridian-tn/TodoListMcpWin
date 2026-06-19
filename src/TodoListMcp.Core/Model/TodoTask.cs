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

    /// <summary>Plain-text notes (the &lt;COMMENTS&gt; child element).</summary>
    public string? Comments { get; init; }

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

    /// <summary>1-based dotted hierarchy path, e.g. "2.1.3" (the POSSTRING attribute).</summary>
    public string Position { get; init; } = "";

    public IReadOnlyList<TodoTask> Subtasks { get; init; } = Array.Empty<TodoTask>();
}
