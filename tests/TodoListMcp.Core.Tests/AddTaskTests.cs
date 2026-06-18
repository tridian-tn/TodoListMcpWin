using TodoListMcp.Core;

namespace TodoListMcp.Core.Tests;

public class AddTaskTests
{
    [Fact]
    public void Add_top_level_uses_NextUniqueId_and_increments_it()
    {
        var doc = TestData.Sample();
        Assert.Equal(4, doc.NextUniqueId);

        var created = doc.AddTask(new() { Title = "New" });

        Assert.Equal(4, created.Id);
        Assert.Equal(5, doc.NextUniqueId);
        Assert.Equal("2", created.Position); // second top-level task
    }

    [Fact]
    public void Add_child_nests_under_parent_with_dotted_position()
    {
        var doc = TestData.Sample();
        var created = doc.AddTask(new() { Title = "Child C", ParentId = 1 });

        var parent = doc.GetTask(1)!;
        Assert.Equal(3, parent.Subtasks.Count);
        Assert.Equal("1.3", created.Position);
    }

    [Fact]
    public void Add_with_index_inserts_and_renumbers_siblings()
    {
        var doc = TestData.Sample();
        var created = doc.AddTask(new() { Title = "First child", ParentId = 1, Index = 0 });

        Assert.Equal("1.1", created.Position);
        // The previously-first child (ID 2) is now second.
        Assert.Equal("1.2", doc.GetTask(2)!.Position);
        Assert.Equal("1.3", doc.GetTask(3)!.Position);
    }

    [Fact]
    public void Add_persists_all_metadata()
    {
        var doc = TestData.Sample();
        var created = doc.AddTask(new()
        {
            Title = "Rich",
            Comments = "Some notes",
            Priority = 7,
            DueDate = new DateTime(2026, 12, 31),
            Categories = new[] { "Home", "Errands" },
            AllocatedTo = new[] { "Alice" },
        });

        var read = doc.GetTask(created.Id)!;
        Assert.Equal("Some notes", read.Comments);
        Assert.Equal(7, read.Priority);
        Assert.Equal(new DateTime(2026, 12, 31), read.DueDate);
        Assert.Equal(new[] { "Home", "Errands" }, read.Categories);
        Assert.Equal(new[] { "Alice" }, read.AllocatedTo);
    }

    [Fact]
    public void Add_persists_new_fields_and_writes_expected_attributes()
    {
        var doc = TestData.Sample();
        var created = doc.AddTask(new()
        {
            Title = "Rich",
            Risk = 6,
            PercentDone = 25,
            StartDate = new DateTime(2026, 1, 2),
            Status = "In Progress",
            Flag = true,
            ExternalId = "JIRA-42",
        });

        var read = doc.GetTask(created.Id)!;
        Assert.Equal(6, read.Risk);
        Assert.Equal(25, read.PercentDone);
        Assert.Equal(new DateTime(2026, 1, 2), read.StartDate);
        Assert.Equal("In Progress", read.Status);
        Assert.True(read.IsFlagged);
        Assert.Equal("JIRA-42", read.ExternalId);

        var xml = doc.ToXmlString();
        Assert.Contains("RISK=\"6\"", xml);
        Assert.Contains("STATUS=\"In Progress\"", xml);
        Assert.Contains("FLAG=\"1\"", xml);
        Assert.Contains("EXTERNALID=\"JIRA-42\"", xml);
        Assert.Contains("STARTDATE=\"", xml);
    }

    [Fact]
    public void Add_risk_is_clamped_to_scale()
    {
        var doc = TestData.Sample();
        var created = doc.AddTask(new() { Title = "Risky", Risk = 99 });
        Assert.Equal(10, doc.GetTask(created.Id)!.Risk);
    }

    [Fact]
    public void Add_under_missing_parent_throws()
    {
        var doc = TestData.Sample();
        Assert.Throws<TaskNotFoundException>(() => doc.AddTask(new() { Title = "X", ParentId = 999 }));
    }

    [Fact]
    public void Add_priority_is_clamped_to_scale()
    {
        var doc = TestData.Sample();
        var created = doc.AddTask(new() { Title = "Loud", Priority = 99 });
        Assert.Equal(10, doc.GetTask(created.Id)!.Priority);
    }

    [Fact]
    public void Single_assignee_written_as_attribute_multiple_as_elements()
    {
        var doc = TestData.Sample();
        var one = doc.AddTask(new() { Title = "One", AllocatedTo = new[] { "Solo" } });
        var many = doc.AddTask(new() { Title = "Many", AllocatedTo = new[] { "A", "B" } });

        var xml = doc.ToXmlString();
        Assert.Contains("ALLOCATEDTO=\"Solo\"", xml);
        Assert.Contains("<PERSON>A</PERSON><PERSON>B</PERSON>", xml);

        // Either representation reads back identically.
        Assert.Equal(new[] { "Solo" }, doc.GetTask(one.Id)!.AllocatedTo);
        Assert.Equal(new[] { "A", "B" }, doc.GetTask(many.Id)!.AllocatedTo);
    }
}
