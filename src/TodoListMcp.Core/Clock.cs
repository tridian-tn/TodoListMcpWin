namespace TodoListMcp.Core;

/// <summary>
/// Abstraction over "now" so that timestamp-writing operations can be made
/// deterministic in tests.
/// </summary>
public interface IClock
{
    /// <summary>Current local time. ToDoList stores OLE-automation dates in local time.</summary>
    DateTime Now { get; }
}

/// <summary>Default clock backed by the system local clock.</summary>
public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();
    public DateTime Now => DateTime.Now;
}

/// <summary>A fixed clock, useful for deterministic tests.</summary>
public sealed class FixedClock : IClock
{
    public FixedClock(DateTime now) => Now = now;
    public DateTime Now { get; }
}
