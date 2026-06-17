namespace TodoListMcp.Core.Model;

/// <summary>
/// A read projection of a ToDoList &lt;TASK&gt; element. Mutations are performed against
/// the underlying <see cref="TodoListDocument"/>, not against instances of this type.
/// </summary>
public sealed class TodoTask
{
    public int Id { get; init; }
    public string Title { get; init; } = "";

    /// <summary>Plain-text notes (the &lt;COMMENTS&gt; child element).</summary>
    public string? Comments { get; init; }

    /// <summary>ToDoList priority on its native 0–10 scale; null when unset (-2 in the file).</summary>
    public int? Priority { get; init; }

    public int PercentDone { get; init; }

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

    /// <summary>1-based dotted hierarchy path, e.g. "2.1.3" (the POSSTRING attribute).</summary>
    public string Position { get; init; } = "";

    public IReadOnlyList<TodoTask> Subtasks { get; init; } = Array.Empty<TodoTask>();
}
