using TodoListMcp.Core;

namespace TodoListMcp.Core.Tests;

public class LifecycleTests
{
    [Fact]
    public void Complete_sets_done_date_full_progress_and_good_as_done()
    {
        var doc = TestData.Sample();
        var t = doc.CompleteTask(3);

        Assert.True(t.IsDone);
        Assert.True(t.IsGoodAsDone);
        Assert.Equal(100, t.PercentDone);
        Assert.NotNull(t.DoneDate);
        Assert.Equal(TestData.Clock.Now.Date, t.DoneDate!.Value.Date);
        Assert.Contains("GOODASDONE=\"1\"", doc.ToXmlString());
    }

    [Fact]
    public void Reopen_clears_done_state_and_good_as_done_flag()
    {
        var doc = TestData.Sample();
        doc.CompleteTask(2); // task 2 starts with GOODASDONE="1"
        var t = doc.ReopenTask(2);

        Assert.False(t.IsDone);
        Assert.False(t.IsGoodAsDone);
        Assert.Equal(0, t.PercentDone);
        Assert.Null(t.DoneDate);
        // The cached GOODASDONE flag is removed from the reopened task.
        Assert.Null(doc.GetTask(2)!.DoneDate);
        Assert.False(doc.GetTask(2)!.IsGoodAsDone);
    }

    [Fact]
    public void Delete_removes_task_and_renumbers()
    {
        var doc = TestData.Sample();
        Assert.True(doc.DeleteTask(2));

        Assert.Null(doc.GetTask(2));
        // Remaining child is renumbered to first position under the parent.
        Assert.Equal("1.1", doc.GetTask(3)!.Position);
    }

    [Fact]
    public void Delete_parent_removes_subtree()
    {
        var doc = TestData.Sample();
        Assert.True(doc.DeleteTask(1));

        Assert.Null(doc.GetTask(1));
        Assert.Null(doc.GetTask(2));
        Assert.Null(doc.GetTask(3));
        Assert.Empty(doc.GetTasks());
    }

    [Fact]
    public void Delete_missing_returns_false()
    {
        var doc = TestData.Sample();
        Assert.False(doc.DeleteTask(999));
    }
}
