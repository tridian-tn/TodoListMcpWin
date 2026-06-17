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

    public DateTime? DueDate { get; init; }

    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AllocatedTo { get; init; } = Array.Empty<string>();
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

    /// <summary>Null = unchanged; a value sets priority (0–10, clamped).</summary>
    public int? Priority { get; init; }

    /// <summary>When true, removes the priority regardless of <see cref="Priority"/>.</summary>
    public bool ClearPriority { get; init; }

    public int? PercentDone { get; init; }

    public DateTime? DueDate { get; init; }

    /// <summary>When true, removes the due date regardless of <see cref="DueDate"/>.</summary>
    public bool ClearDueDate { get; init; }

    /// <summary>Null = unchanged; a list (including empty) replaces the categories.</summary>
    public IReadOnlyList<string>? Categories { get; init; }

    /// <summary>Null = unchanged; a list (including empty) replaces the assignees.</summary>
    public IReadOnlyList<string>? AllocatedTo { get; init; }
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

    /// <summary>Minimum priority (inclusive) on the 0–10 scale.</summary>
    public int? MinPriority { get; init; }
}
