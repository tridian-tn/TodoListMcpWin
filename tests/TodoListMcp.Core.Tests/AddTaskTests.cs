using TodoListMcp.Core;
using TodoListMcp.Core.Model;

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
            Version = "2.0",
            Flag = true,
            ExternalId = "JIRA-42",
            AllocatedBy = "Alice",
        });

        var read = doc.GetTask(created.Id)!;
        Assert.Equal(6, read.Risk);
        Assert.Equal(25, read.PercentDone);
        Assert.Equal(new DateTime(2026, 1, 2), read.StartDate);
        Assert.Equal("In Progress", read.Status);
        Assert.Equal("2.0", read.Version);
        Assert.True(read.IsFlagged);
        Assert.Equal("JIRA-42", read.ExternalId);
        Assert.Equal("Alice", read.AllocatedBy);

        var xml = doc.ToXmlString();
        Assert.Contains("RISK=\"6\"", xml);
        Assert.Contains("STATUS=\"In Progress\"", xml);
        Assert.Contains("VERSION=\"2.0\"", xml);
        Assert.Contains("FLAG=\"1\"", xml);
        Assert.Contains("EXTERNALID=\"JIRA-42\"", xml);
        Assert.Contains("ALLOCATEDBY=\"Alice\"", xml);
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
    public void Add_persists_time_estimate_and_spent_with_units()
    {
        var doc = TestData.Sample();
        var created = doc.AddTask(new()
        {
            Title = "Effort",
            TimeEstimate = 3,
            TimeEstimateUnit = TimeUnit.Days,
            TimeSpent = 90,
            TimeSpentUnit = TimeUnit.Minutes,
        });

        var read = doc.GetTask(created.Id)!;
        Assert.Equal(3, read.TimeEstimate);
        Assert.Equal("days", read.TimeEstimateUnit);
        Assert.Equal(90, read.TimeSpent);
        Assert.Equal("minutes", read.TimeSpentUnit);

        var xml = doc.ToXmlString();
        Assert.Contains("TIMEESTIMATE=\"3.00000000\"", xml);
        Assert.Contains("TIMEESTUNITS=\"D\"", xml);
        Assert.Contains("TIMESPENT=\"90.00000000\"", xml);
        Assert.Contains("TIMESPENTUNITS=\"I\"", xml);  // minutes is I, not M
    }

    [Fact]
    public void Add_time_unit_defaults_to_hours()
    {
        var doc = TestData.Sample();
        var created = doc.AddTask(new() { Title = "Quick", TimeEstimate = 4 });

        var read = doc.GetTask(created.Id)!;
        Assert.Equal(4, read.TimeEstimate);
        Assert.Equal("hours", read.TimeEstimateUnit);
        Assert.Contains("TIMEESTUNITS=\"H\"", doc.ToXmlString());
    }

    [Fact]
    public void Add_negative_time_clamps_to_zero_and_reads_as_unset()
    {
        var doc = TestData.Sample();
        var created = doc.AddTask(new() { Title = "Bad", TimeEstimate = -5, TimeEstimateUnit = TimeUnit.Hours });

        // Zero time is "no estimate" in ToDoList, so it reads back as null.
        Assert.Null(doc.GetTask(created.Id)!.TimeEstimate);
        Assert.Null(doc.GetTask(created.Id)!.TimeEstimateUnit);
        Assert.Contains("TIMEESTIMATE=\"0.00000000\"", doc.ToXmlString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Add_empty_or_whitespace_title_throws(string title)
    {
        var doc = TestData.Sample();
        Assert.Throws<ArgumentException>(() => doc.AddTask(new() { Title = title }));
    }

    [Fact]
    public void Add_falls_back_to_max_id_plus_one_when_NextUniqueId_missing()
    {
        // A file without NEXTUNIQUEID (e.g. hand-edited): the next ID is derived from the highest in use.
        var doc = TodoListDocument.Parse(
            """
            <?xml version="1.0" encoding="utf-16"?>
            <TODOLIST PROJECTNAME="NoCounter"><TASK ID="1" TITLE="A" POS="0" POSSTRING="1"/><TASK ID="5" TITLE="B" POS="1" POSSTRING="2"/></TODOLIST>
            """,
            TestData.Clock);

        var created = doc.AddTask(new() { Title = "C" });

        Assert.Equal(6, created.Id);            // max(1, 5) + 1
        Assert.Equal(7, doc.NextUniqueId);      // counter is now seeded and advanced
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
