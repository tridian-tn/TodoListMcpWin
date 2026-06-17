using TodoListMcp.Core;

namespace TodoListMcp.Core.Tests;

public class UpdateTaskTests
{
    [Fact]
    public void Update_changes_title_priority_and_percent()
    {
        var doc = TestData.Sample();
        doc.UpdateTask(3, new() { Title = "Renamed", Priority = 9, PercentDone = 40 });

        var t = doc.GetTask(3)!;
        Assert.Equal("Renamed", t.Title);
        Assert.Equal(9, t.Priority);
        Assert.Equal(40, t.PercentDone);
    }

    [Fact]
    public void Update_comments_writes_child_element_and_drops_attribute_form()
    {
        var doc = TestData.Sample();
        doc.UpdateTask(3, new() { Comments = "Fresh notes" });

        Assert.Equal("Fresh notes", doc.GetTask(3)!.Comments);
        var xml = doc.ToXmlString();
        Assert.Contains("<COMMENTS>Fresh notes</COMMENTS>", xml);
        Assert.DoesNotContain("COMMENTS=\"Fresh notes\"", xml); // not the attribute form
    }

    [Fact]
    public void Update_with_null_fields_leaves_values_untouched()
    {
        var doc = TestData.Sample();
        doc.UpdateTask(2, new() { PercentDone = 50 });

        var t = doc.GetTask(2)!;
        Assert.Equal("Child A", t.Title);          // unchanged
        Assert.Equal(8, t.Priority);               // unchanged
        Assert.Equal(new[] { "Bob", "Jane" }, t.AllocatedTo); // unchanged
        Assert.Equal(50, t.PercentDone);           // changed
    }

    [Fact]
    public void ClearDueDate_removes_the_due_date()
    {
        var doc = TestData.Sample();
        Assert.NotNull(doc.GetTask(3)!.DueDate);

        doc.UpdateTask(3, new() { ClearDueDate = true });
        Assert.Null(doc.GetTask(3)!.DueDate);
    }

    [Fact]
    public void ClearPriority_removes_priority()
    {
        var doc = TestData.Sample();
        doc.UpdateTask(2, new() { ClearPriority = true });
        Assert.Null(doc.GetTask(2)!.Priority);
    }

    [Fact]
    public void Update_categories_replaces_and_empty_list_clears()
    {
        var doc = TestData.Sample();
        doc.UpdateTask(1, new() { Categories = new[] { "Travel" } });
        Assert.Equal(new[] { "Travel" }, doc.GetTask(1)!.Categories);

        doc.UpdateTask(1, new() { Categories = Array.Empty<string>() });
        Assert.Empty(doc.GetTask(1)!.Categories);
    }

    [Fact]
    public void Update_stamps_LastModBy()
    {
        var doc = TestData.Sample();
        doc.ModifiedBy = "UnitTest";
        doc.UpdateTask(3, new() { Title = "x" });

        Assert.Contains("LASTMODBY=\"UnitTest\"", doc.ToXmlString());
    }

    [Fact]
    public void Update_missing_task_throws()
    {
        var doc = TestData.Sample();
        Assert.Throws<TaskNotFoundException>(() => doc.UpdateTask(999, new() { Title = "x" }));
    }

    [Fact]
    public void Update_preserves_unknown_attributes()
    {
        var doc = TestData.Sample();
        doc.UpdateTask(1, new() { Title = "Parent Renamed" });

        // ICONINDEX/COLOR are attributes this library does not manage.
        var xml = doc.ToXmlString();
        Assert.Contains("ICONINDEX=\"10\"", xml);
        Assert.Contains("COLOR=\"255\"", xml);
    }
}
