using TodoListMcp.Core;

namespace TodoListMcp.Core.Tests;

public class MoveTaskTests
{
    [Fact]
    public void Move_child_to_top_level()
    {
        var doc = TestData.Sample();
        var moved = doc.MoveTask(2, newParentId: null);

        Assert.Equal("2", moved.Position); // now a second top-level task
        Assert.Equal(2, doc.GetTasks().Count);
        // The former sibling is renumbered under the old parent.
        Assert.Equal("1.1", doc.GetTask(3)!.Position);
    }

    [Fact]
    public void Move_into_descendant_throws()
    {
        var doc = TestData.Sample();
        // Task 1 is an ancestor of task 2 — moving 1 under 2 would create a cycle.
        Assert.Throws<InvalidOperationException>(() => doc.MoveTask(1, newParentId: 2));
    }

    [Fact]
    public void Move_into_self_throws()
    {
        var doc = TestData.Sample();
        Assert.Throws<InvalidOperationException>(() => doc.MoveTask(1, newParentId: 1));
    }

    [Fact]
    public void Move_with_index_controls_ordering()
    {
        var doc = TestData.Sample();
        // Make task 3 the first child of the parent.
        var moved = doc.MoveTask(3, newParentId: 1, index: 0);

        Assert.Equal("1.1", moved.Position);
        Assert.Equal("1.2", doc.GetTask(2)!.Position);
    }

    [Fact]
    public void Move_missing_task_throws()
    {
        var doc = TestData.Sample();
        Assert.Throws<TaskNotFoundException>(() => doc.MoveTask(999, newParentId: null));
    }
}
