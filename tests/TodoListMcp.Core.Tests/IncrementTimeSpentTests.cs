using TodoListMcp.Core;
using TodoListMcp.Core.Model;

namespace TodoListMcp.Core.Tests;

/// <summary>
/// Covers <see cref="TodoListDocument.IncrementTimeSpent"/> — the delta-add used by logged time's
/// "add to time spent", which keeps the task's existing unit and clamps at zero.
/// </summary>
public class IncrementTimeSpentTests
{
    private static TodoListDocument Doc() => TodoListDocument.Parse(TestData.SampleXml, TestData.Clock);

    [Fact]
    public void Adds_hours_to_a_task_with_no_existing_time_spent()
    {
        var doc = Doc();
        var task = doc.IncrementTimeSpent(1, 2.0);
        Assert.Equal(2.0, task.TimeSpent);
        Assert.Equal("hours", task.TimeSpentUnit);
    }

    [Fact]
    public void Adds_to_existing_hours()
    {
        var doc = Doc();
        var id = doc.AddTask(new() { Title = "T", TimeSpent = 1.0, TimeSpentUnit = TimeUnit.Hours }).Id;
        var task = doc.IncrementTimeSpent(id, 0.5);
        Assert.Equal(1.5, task.TimeSpent);
        Assert.Equal("hours", task.TimeSpentUnit);
    }

    [Fact]
    public void Reconciles_into_the_tasks_existing_unit()
    {
        var doc = Doc();
        // 1 day spent; add 8 hours → 2 days (at the fixed 8h/day convention), unit preserved.
        var id = doc.AddTask(new() { Title = "T", TimeSpent = 1.0, TimeSpentUnit = TimeUnit.Days }).Id;
        var task = doc.IncrementTimeSpent(id, 8.0);
        Assert.Equal(2.0, task.TimeSpent);
        Assert.Equal("days", task.TimeSpentUnit);
    }

    [Fact]
    public void Clamps_at_zero_when_the_delta_is_negative()
    {
        var doc = Doc();
        var id = doc.AddTask(new() { Title = "T", TimeSpent = 1.0, TimeSpentUnit = TimeUnit.Hours }).Id;
        var task = doc.IncrementTimeSpent(id, -5.0);
        Assert.Null(task.TimeSpent); // 0 reads back as "no time"
    }

    [Fact]
    public void Zero_delta_is_a_no_op_and_leaves_the_document_clean()
    {
        var doc = Doc();
        doc.IncrementTimeSpent(1, 0);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void Throws_for_a_missing_task()
    {
        Assert.Throws<TaskNotFoundException>(() => Doc().IncrementTimeSpent(999, 1.0));
    }

    [Fact]
    public void Refuses_a_locked_task()
    {
        var doc = TodoListDocument.Parse(
            "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
            "<TODOLIST NEXTUNIQUEID=\"2\"><TASK ID=\"1\" TITLE=\"Locked\" LOCK=\"1\" POS=\"0\" POSSTRING=\"1\"/></TODOLIST>",
            TestData.Clock);
        Assert.Throws<TaskLockedException>(() => doc.IncrementTimeSpent(1, 1.0));
    }
}
