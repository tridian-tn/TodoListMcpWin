using TodoListMcp.Core;

namespace TodoListMcp.Core.Tests;

/// <summary>
/// ToDoList stores task-ordering dependencies as repeated &lt;DEPENDS&gt; child elements whose body
/// is the local dependee ID, with an optional DEPENDSLEADIN (lead/lag in days). The server surfaces
/// local dependencies on <c>TodoTask.Dependencies</c> and edits them with AddDependency/
/// RemoveDependency, validating existence and self-reference, while leaving cross-tasklist
/// references untouched on round-trip.
/// </summary>
public class DependencyTests
{
    // Tasks 1, 2, 3 (siblings). Task 1 already depends on 3 with a +5d lead-in; task 2 depends on 3
    // (no lead-in) and on a cross-tasklist reference that the server should preserve but not surface.
    private const string Xml =
        "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
        "<TODOLIST PROJECTNAME=\"Deps\" NEXTUNIQUEID=\"4\" FILEFORMAT=\"12\">" +
        "<TASK ID=\"1\" TITLE=\"First\" POS=\"0\" POSSTRING=\"1\"><DEPENDS DEPENDSLEADIN=\"5\">3</DEPENDS></TASK>" +
        "<TASK ID=\"2\" TITLE=\"Second\" POS=\"1\" POSSTRING=\"2\"><DEPENDS>3</DEPENDS><DEPENDS>other.tdl?9</DEPENDS></TASK>" +
        "<TASK ID=\"3\" TITLE=\"Third\" POS=\"2\" POSSTRING=\"3\"/>" +
        "</TODOLIST>";

    private static TodoListDocument Doc() => TodoListDocument.Parse(Xml, TestData.Clock);

    // ---- Reading -----------------------------------------------------------

    [Fact]
    public void Reads_dependency_with_lead_in()
    {
        var deps = Doc().GetTask(1)!.Dependencies;
        var dep = Assert.Single(deps);
        Assert.Equal(3, dep.DependsOnId);
        Assert.Equal(5, dep.LeadInDays);
    }

    [Fact]
    public void Reads_dependency_without_lead_in_as_null()
    {
        var dep = Doc().GetTask(2)!.Dependencies.Single(d => d.DependsOnId == 3);
        Assert.Null(dep.LeadInDays);
    }

    [Fact]
    public void Cross_tasklist_dependency_is_not_surfaced()
    {
        // Task 2's "other.tdl?9" reference is left on disk but not reported as a local dependency.
        var deps = Doc().GetTask(2)!.Dependencies;
        var dep = Assert.Single(deps);
        Assert.Equal(3, dep.DependsOnId);
    }

    [Fact]
    public void Duplicate_dependee_collapses_to_first()
    {
        var doc = TodoListDocument.Parse(
            "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
            "<TODOLIST PROJECTNAME=\"D\" NEXTUNIQUEID=\"3\">" +
            "<TASK ID=\"1\" TITLE=\"T\" POS=\"0\" POSSTRING=\"1\">" +
            "<DEPENDS DEPENDSLEADIN=\"2\">2</DEPENDS><DEPENDS>2</DEPENDS></TASK>" +
            "<TASK ID=\"2\" TITLE=\"U\" POS=\"1\" POSSTRING=\"2\"/></TODOLIST>",
            TestData.Clock);

        var dep = Assert.Single(doc.GetTask(1)!.Dependencies);
        Assert.Equal(2, dep.LeadInDays);
    }

    // ---- AddDependency -----------------------------------------------------

    [Fact]
    public void Add_writes_depends_element_without_lead_in_attribute()
    {
        var doc = Doc();
        doc.AddDependency(3, dependsOnId: 1);

        Assert.Contains("<DEPENDS>1</DEPENDS>", doc.ToXmlString());
        var dep = Assert.Single(doc.GetTask(3)!.Dependencies);
        Assert.Equal(1, dep.DependsOnId);
        Assert.Null(dep.LeadInDays);
    }

    [Fact]
    public void Add_with_lead_in_writes_attribute()
    {
        var doc = Doc();
        doc.AddDependency(3, dependsOnId: 2, leadIn: -4);

        Assert.Contains("<DEPENDS DEPENDSLEADIN=\"-4\">2</DEPENDS>", doc.ToXmlString());
        Assert.Equal(-4, doc.GetTask(3)!.Dependencies.Single().LeadInDays);
    }

