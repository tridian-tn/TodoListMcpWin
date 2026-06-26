namespace TodoListMcp.Core.Model;

/// <summary>
/// A task-ordering dependency (a ToDoList &lt;DEPENDS&gt; element): the owning task should follow
/// the task identified by <see cref="DependsOnId"/>, optionally offset by <see cref="LeadInDays"/>.
/// Only local (same-list) dependencies are modelled; cross-tasklist <c>tasklist?id</c> references
/// are preserved on disk but not surfaced.
/// </summary>
public sealed class TaskDependency
{
    /// <summary>The local ID of the task this one depends on (the &lt;DEPENDS&gt; element body).</summary>
    public required int DependsOnId { get; init; }

    /// <summary>
    /// Lead/lag offset in days (the DEPENDSLEADIN attribute); positive delays the start, negative
    /// brings it forward. Null when there is no offset — both an absent attribute and an explicit
    /// <c>0</c>, which ToDoList treats identically (it omits the attribute for a zero lead-in).
    /// </summary>
    public int? LeadInDays { get; init; }
}
