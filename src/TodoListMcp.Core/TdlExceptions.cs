namespace TodoListMcp.Core;

/// <summary>Thrown when a .tdl file is not valid ToDoList XML.</summary>
public sealed class InvalidTdlException : Exception
{
    public InvalidTdlException(string message) : base(message) { }
    public InvalidTdlException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Thrown when an operation references a task ID that does not exist.</summary>
public sealed class TaskNotFoundException : Exception
{
    public TaskNotFoundException(int id) : base($"Task with ID {id} was not found.") => TaskId = id;
    public int TaskId { get; }
}

/// <summary>Thrown when an operation targets a task the user has locked in ToDoList (LOCK="1").</summary>
public sealed class TaskLockedException : Exception
{
    public TaskLockedException(int id)
        : base($"Task with ID {id} is locked (LOCK=\"1\") in ToDoList and is read-only. "
             + "Unlock it in the ToDoList app before modifying it here.")
        => TaskId = id;
    public int TaskId { get; }
}

/// <summary>
/// Thrown when a caller tries to overwrite a task's formatted (non-plain-text) comments without
/// opting in. Replacing them would discard the rich &lt;CUSTOMCOMMENTS&gt; payload ToDoList stores
/// for rich text/HTML/Markdown/spreadsheet notes.
/// </summary>
public sealed class FormattedCommentsException : Exception
{
    public FormattedCommentsException(int id, string format)
        : base($"Task with ID {id} has {format} comments. Overwriting them here would discard the "
             + "formatted content (ToDoList's <CUSTOMCOMMENTS> payload). Pass "
             + "replaceFormattedComments=true to replace them with plain text, or edit them in the "
             + "ToDoList app.")
    {
        TaskId = id;
        Format = format;
    }
    public int TaskId { get; }
    public string Format { get; }
}