    [Fact]
    public void Add_with_zero_lead_in_omits_attribute()
    {
        var doc = Doc();
        doc.AddDependency(3, dependsOnId: 1, leadIn: 0);

        // A zero lead-in is the default — written as the bare element, no DEPENDSLEADIN attribute.
        Assert.Contains("<DEPENDS>1</DEPENDS>", doc.ToXmlString());
        Assert.Null(doc.GetTask(3)!.Dependencies.Single().LeadInDays);
    }

    [Fact]
    public void Re_adding_existing_dependee_updates_lead_in_without_duplicating()
    {
        var doc = Doc();
        // Task 1 already depends on 3 with lead-in 5; re-adding replaces the lead-in.
        doc.AddDependency(1, dependsOnId: 3, leadIn: 8);

        var dep = Assert.Single(doc.GetTask(1)!.Dependencies);
        Assert.Equal(8, dep.LeadInDays);
        // The original lead-in is gone, not left behind as a second element.
        Assert.DoesNotContain("DEPENDSLEADIN=\"5\"", doc.ToXmlString());
    }

    [Fact]
    public void Re_adding_without_lead_in_clears_existing_lead_in()
    {
        var doc = Doc();
        doc.AddDependency(1, dependsOnId: 3);

        Assert.Null(doc.GetTask(1)!.Dependencies.Single().LeadInDays);
        Assert.DoesNotContain("DEPENDSLEADIN", doc.ToXmlString());
    }

    [Fact]
    public void Add_self_dependency_throws()
    {
        Assert.Throws<ArgumentException>(() => Doc().AddDependency(1, dependsOnId: 1));
    }

    [Fact]
    public void Add_dependency_on_missing_task_throws()
    {
        var ex = Assert.Throws<TaskNotFoundException>(() => Doc().AddDependency(1, dependsOnId: 999));
        Assert.Equal(999, ex.TaskId);
    }

    [Fact]
    public void Add_dependency_to_missing_task_throws()
    {
        var ex = Assert.Throws<TaskNotFoundException>(() => Doc().AddDependency(999, dependsOnId: 1));
        Assert.Equal(999, ex.TaskId);
    }

    [Fact]
    public void Adds_multiple_distinct_local_dependencies_in_order()
    {
        var doc = Doc();
        doc.AddDependency(3, dependsOnId: 1);
        doc.AddDependency(3, dependsOnId: 2, leadIn: 5);

        Assert.Equal(
            new[] { (1, (int?)null), (2, (int?)5) },
            doc.GetTask(3)!.Dependencies.Select(d => (d.DependsOnId, d.LeadInDays)).ToArray());
    }

    [Fact]
    public void Editing_dependencies_leaves_other_task_data_intact()
    {
        // A task carrying notes, a category and an unknown attribute. Adding a dependency must edit
        // the tree in place, not regenerate the element, so the other data round-trips untouched.
        var doc = TodoListDocument.Parse(
            "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
            "<TODOLIST PROJECTNAME=\"D\" NEXTUNIQUEID=\"3\">" +
            "<TASK ID=\"1\" TITLE=\"Rich\" COLOR=\"255\" POS=\"0\" POSSTRING=\"1\">" +
            "<COMMENTS>Keep me</COMMENTS><CATEGORY>Work</CATEGORY></TASK>" +
            "<TASK ID=\"2\" TITLE=\"Other\" POS=\"1\" POSSTRING=\"2\"/></TODOLIST>",
            TestData.Clock);

        doc.AddDependency(1, dependsOnId: 2);

        var t = doc.GetTask(1)!;
        Assert.Equal("Keep me", t.Comments);
        Assert.Equal(new[] { "Work" }, t.Categories);
        Assert.Equal(2, t.Dependencies.Single().DependsOnId);
        Assert.Contains("COLOR=\"255\"", doc.ToXmlString()); // unknown attribute preserved
    }

    [Fact]
    public void Add_marks_dirty()
    {
        var doc = Doc();
        doc.AddDependency(2, dependsOnId: 1);
        Assert.True(doc.IsDirty);
    }

