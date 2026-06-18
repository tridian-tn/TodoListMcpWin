using TodoListMcp.Core;
using TodoListMcp.Core.Model;

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
    public void Update_sets_status_risk_flag_and_external_id()
    {
        var doc = TestData.Sample();
        doc.UpdateTask(3, new()
        {
            Status = "Blocked",
            Risk = 4,
            Flag = true,
            ExternalId = "GH-7",
        });

        var t = doc.GetTask(3)!;
        Assert.Equal("Blocked", t.Status);
        Assert.Equal(4, t.Risk);
        Assert.True(t.IsFlagged);
        Assert.Equal("GH-7", t.ExternalId);
    }

    [Fact]
    public void Update_clears_status_risk_flag_and_external_id()
    {
        var doc = TestData.Sample();
        doc.UpdateTask(3, new() { Status = "Done", Risk = 4, Flag = true, ExternalId = "GH-7" });

        // Empty string clears free-text fields; flag=false and ClearRisk remove their attributes.
        doc.UpdateTask(3, new() { Status = "", ExternalId = "", Flag = false, ClearRisk = true });

        var t = doc.GetTask(3)!;
        Assert.Null(t.Status);
        Assert.Null(t.Risk);
        Assert.False(t.IsFlagged);
        Assert.Null(t.ExternalId);
    }

    [Fact]
    public void Update_trims_status_and_external_id_and_clears_on_whitespace()
    {
        var doc = TestData.Sample();
        doc.UpdateTask(3, new() { Status = "  In Progress  ", ExternalId = "  GH-7  " });

        var t = doc.GetTask(3)!;
        Assert.Equal("In Progress", t.Status);   // stored trimmed
        Assert.Equal("GH-7", t.ExternalId);

        // Whitespace-only clears, and nothing whitespace-y is left in the file.
        doc.UpdateTask(3, new() { Status = "   ", ExternalId = "   " });
        var cleared = doc.GetTask(3)!;
        Assert.Null(cleared.Status);
        Assert.Null(cleared.ExternalId);

        var xml = doc.ToXmlString();
        Assert.DoesNotContain("STATUS=", xml);
        Assert.DoesNotContain("EXTERNALID=", xml);
    }

    [Fact]
    public void Update_sets_trims_and_clears_version_and_allocated_by()
    {
        var doc = TestData.Sample();
        doc.UpdateTask(3, new() { Version = "  1.5  ", AllocatedBy = "  Alice  " });

        var t = doc.GetTask(3)!;
        Assert.Equal("1.5", t.Version);          // stored trimmed
        Assert.Equal("Alice", t.AllocatedBy);

        // Empty string clears both, leaving no whitespace-y attribute behind.
        doc.UpdateTask(3, new() { Version = "", AllocatedBy = "" });
        var cleared = doc.GetTask(3)!;
        Assert.Null(cleared.Version);
        Assert.Null(cleared.AllocatedBy);

        var xml = doc.ToXmlString();
        Assert.DoesNotContain(" VERSION=", xml);   // leading space avoids matching root FILEVERSION
        Assert.DoesNotContain("ALLOCATEDBY=", xml);
    }

    [Fact]
    public void Update_sets_and_clears_time_estimate_and_spent()
    {
        var doc = TestData.Sample();
        doc.UpdateTask(3, new()
        {
            TimeEstimate = 2, TimeEstimateUnit = TimeUnit.Days,
            TimeSpent = 30, TimeSpentUnit = TimeUnit.Minutes,
        });

        var t = doc.GetTask(3)!;
        Assert.Equal(2, t.TimeEstimate);
        Assert.Equal("days", t.TimeEstimateUnit);
        Assert.Equal(30, t.TimeSpent);
        Assert.Equal("minutes", t.TimeSpentUnit);

        doc.UpdateTask(3, new() { ClearTimeEstimate = true, ClearTimeSpent = true });
        var cleared = doc.GetTask(3)!;
        Assert.Null(cleared.TimeEstimate);
        Assert.Null(cleared.TimeEstimateUnit);
        Assert.Null(cleared.TimeSpent);
        Assert.Null(cleared.TimeSpentUnit);

        var xml = doc.ToXmlString();
        Assert.DoesNotContain("TIMEESTIMATE=", xml);
        Assert.DoesNotContain("TIMEESTUNITS=", xml);
        Assert.DoesNotContain("TIMESPENT=", xml);
        Assert.DoesNotContain("TIMESPENTUNITS=", xml);
    }

    [Fact]
    public void Update_value_alone_keeps_existing_unit()
    {
        var doc = TestData.Sample();
        doc.UpdateTask(3, new() { TimeEstimate = 2, TimeEstimateUnit = TimeUnit.Days });

        doc.UpdateTask(3, new() { TimeEstimate = 5 }); // unit omitted
        var t = doc.GetTask(3)!;
        Assert.Equal(5, t.TimeEstimate);
        Assert.Equal("days", t.TimeEstimateUnit);   // preserved, not reset to hours
    }

    [Fact]
    public void Update_unit_alone_relabels_existing_estimate()
    {
        var doc = TestData.Sample();
        doc.UpdateTask(3, new() { TimeEstimate = 4, TimeEstimateUnit = TimeUnit.Hours });

        doc.UpdateTask(3, new() { TimeEstimateUnit = TimeUnit.Days }); // value omitted
        var t = doc.GetTask(3)!;
        Assert.Equal(4, t.TimeEstimate);
        Assert.Equal("days", t.TimeEstimateUnit);
    }

    [Fact]
    public void Update_unit_alone_on_task_without_estimate_is_noop()
    {
        var doc = TestData.Sample();
        doc.UpdateTask(3, new() { TimeEstimateUnit = TimeUnit.Days }); // task 3 has no estimate

        var t = doc.GetTask(3)!;
        Assert.Null(t.TimeEstimate);
        Assert.Null(t.TimeEstimateUnit);
        Assert.DoesNotContain("TIMEESTUNITS=", doc.ToXmlString());
    }

    [Fact]
    public void Update_unit_alone_on_zero_estimate_is_noop()
    {
        var doc = TestData.Sample();
        // A clamped-to-zero estimate writes TIMEESTIMATE="0..." but reads as "unset".
        var id = doc.AddTask(new() { Title = "Zero", TimeEstimate = -1 }).Id;

        doc.UpdateTask(id, new() { TimeEstimateUnit = TimeUnit.Days }); // re-label a zero amount
        Assert.Null(doc.GetTask(id)!.TimeEstimateUnit);
        Assert.DoesNotContain("TIMEESTUNITS=\"D\"", doc.ToXmlString());
    }

    [Fact]
    public void Update_sets_and_clears_start_date()
    {
        var doc = TestData.Sample();
        doc.UpdateTask(3, new() { StartDate = new DateTime(2026, 3, 4) });
        Assert.Equal(new DateTime(2026, 3, 4), doc.GetTask(3)!.StartDate);

        doc.UpdateTask(3, new() { ClearStartDate = true });
        Assert.Null(doc.GetTask(3)!.StartDate);
    }

    [Fact]
    public void Update_flag_left_null_is_unchanged()
    {
        var doc = TestData.Sample();
        doc.UpdateTask(3, new() { Flag = true });

        doc.UpdateTask(3, new() { Title = "Renamed" }); // flag omitted
        Assert.True(doc.GetTask(3)!.IsFlagged);
    }

    [Fact]
    public void ClearRisk_removes_risk()
    {
        var doc = TestData.Sample();
        doc.UpdateTask(2, new() { Risk = 5 });
        Assert.Equal(5, doc.GetTask(2)!.Risk);

        doc.UpdateTask(2, new() { ClearRisk = true });
        Assert.Null(doc.GetTask(2)!.Risk);
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
