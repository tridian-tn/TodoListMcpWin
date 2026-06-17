using TodoListMcp.Core;

namespace TodoListMcp.Core.Tests;

public class ReadAndDateTests
{
    [Fact]
    public void GetTasks_returns_hierarchy()
    {
        var doc = TestData.Sample();
        var tasks = doc.GetTasks();

        Assert.Single(tasks);
        var parent = tasks[0];
        Assert.Equal(1, parent.Id);
        Assert.Equal("Parent", parent.Title);
        Assert.Equal("Parent notes", parent.Comments);
        Assert.Equal(new[] { "Work" }, parent.Categories);
        Assert.Equal(2, parent.Subtasks.Count);
        Assert.Equal("1", parent.Position);
        Assert.Equal("1.1", parent.Subtasks[0].Position);
    }

    [Fact]
    public void Priority_uses_native_0_to_10_scale()
    {
        var doc = TestData.Sample();
        var childA = doc.GetTask(2)!;
        var childB = doc.GetTask(3)!;

        // Fidelity: an 8 stays an 8, a 2 stays a 2 — no lossy bucketing.
        Assert.Equal(8, childA.Priority);
        Assert.Equal(2, childB.Priority);
    }

    [Fact]
    public void Comments_are_read_from_child_element_not_attribute()
    {
        var doc = TestData.Sample();
        Assert.Equal("Parent notes", doc.GetTask(1)!.Comments);
    }

    [Fact]
    public void Assignees_read_from_both_PERSON_elements_and_ALLOCATEDTO_attribute()
    {
        var doc = TestData.Sample();
        Assert.Equal(new[] { "Bob", "Jane" }, doc.GetTask(2)!.AllocatedTo);
        Assert.Equal(new[] { "Mary" }, doc.GetTask(3)!.AllocatedTo);
    }

    [Fact]
    public void Completion_detected_from_DONEDATE()
    {
        var doc = TestData.Sample();
        Assert.True(doc.GetTask(2)!.IsDone);
        Assert.False(doc.GetTask(1)!.IsDone);
        Assert.NotNull(doc.GetTask(2)!.DoneDate);
    }

    [Fact]
    public void GoodAsDone_read_from_GOODASDONE_attribute()
    {
        var doc = TestData.Sample();
        Assert.True(doc.GetTask(2)!.IsGoodAsDone);   // has GOODASDONE="1"
        Assert.False(doc.GetTask(1)!.IsGoodAsDone);  // no GOODASDONE
        Assert.False(doc.GetTask(3)!.IsGoodAsDone);
    }

    [Fact]
    public void Ole_serial_date_decodes_to_expected_calendar_date()
    {
        // ToDoList recorded DUEDATESTRING="16/9/2025" for serial 45916.79166667.
        var doc = TestData.Sample();
        var due = doc.GetTask(3)!.DueDate;

        Assert.NotNull(due);
        Assert.Equal(new DateTime(2025, 9, 16), due!.Value.Date);
        Assert.Equal(19, due.Value.Hour); // .79166667 of a day ≈ 19:00
    }

    [Fact]
    public void Due_date_round_trips_through_set_and_read()
    {
        var doc = TestData.Sample();
        var when = new DateTime(2027, 3, 5);
        doc.UpdateTask(3, new() { DueDate = when });

        Assert.Equal(when, doc.GetTask(3)!.DueDate);
    }
}
