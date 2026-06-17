using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using TodoListMcp.Core.Model;

namespace TodoListMcp.App.Mcp;

/// <summary>
/// The MCP tool surface. Each tool selects a configured list via the optional
/// <c>list</c> alias (omit it to use the default list) and delegates to the
/// format-faithful core engine through <see cref="TodoListManager"/>.
/// </summary>
[McpServerToolType]
public sealed class TodoTools
{
    private readonly TodoListManager _manager;

    public TodoTools(TodoListManager manager) => _manager = manager;

    [McpServerTool(Name = "list_todo_files")]
    [Description("List the configured ToDoList (.tdl) files this server can read and modify, with their aliases.")]
    public object ListFiles() =>
        _manager.Files.Select(f => new { f.Alias, f.Path, f.Default }).ToArray();

    [McpServerTool(Name = "get_tasks")]
    [Description("Get the full task hierarchy (with subtasks) from a configured ToDoList file.")]
    public IReadOnlyList<TodoTask> GetTasks(
        [Description("Alias of the configured list. Omit to use the default list.")] string? list = null) =>
        _manager.Read(list, d => d.GetTasks());

    [McpServerTool(Name = "get_task")]
    [Description("Get a single task and its subtasks by ID.")]
    public TodoTask GetTask(
        [Description("The task ID.")] int id,
        [Description("Alias of the configured list. Omit to use the default list.")] string? list = null) =>
        _manager.Read(list, d => d.GetTask(id))
        ?? throw new InvalidOperationException($"Task {id} was not found.");

    [McpServerTool(Name = "search_tasks")]
    [Description("Search tasks by text, category, assignee, completion state, or minimum priority. Returns a flat list of matches.")]
    public IReadOnlyList<TodoTask> SearchTasks(
        [Description("Case-insensitive text matched against the title and notes.")] string? text = null,
        [Description("Only tasks in this category.")] string? category = null,
        [Description("Only tasks assigned to this person.")] string? person = null,
        [Description("Filter by completion: true = done only, false = open only.")] bool? completed = null,
        [Description("Minimum priority on the 0-10 scale.")] int? minPriority = null,
        [Description("Alias of the configured list. Omit to use the default list.")] string? list = null) =>
        _manager.Read(list, d => d.Search(new TaskQuery
        {
            Text = text,
            Category = category,
            Person = person,
            Completed = completed,
            MinPriority = minPriority,
        }));

    [McpServerTool(Name = "add_task")]
    [Description("Create a new task and return it.")]
    public TodoTask AddTask(
        [Description("The task title.")] string title,
        [Description("Parent task ID to nest under. Omit for a top-level task.")] int? parentId = null,
        [Description("Zero-based position among siblings. Omit to append at the end.")] int? index = null,
        [Description("Plain-text notes for the task.")] string? comments = null,
        [Description("Priority on the 0-10 scale.")] int? priority = null,
        [Description("Due date as yyyy-MM-dd or ISO 8601.")] string? dueDate = null,
        [Description("Categories to assign.")] string[]? categories = null,
        [Description("People to assign the task to.")] string[]? allocatedTo = null,
        [Description("Alias of the configured list. Omit to use the default list.")] string? list = null) =>
        _manager.Write(list, d => d.AddTask(new AddTaskRequest
        {
            Title = title,
            ParentId = parentId,
            Index = index,
            Comments = comments,
            Priority = priority,
            DueDate = ParseDate(dueDate),
            Categories = categories ?? Array.Empty<string>(),
            AllocatedTo = allocatedTo ?? Array.Empty<string>(),
        }));

    [McpServerTool(Name = "update_task")]
    [Description("Update fields of an existing task. Only the parameters you supply are changed.")]
    public TodoTask UpdateTask(
        [Description("The task ID.")] int id,
        [Description("New title.")] string? title = null,
        [Description("New notes (empty string clears the notes).")] string? comments = null,
        [Description("New priority on the 0-10 scale.")] int? priority = null,
        [Description("Remove the priority entirely.")] bool clearPriority = false,
        [Description("Completion percentage, 0-100.")] int? percentDone = null,
        [Description("New due date as yyyy-MM-dd or ISO 8601.")] string? dueDate = null,
        [Description("Remove the due date entirely.")] bool clearDueDate = false,
        [Description("Replace the categories (empty array clears them).")] string[]? categories = null,
        [Description("Replace the assignees (empty array clears them).")] string[]? allocatedTo = null,
        [Description("Alias of the configured list. Omit to use the default list.")] string? list = null) =>
        _manager.Write(list, d => d.UpdateTask(id, new UpdateTaskRequest
        {
            Title = title,
            Comments = comments,
            Priority = priority,
            ClearPriority = clearPriority,
            PercentDone = percentDone,
            DueDate = ParseDate(dueDate),
            ClearDueDate = clearDueDate,
            Categories = categories,
            AllocatedTo = allocatedTo,
        }));

    [McpServerTool(Name = "complete_task")]
    [Description("Mark a task as done (sets the done date and 100% progress).")]
    public TodoTask CompleteTask(
        [Description("The task ID.")] int id,
        [Description("Alias of the configured list. Omit to use the default list.")] string? list = null) =>
        _manager.Write(list, d => d.CompleteTask(id));

    [McpServerTool(Name = "reopen_task")]
    [Description("Re-open a completed task (clears the done date and resets progress to 0%).")]
    public TodoTask ReopenTask(
        [Description("The task ID.")] int id,
        [Description("Alias of the configured list. Omit to use the default list.")] string? list = null) =>
        _manager.Write(list, d => d.ReopenTask(id));

    [McpServerTool(Name = "delete_task")]
    [Description("Delete a task and all of its subtasks.")]
    public object DeleteTask(
        [Description("The task ID.")] int id,
        [Description("Alias of the configured list. Omit to use the default list.")] string? list = null) =>
        _manager.Write(list, d => new { id, deleted = d.DeleteTask(id) });

    [McpServerTool(Name = "move_task")]
    [Description("Move a task under a new parent and/or to a new position among its siblings.")]
    public TodoTask MoveTask(
        [Description("The task ID to move.")] int id,
        [Description("New parent task ID. Omit to move the task to the top level.")] int? newParentId = null,
        [Description("Zero-based position among the new siblings. Omit to append at the end.")] int? index = null,
        [Description("Alias of the configured list. Omit to use the default list.")] string? list = null) =>
        _manager.Write(list, d => d.MoveTask(id, newParentId, index));

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt;
        throw new ArgumentException($"Could not parse date '{value}'. Use yyyy-MM-dd or ISO 8601.");
    }
}
