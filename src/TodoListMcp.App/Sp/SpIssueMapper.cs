using System.Globalization;
using TodoListMcp.Core;
using TodoListMcp.Core.Model;

namespace TodoListMcp.App.Sp;

/// <summary>
/// The single translation point between a ToDoList document and the SP v1 contract shape. All
/// .tdl → issue mapping lives here, so a change to what SP expects is a one-file edit (and never a
/// plugin redeploy). The Core already parses OLE dates into <see cref="DateTime"/>, so no date-serial
/// maths is needed here — only formatting.
/// </summary>
internal static class SpIssueMapper
{
    /// <summary>Flattened task summaries, filtered by completion and an optional title substring.</summary>
    /// <param name="status">"open", "done", or "all" (already normalised by the caller).</param>
    public static IReadOnlyList<SpIssueSummary> Summaries(
        TodoListDocument doc, string status, string? q, int limit)
    {
        var term = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        var result = new List<SpIssueSummary>();
        foreach (var (task, _) in Flatten(doc.GetTasks(), null))
        {
            if (status == "open" && task.IsDone) continue;
            if (status == "done" && !task.IsDone) continue;
            if (term is not null && task.Title.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0) continue;

            result.Add(Summary(task));
            if (result.Count >= limit) break;
        }
        return result;
    }

    /// <summary>Full detail for one task by ID, or null when the list has no such task.</summary>
    public static SpIssue? Detail(TodoListDocument doc, int id)
    {
        foreach (var (task, parent) in Flatten(doc.GetTasks(), null))
            if (task.Id == id) return Detail(task, parent, doc);
        return null;
    }

    // Depth-first walk yielding each task with its parent (null for top-level tasks).
    private static IEnumerable<(TodoTask task, TodoTask? parent)> Flatten(
        IReadOnlyList<TodoTask> tasks, TodoTask? parent)
    {
        foreach (var t in tasks)
        {
            yield return (t, parent);
            foreach (var child in Flatten(t.Subtasks, t))
                yield return child;
        }
    }

    private static SpIssueSummary Summary(TodoTask t) => new(
        Id: t.Id.ToString(CultureInfo.InvariantCulture),
        Title: t.Title,
        Status: t.IsDone ? "done" : "open",
        IsDone: t.IsDone,
        Assignee: People(t),
        DueDate: DateOnly(t.DueDate),
        Url: "");

    private static SpIssue Detail(TodoTask t, TodoTask? parent, TodoListDocument doc) => new(
        Id: t.Id.ToString(CultureInfo.InvariantCulture),
        Title: t.Title,
        Body: t.Comments ?? "",
        IsDone: t.IsDone,
        State: t.IsDone ? "done" : "open",
        Url: "",                                   // v1: local app, no web URL
        LastUpdated: EpochMs(t.LastModified),
        CreatedAt: EpochMs(t.CreationDate),
        DueOn: DateOnly(t.DueDate),
        Assignee: People(t),
        Labels: t.Categories,
        Priority: t.Priority,
        ProjectNames: doc.ProjectName ?? "",
        ParentName: parent?.Title,
        NumSubtasks: t.Subtasks.Count);

    // ToDoList allows multiple assignees; SP shows a single string, so join them.
    private static string? People(TodoTask t) =>
        t.AllocatedTo.Count > 0 ? string.Join(", ", t.AllocatedTo) : null;

    private static string? DateOnly(DateTime? dt) =>
        dt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ToDoList dates are local wall-clock; treat the parsed value as Local for the epoch.
    private static long? EpochMs(DateTime? dt) => dt is null
        ? null
        : new DateTimeOffset(DateTime.SpecifyKind(dt.Value, DateTimeKind.Local)).ToUnixTimeMilliseconds();
}