    // ---- RemoveDependency --------------------------------------------------

    [Fact]
    public void Remove_deletes_the_dependency()
    {
        var doc = Doc();
        doc.RemoveDependency(1, dependsOnId: 3);

        Assert.Empty(doc.GetTask(1)!.Dependencies);
        Assert.DoesNotContain("DEPENDSLEADIN=\"5\"", doc.ToXmlString());
    }

    [Fact]
    public void Remove_missing_dependency_is_noop_and_leaves_document_clean()
    {
        var doc = Doc();
        doc.RemoveDependency(1, dependsOnId: 2); // task 1 doesn't depend on 2

        Assert.Single(doc.GetTask(1)!.Dependencies);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void Remove_leaves_cross_tasklist_dependency_untouched()
    {
        var doc = Doc();
        doc.RemoveDependency(2, dependsOnId: 3);

        // The local dependency is gone, the cross-tasklist one preserved verbatim.
        Assert.Empty(doc.GetTask(2)!.Dependencies);
        Assert.Contains("<DEPENDS>other.tdl?9</DEPENDS>", doc.ToXmlString());
    }

    [Fact]
    public void Remove_marks_dirty()
    {
        var doc = Doc();
        doc.RemoveDependency(1, dependsOnId: 3);
        Assert.True(doc.IsDirty);
    }

    [Fact]
    public void Remove_from_missing_task_throws()
    {
        var ex = Assert.Throws<TaskNotFoundException>(() => Doc().RemoveDependency(999, dependsOnId: 1));
        Assert.Equal(999, ex.TaskId);
    }

    [Fact]
    public void Re_adding_collapses_duplicate_depends_elements()
    {
        // A malformed/externally-edited file with the same dependee twice. Updating it should leave
        // a single element, matching ToDoList's "a dependee appears at most once" invariant.
        var doc = TodoListDocument.Parse(
            "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
            "<TODOLIST PROJECTNAME=\"D\" NEXTUNIQUEID=\"3\">" +
            "<TASK ID=\"1\" TITLE=\"T\" POS=\"0\" POSSTRING=\"1\">" +
            "<DEPENDS>2</DEPENDS><DEPENDS>2</DEPENDS></TASK>" +
            "<TASK ID=\"2\" TITLE=\"U\" POS=\"1\" POSSTRING=\"2\"/></TODOLIST>",
            TestData.Clock);

        doc.AddDependency(1, dependsOnId: 2, leadIn: 3);

        Assert.Single(doc.GetTask(1)!.Dependencies);
        var xml = doc.ToXmlString();
        // Exactly one DEPENDS element survives, carrying the new lead-in.
        Assert.Equal(1, CountOccurrences(xml, "<DEPENDS"));
        Assert.Contains("<DEPENDS DEPENDSLEADIN=\"3\">2</DEPENDS>", xml);
    }

    [Fact]
    public void Read_treats_explicit_zero_lead_in_as_none()
    {
        // ToDoList never distinguishes DEPENDSLEADIN="0" from an absent attribute; surface both as null.
        var doc = TodoListDocument.Parse(
            "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
            "<TODOLIST PROJECTNAME=\"D\" NEXTUNIQUEID=\"3\">" +
            "<TASK ID=\"1\" TITLE=\"T\" POS=\"0\" POSSTRING=\"1\"><DEPENDS DEPENDSLEADIN=\"0\">2</DEPENDS></TASK>" +
            "<TASK ID=\"2\" TITLE=\"U\" POS=\"1\" POSSTRING=\"2\"/></TODOLIST>",
            TestData.Clock);

        Assert.Null(doc.GetTask(1)!.Dependencies.Single().LeadInDays);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { count++; i += needle.Length; }
        return count;
    }

    // ---- Stale-dependency handling (ToDoList fidelity) ---------------------

    [Fact]
    public void Read_weeds_out_dependency_on_a_missing_task_without_mutating()
    {
        // A dependency on a task not in the list (e.g. left by an external edit). ToDoList filters
        // these on read but leaves the element on disk until something prunes it.
        var doc = TodoListDocument.Parse(
            "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
            "<TODOLIST PROJECTNAME=\"D\" NEXTUNIQUEID=\"2\">" +
            "<TASK ID=\"1\" TITLE=\"T\" POS=\"0\" POSSTRING=\"1\"><DEPENDS>99</DEPENDS></TASK></TODOLIST>",
            TestData.Clock);

        Assert.Empty(doc.GetTask(1)!.Dependencies);          // filtered from the projection
        Assert.Contains("<DEPENDS>99</DEPENDS>", doc.ToXmlString()); // but not removed from disk
        Assert.False(doc.IsDirty);                            // a read changes nothing
    }

    [Fact]
    public void Deleting_a_task_prunes_it_from_other_tasks_dependencies()
    {
        var doc = Doc(); // task 1 and task 2 both depend on task 3
        doc.DeleteTask(3);

        Assert.Empty(doc.GetTask(1)!.Dependencies);
        Assert.Empty(doc.GetTask(2)!.Dependencies);
        // The stale <DEPENDS> elements are actually removed (not merely filtered on read)...
        Assert.DoesNotContain(">3</DEPENDS>", doc.ToXmlString());
        // ...while the unrelated cross-tasklist reference is left intact.
        Assert.Contains("<DEPENDS>other.tdl?9</DEPENDS>", doc.ToXmlString());
    }

    [Fact]
    public void Deleting_a_parent_prunes_every_subtree_id_from_dependents()
    {
        // Task 3 depends on task 2, which is a child of task 1. Deleting task 1 removes both 1 and 2,
        // so task 3's dependency on the (now gone) child must be pruned too.
        var doc = TodoListDocument.Parse(
            "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
            "<TODOLIST PROJECTNAME=\"D\" NEXTUNIQUEID=\"4\">" +
            "<TASK ID=\"1\" TITLE=\"Parent\" POS=\"0\" POSSTRING=\"1\">" +
            "<TASK ID=\"2\" TITLE=\"Child\" POS=\"0\" POSSTRING=\"1.1\"/></TASK>" +
            "<TASK ID=\"3\" TITLE=\"Dependent\" POS=\"1\" POSSTRING=\"2\"><DEPENDS>2</DEPENDS></TASK></TODOLIST>",
            TestData.Clock);

        doc.DeleteTask(1);

        Assert.Empty(doc.GetTask(3)!.Dependencies);
        Assert.DoesNotContain("DEPENDS", doc.ToXmlString());
    }

    [Fact]
    public void Deleting_a_task_with_no_dependents_does_not_touch_dependencies()
    {
        var doc = Doc(); // nothing depends on task 1
        doc.DeleteTask(1);

        // Task 2's own dependencies are untouched by deleting an unrelated task.
        Assert.Contains("<DEPENDS>3</DEPENDS>", doc.ToXmlString());
    }

    // ---- Locking -----------------------------------------------------------

    [Fact]
    public void Add_dependency_on_locked_task_throws()
    {
        var doc = TodoListDocument.Parse(
            "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
            "<TODOLIST PROJECTNAME=\"L\" NEXTUNIQUEID=\"3\">" +
            "<TASK ID=\"1\" TITLE=\"Locked\" LOCK=\"1\" POS=\"0\" POSSTRING=\"1\"/>" +
            "<TASK ID=\"2\" TITLE=\"Open\" POS=\"1\" POSSTRING=\"2\"/></TODOLIST>",
            TestData.Clock);

        var ex = Assert.Throws<TaskLockedException>(() => doc.AddDependency(1, dependsOnId: 2));
        Assert.Equal(1, ex.TaskId);
    }

    [Fact]
    public void Depending_on_a_locked_task_is_allowed()
    {
        // Locking gates edits to the locked task itself, not being referenced by another task.
        var doc = TodoListDocument.Parse(
            "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
            "<TODOLIST PROJECTNAME=\"L\" NEXTUNIQUEID=\"3\">" +
            "<TASK ID=\"1\" TITLE=\"Open\" POS=\"0\" POSSTRING=\"1\"/>" +
            "<TASK ID=\"2\" TITLE=\"Locked\" LOCK=\"1\" POS=\"1\" POSSTRING=\"2\"/></TODOLIST>",
            TestData.Clock);

        doc.AddDependency(1, dependsOnId: 2);
        Assert.Equal(2, doc.GetTask(1)!.Dependencies.Single().DependsOnId);
    }
}
