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
