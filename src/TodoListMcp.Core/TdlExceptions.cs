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

/// <summary>Thrown when a time-log selector matches no entry in the sidecar.</summary>
public sealed class TimeLogEntryNotFoundException : Exception
{
    public TimeLogEntryNotFoundException()
        : base("No time-log entry matched the given criteria. Read the log with get_time_log to "
             + "find the entry's exact fields, then select it by those.") { }
}

/// <summary>Thrown when a time-log selector matches more than one entry, so the target is ambiguous.</summary>
public sealed class AmbiguousTimeLogMatchException : Exception
{
    public AmbiguousTimeLogMatchException(int matchCount)
        : base($"{matchCount} time-log entries matched the given criteria; the target is ambiguous. "
             + "Add more criteria (e.g. from/to, person, comment, hours) so exactly one entry matches.")
        => MatchCount = matchCount;
    public int MatchCount { get; }
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
