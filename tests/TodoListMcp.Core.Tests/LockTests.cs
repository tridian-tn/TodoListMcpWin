using TodoListMcp.Core;

namespace TodoListMcp.Core.Tests;

/// <summary>
/// ToDoList marks a task read-only with LOCK="1". The server surfaces that as IsLocked and mirrors
/// ToDoList 8.1's enforcement: attribute edits (incl. complete/reopen) are refused on a locked task;
/// move and delete are refused on a locked task or one whose <em>immediate parent</em> is locked,
/// and move is also refused into a locked destination parent. A locked <em>descendant</em> does not
/// block deleting/moving an ancestor, and editing an unlocked child of a locked parent is allowed.
/// </summary>
public class LockTests
{
    // Task 2 is locked (under open parent 1); task 4 is a locked parent of open child 5.
    private const string Xml =
        "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
        "<TODOLIST PROJECTNAME=\"Lock Test\" NEXTUNIQUEID=\"6\">" +
        "<TASK ID=\"1\" TITLE=\"Open parent\" POS=\"0\" POSSTRING=\"1\">" +
        "<TASK ID=\"2\" TITLE=\"Locked child\" LOCK=\"1\" POS=\"0\" POSSTRING=\"1.1\"/>" +
        "<TASK ID=\"3\" TITLE=\"Open child\" POS=\"1\" POSSTRING=\"1.2\"/>" +
        "</TASK>" +
        "<TASK ID=\"4\" TITLE=\"Locked parent\" LOCK=\"1\" POS=\"1\" POSSTRING=\"2\">" +
        "<TASK ID=\"5\" TITLE=\"Open grandchild\" POS=\"0\" POSSTRING=\"2.1\"/>" +
        "</TASK></TODOLIST>";

    private static TodoListDocument Doc() => TodoListDocument.Parse(Xml, TestData.Clock);

    [Fact]
    public void Reads_lock_flag()
    {
        var doc = Doc();
        Assert.True(doc.GetTask(2)!.IsLocked);
        Assert.True(doc.GetTask(4)!.IsLocked);
        Assert.False(doc.GetTask(1)!.IsLocked);
        Assert.False(doc.GetTask(3)!.IsLocked);
        Assert.False(doc.GetTask(5)!.IsLocked);
    }

    // ---- Attribute edits: the task's own lock only -------------------------

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
    public void Update_unlocked_child_of_locked_parent_is_allowed()
    {
        // ToDoList gates attribute edits on the task's own lock, not its parent's.
        var updated = Doc().UpdateTask(5, new() { Title = "Renamed" });
        Assert.Equal("Renamed", updated.Title);
    }

    // ---- Move: self, source parent, and destination parent -----------------

    [Fact]
    public void Move_locked_task_throws()
    {
        var ex = Assert.Throws<TaskLockedException>(() => Doc().MoveTask(2, newParentId: null));
        Assert.Equal(2, ex.TaskId);
    }

    [Fact]
    public void Move_out_of_locked_parent_throws()
    {
        var ex = Assert.Throws<TaskLockedException>(() => Doc().MoveTask(5, newParentId: null));
        Assert.Equal(4, ex.TaskId); // the locked source parent
    }

    [Fact]
    public void Move_into_locked_parent_throws()
    {
        var ex = Assert.Throws<TaskLockedException>(() => Doc().MoveTask(3, newParentId: 4));
        Assert.Equal(4, ex.TaskId); // the locked destination parent
    }

    // ---- Delete: self and immediate parent, but not descendants ------------

    [Fact]
    public void Delete_locked_task_throws()
    {
        var doc = Doc();
        Assert.Throws<TaskLockedException>(() => doc.DeleteTask(2));
        Assert.NotNull(doc.GetTask(2)); // refused before any removal
    }

    [Fact]
    public void Delete_child_of_locked_parent_throws()
    {
        var doc = Doc();
        var ex = Assert.Throws<TaskLockedException>(() => doc.DeleteTask(5));
        Assert.Equal(4, ex.TaskId);
        Assert.NotNull(doc.GetTask(5));
    }

    [Fact]
    public void Delete_parent_with_locked_descendant_is_allowed()
    {
        // ToDoList does NOT check descendants on delete (bCheckChildren=FALSE): deleting an open
        // parent removes its whole subtree, locked children included.
        var doc = Doc();
        Assert.True(doc.DeleteTask(1));
        Assert.Null(doc.GetTask(1));
        Assert.Null(doc.GetTask(2));
        Assert.Null(doc.GetTask(3));
    }

    // ---- Cross-cutting -----------------------------------------------------

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
