using TodoListMcp.Core;

namespace TodoListMcp.Core.Tests;

/// <summary>
/// ToDoList marks a task read-only with LOCK="1". The server surfaces that as IsLocked and refuses
/// to update, complete, reopen, move, or delete a locked task (including deleting it as part of a
/// parent's subtree), so an agent can't silently override a lock the user set deliberately.
/// </summary>
public class LockTests
{
    // Task 2 is locked; task 3 (its sibling) is open. Both are children of task 1.
    private const string Xml =
        "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
        "<TODOLIST PROJECTNAME=\"Lock Test\" NEXTUNIQUEID=\"4\">" +
        "<TASK ID=\"1\" TITLE=\"Parent\" POS=\"0\" POSSTRING=\"1\">" +
        "<TASK ID=\"2\" TITLE=\"Locked child\" LOCK=\"1\" POS=\"0\" POSSTRING=\"1.1\"/>" +
        "<TASK ID=\"3\" TITLE=\"Open child\" POS=\"1\" POSSTRING=\"1.2\"/>" +
        "</TASK></TODOLIST>";

    private static TodoListDocument Doc() => TodoListDocument.Parse(Xml, TestData.Clock);

    [Fact]
    public void Reads_lock_flag()
    {
        var doc = Doc();
        Assert.True(doc.GetTask(2)!.IsLocked);
        Assert.False(doc.GetTask(3)!.IsLocked);
        Assert.False(doc.GetTask(1)!.IsLocked);
    }

    [Fact]
    public void Update_locked_task_throws()
    {
        var ex = Assert.Throws<TaskLockedException>(() => Doc().UpdateTask(2, new() { Title = "x" }));
        Assert.Equal(2, ex.TaskId);
    }

    [Fact]
    public void Complete_locked_task_throws() =>
        Assert.Throws<TaskLockedException>(() => Doc().CompleteTask(2));

    [Fact]
    public void Reopen_locked_task_throws() =>
        Assert.Throws<TaskLockedException>(() => Doc().ReopenTask(2));

    [Fact]
    public void Move_locked_task_throws() =>
        Assert.Throws<TaskLockedException>(() => Doc().MoveTask(2, newParentId: null));

    [Fact]
    public void Delete_locked_task_throws()
    {
        var doc = Doc();
        Assert.Throws<TaskLockedException>(() => doc.DeleteTask(2));
        // The locked task is still there — the refusal happened before any removal.
        Assert.NotNull(doc.GetTask(2));
    }

    [Fact]
    public void Delete_parent_with_locked_descendant_throws_and_keeps_subtree()
    {
        var doc = Doc();
        var ex = Assert.Throws<TaskLockedException>(() => doc.DeleteTask(1));
        Assert.Equal(2, ex.TaskId); // names the locked descendant, not the parent
        Assert.NotNull(doc.GetTask(1));
        Assert.NotNull(doc.GetTask(2));
    }

    [Fact]
    public void Refused_mutation_leaves_document_clean()
    {
        var doc = Doc();
        Assert.Throws<TaskLockedException>(() => doc.UpdateTask(2, new() { Title = "x" }));
        // Nothing was written, so the manager would skip the save entirely.
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void Open_sibling_remains_mutable()
    {
        var doc = Doc();
        var updated = doc.UpdateTask(3, new() { Title = "Renamed" });
        Assert.Equal("Renamed", updated.Title);
        Assert.True(doc.DeleteTask(3));
    }
}
