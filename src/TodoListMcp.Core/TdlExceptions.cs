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
