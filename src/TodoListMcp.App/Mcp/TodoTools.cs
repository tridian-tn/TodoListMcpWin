using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using TodoListMcp.Core;
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
    [Description("Search tasks by text, category, assignee, allocated-by, completion, flag, status, version, external ID, minimum priority/risk, or time estimate/spent range. Returns a flat list of matches.")]
    public IReadOnlyList<TodoTask> SearchTasks(
        [Description("Case-insensitive text matched against the title and notes.")] string? text = null,
        [Description("Only tasks in this category.")] string? category = null,
        [Description("Only tasks assigned to this person.")] string? person = null,
        [Description("Filter by completion: true = done only, false = open only.")] bool? completed = null,
        [Description("Filter by flag: true = flagged only, false = un-flagged only.")] bool? flagged = null,
        [Description("Exact (case-insensitive) workflow status, e.g. \"In Progress\".")] string? status = null,
        [Description("Exact (case-insensitive) version/release string.")] string? version = null,
        [Description("Exact (case-insensitive) external ID, e.g. an issue key.")] string? externalId = null,
        [Description("Exact (case-insensitive) person who allocated the task.")] string? allocatedBy = null,
        [Description("Minimum priority on the 0-10 scale.")] int? minPriority = null,
        [Description("Minimum risk on the 0-10 scale.")] int? minRisk = null,
        [Description("Minimum time estimate in hours (days/weeks/etc. normalised at 8h/day).")] double? minEstimateHours = null,
        [Description("Maximum time estimate in hours (normalised at 8h/day).")] double? maxEstimateHours = null,
        [Description("Minimum time spent in hours (normalised at 8h/day).")] double? minSpentHours = null,
        [Description("Maximum time spent in hours (normalised at 8h/day).")] double? maxSpentHours = null,
        [Description("Alias of the configured list. Omit to use the default list.")] string? list = null) =>
        _manager.Read(list, d => d.Search(new TaskQuery
        {
            Text = text,
            Category = category,
            Person = person,
            Completed = completed,
            Flagged = flagged,
            Status = status,
            Version = version,
            ExternalId = externalId,
            AllocatedBy = allocatedBy,
            MinPriority = minPriority,
            MinRisk = minRisk,
            MinEstimateHours = minEstimateHours,
            MaxEstimateHours = maxEstimateHours,
            MinSpentHours = minSpentHours,
            MaxSpentHours = maxSpentHours,
        }));

    [McpServerTool(Name = "add_task")]
    [Description("Create a new task and return it.")]
    public TodoTask AddTask(
        [Description("The task title.")] string title,
        [Description("Parent task ID to nest under. Omit for a top-level task.")] int? parentId = null,
        [Description("Zero-based position among siblings. Omit to append at the end.")] int? index = null,
        [Description("Notes for the task.")] string? comments = null,
        [Description("Format for the notes: plain (default), markdown, or html. Rich text and spreadsheet can't be authored here.")] string? commentsFormat = null,
        [Description("Priority on the 0-10 scale.")] int? priority = null,
        [Description("Risk on the 0-10 scale.")] int? risk = null,
        [Description("Initial completion percentage, 0-100. Omit to start at 0.")] int? percentDone = null,
        [Description("Time estimate value, in the chosen unit.")] double? timeEstimate = null,
        [Description("Unit for the estimate: minutes/hours/days/weekdays/weeks/months/years (or I/H/D/K/W/M/Y). Default hours.")] string? timeEstimateUnit = null,
        [Description("Time spent value, in the chosen unit.")] double? timeSpent = null,
        [Description("Unit for time spent (same options as the estimate). Default hours.")] string? timeSpentUnit = null,
        [Description("Due date as yyyy-MM-dd or ISO 8601.")] string? dueDate = null,
        [Description("Start date as yyyy-MM-dd or ISO 8601.")] string? startDate = null,
        [Description("Free-text workflow status, e.g. \"In Progress\".")] string? status = null,
        [Description("Target version/release string.")] string? version = null,
        [Description("Set the flag (star) marker on the task.")] bool flag = false,
        [Description("Caller-defined external reference, e.g. an issue key.")] string? externalId = null,
        [Description("Categories to assign.")] string[]? categories = null,
        [Description("People to assign the task to.")] string[]? allocatedTo = null,
        [Description("Who allocated the task (single value).")] string? allocatedBy = null,
        [Description("File/URL links to attach (local/network paths, http(s)/mailto URLs, or tdl:// task links). Stored verbatim; blank entries are ignored and exact duplicates are collapsed case-insensitively, as ToDoList does.")] string[]? fileLinks = null,
        [Description("Alias of the configured list. Omit to use the default list.")] string? list = null) =>
        _manager.Write(list, d => d.AddTask(new AddTaskRequest
        {
            Title = title,
            ParentId = parentId,
            Index = index,
            Comments = comments,
            CommentsFormat = comments is null ? CommentContentFormat.Plain : ParseCommentsFormat(commentsFormat),
            Priority = priority,
            Risk = risk,
            PercentDone = percentDone,
            TimeEstimate = timeEstimate,
            TimeEstimateUnit = ParseUnit(timeEstimateUnit, nameof(timeEstimateUnit)),
            TimeSpent = timeSpent,
            TimeSpentUnit = ParseUnit(timeSpentUnit, nameof(timeSpentUnit)),
            DueDate = ParseDate(dueDate),
            StartDate = ParseDate(startDate),
            Status = status,
            Version = version,
            Flag = flag,
            ExternalId = externalId,
            Categories = categories ?? Array.Empty<string>(),
            AllocatedTo = allocatedTo ?? Array.Empty<string>(),
            AllocatedBy = allocatedBy,
            FileLinks = fileLinks ?? Array.Empty<string>(),
        }));

    [McpServerTool(Name = "update_task")]
    [Description("Update fields of an existing task. Only the parameters you supply are changed.")]
    public TodoTask UpdateTask(
        [Description("The task ID.")] int id,
        [Description("New title.")] string? title = null,
        [Description("New notes (empty string clears the notes).")] string? comments = null,
        [Description("Format for new notes: plain (default), markdown, or html. Only applies when comments is supplied.")] string? commentsFormat = null,
        [Description("Allow replacing existing formatted (rich text/HTML/Markdown/spreadsheet) notes. Without this, editing the notes of such a task is refused so ToDoList's rich content isn't discarded.")] bool replaceFormattedComments = false,
        [Description("New priority on the 0-10 scale.")] int? priority = null,
        [Description("Remove the priority entirely.")] bool clearPriority = false,
        [Description("New risk on the 0-10 scale.")] int? risk = null,
        [Description("Remove the risk entirely.")] bool clearRisk = false,
        [Description("Completion percentage, 0-100.")] int? percentDone = null,
        [Description("New time estimate value, in timeEstimateUnit (or the existing unit if omitted).")] double? timeEstimate = null,
        [Description("Unit for the estimate: minutes/hours/days/weekdays/weeks/months/years (or I/H/D/K/W/M/Y). Re-labels the estimate when supplied alone.")] string? timeEstimateUnit = null,
        [Description("Remove the time estimate entirely.")] bool clearTimeEstimate = false,
        [Description("New time spent value, in timeSpentUnit (or the existing unit if omitted).")] double? timeSpent = null,
        [Description("Unit for time spent (same options as the estimate). Re-labels time spent when supplied alone.")] string? timeSpentUnit = null,
        [Description("Remove the time spent entirely.")] bool clearTimeSpent = false,
        [Description("New due date as yyyy-MM-dd or ISO 8601.")] string? dueDate = null,
        [Description("Remove the due date entirely.")] bool clearDueDate = false,
        [Description("New start date as yyyy-MM-dd or ISO 8601.")] string? startDate = null,
        [Description("Remove the start date entirely.")] bool clearStartDate = false,
        [Description("New workflow status (empty string clears it).")] string? status = null,
        [Description("New version/release string (empty string clears it).")] string? version = null,
        [Description("Set or remove the flag (star) marker.")] bool? flag = null,
        [Description("New external ID (empty string clears it).")] string? externalId = null,
        [Description("Replace the categories (empty array clears them).")] string[]? categories = null,
        [Description("Replace the assignees (empty array clears them).")] string[]? allocatedTo = null,
        [Description("New allocated-by person (empty string clears it).")] string? allocatedBy = null,
        [Description("Replace the file/URL links (empty array clears them). Stored verbatim; blank entries are ignored and exact duplicates are collapsed case-insensitively, as ToDoList does.")] string[]? fileLinks = null,
        [Description("Alias of the configured list. Omit to use the default list.")] string? list = null) =>
        _manager.Write(list, d => d.UpdateTask(id, new UpdateTaskRequest
        {
            Title = title,
            Comments = comments,
            CommentsFormat = comments is null ? CommentContentFormat.Plain : ParseCommentsFormat(commentsFormat),
            ReplaceFormattedComments = replaceFormattedComments,
            Priority = priority,
            ClearPriority = clearPriority,
            Risk = risk,
            ClearRisk = clearRisk,
            PercentDone = percentDone,
            TimeEstimate = timeEstimate,
            TimeEstimateUnit = ParseUnit(timeEstimateUnit, nameof(timeEstimateUnit)),
            ClearTimeEstimate = clearTimeEstimate,
            TimeSpent = timeSpent,
            TimeSpentUnit = ParseUnit(timeSpentUnit, nameof(timeSpentUnit)),
            ClearTimeSpent = clearTimeSpent,
            DueDate = ParseDate(dueDate),
            ClearDueDate = clearDueDate,
            StartDate = ParseDate(startDate),
            ClearStartDate = clearStartDate,
            Status = status,
            Version = version,
            Flag = flag,
            ExternalId = externalId,
            Categories = categories,
            AllocatedTo = allocatedTo,
            AllocatedBy = allocatedBy,
            FileLinks = fileLinks,
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

    [McpServerTool(Name = "add_dependency")]
    [Description("Make a task depend on another task in the same list (a DEPENDS ordering relationship), optionally with a lead-in/lag in days. Re-adding an existing dependency updates its lead-in.")]
    public TodoTask AddDependency(
        [Description("The dependent task's ID (the one that should follow the other).")] int id,
        [Description("The ID of the task it depends on. Must exist in the same list; cannot be the task itself.")] int dependsOnId,
        [Description("Lead-in/lag offset in days: positive delays the start, negative brings it forward. Omit or 0 for none.")] int? leadIn = null,
        [Description("Alias of the configured list. Omit to use the default list.")] string? list = null) =>
        _manager.Write(list, d => d.AddDependency(id, dependsOnId, leadIn));

    [McpServerTool(Name = "remove_dependency")]
    [Description("Remove a task's dependency on another task. No-op if the dependency isn't present.")]
    public TodoTask RemoveDependency(
        [Description("The dependent task's ID.")] int id,
        [Description("The ID of the task it currently depends on.")] int dependsOnId,
        [Description("Alias of the configured list. Omit to use the default list.")] string? list = null) =>
        _manager.Write(list, d => d.RemoveDependency(id, dependsOnId));

    [McpServerTool(Name = "log_time")]
    [Description("Append a time-log entry to the list's _Log.csv sidecar (distinct from a task's time-spent attribute). Logs against a task, or task-less when taskId is omitted. An entry needs a comment, or non-zero hours over a valid period. Optionally also increments the task's time spent.")]
    public TimeLogEntry LogTime(
        [Description("Hours to log (e.g. 1.5). Negative values clamp to 0.")] double hours,
        [Description("Task ID to log against. Omit (or 0) for a task-less entry.")] int? taskId = null,
        [Description("Comment for the entry. Required when hours is 0.")] string? comment = null,
        [Description("End of the period as yyyy-MM-dd HH:mm or ISO 8601; the start is derived as this minus the hours. Defaults to now.")] string? when = null,
        [Description("Explicit start of the period; overrides the when-derived start.")] string? from = null,
        [Description("Explicit end of the period; overrides when.")] string? to = null,
        [Description("Who logged the time. Defaults to the current OS user.")] string? person = null,
        [Description("Entry type: \"Adjusted\" (manual, default) or \"Tracked\" (timer).")] string? type = null,
        [Description("When a task is given, also add these hours to the task's time spent (keeping its unit).")] bool addToTimeSpent = false,
        [Description("Alias of the configured list. Omit to use the default list.")] string? list = null) =>
        _manager.LogTime(list, new LogTimeRequest
        {
            TaskId = taskId ?? 0,
            Hours = hours,
            Comment = comment,
            When = ParseDate(when),
            From = ParseDate(from),
            To = ParseDate(to),
            Person = person,
            Type = type,
            AddToTimeSpent = addToTimeSpent,
        });

    [McpServerTool(Name = "get_time_log")]
    [Description("Read time-log entries from the list's _Log.csv sidecar, optionally filtered by task, date range, or person. Includes task-less entries.")]
    public IReadOnlyList<TimeLogEntry> GetTimeLog(
        [Description("Only entries for this task ID. Use 0 for task-less entries. Omit for all.")] int? taskId = null,
        [Description("Only entries ending on or after this date (yyyy-MM-dd or ISO 8601).")] string? since = null,
        [Description("Only entries starting on or before this date, inclusive of the whole day when no time is given (yyyy-MM-dd or ISO 8601). So since=until=today returns everything logged today.")] string? until = null,
        [Description("Only entries logged by this person (case-insensitive, exact).")] string? person = null,
        [Description("Alias of the configured list. Omit to use the default list.")] string? list = null) =>
        _manager.ReadLog(list, new TimeLogQuery
        {
            TaskId = taskId,
            Since = ParseDate(since),
            Until = ParseUpperBoundDate(until),
            Person = person,
        });

    [McpServerTool(Name = "update_time_log_entry")]
    [Description("Edit a single existing time-log entry in the list's _Log.csv sidecar. Identify the entry by its current fields (taskId/from/to/person/comment/hours) — these must match exactly one entry — and pass the new values to change. Any new* value left unset keeps the current one. Only the sidecar changes; a task's time spent is not adjusted.")]
    public TimeLogEntry UpdateTimeLogEntry(
        [Description("Identify the entry: its task ID (use 0 for a task-less entry). Omit to not match on task.")] int? taskId = null,
        [Description("Identify the entry: its period start (yyyy-MM-dd HH:mm or ISO 8601), matched to the minute.")] string? from = null,
        [Description("Identify the entry: its period end (yyyy-MM-dd HH:mm or ISO 8601), matched to the minute.")] string? to = null,
        [Description("Identify the entry: its person (case-insensitive, exact).")] string? person = null,
        [Description("Identify the entry: its comment (case-sensitive, exact).")] string? comment = null,
        [Description("Identify the entry: its logged hours.")] double? hours = null,
        [Description("New period start (yyyy-MM-dd HH:mm or ISO 8601).")] string? newFrom = null,
        [Description("New period end (yyyy-MM-dd HH:mm or ISO 8601).")] string? newTo = null,
        [Description("New logged hours.")] double? newHours = null,
        [Description("New comment. Pass an empty string to clear it.")] string? newComment = null,
        [Description("New person. Pass an empty string to clear it.")] string? newPerson = null,
        [Description("New type: \"Adjusted\" or \"Tracked\". Pass an empty string to clear it.")] string? newType = null,
        [Description("Alias of the configured list. Omit to use the default list.")] string? list = null) =>
        _manager.UpdateLogEntry(
            list,
            new TimeLogSelector
            {
                TaskId = taskId,
                From = ParseDate(from),
                To = ParseDate(to),
                Person = person,
                Comment = comment,
                Hours = hours,
            },
            new TimeLogEdit
            {
                From = ParseDate(newFrom),
                To = ParseDate(newTo),
                Hours = newHours,
                Comment = newComment,
                Person = newPerson,
                Type = newType,
            });

    [McpServerTool(Name = "delete_time_log_entry")]
    [Description("Delete a single existing time-log entry from the list's _Log.csv sidecar. Identify the entry by its fields (taskId/from/to/person/comment/hours) — these must match exactly one entry. Only the sidecar changes; a task's time spent is not adjusted. Returns the deleted entry.")]
    public TimeLogEntry DeleteTimeLogEntry(
        [Description("The entry's task ID (use 0 for a task-less entry). Omit to not match on task.")] int? taskId = null,
        [Description("The entry's period start (yyyy-MM-dd HH:mm or ISO 8601), matched to the minute.")] string? from = null,
        [Description("The entry's period end (yyyy-MM-dd HH:mm or ISO 8601), matched to the minute.")] string? to = null,
        [Description("The entry's person (case-insensitive, exact).")] string? person = null,
        [Description("The entry's comment (case-sensitive, exact).")] string? comment = null,
        [Description("The entry's logged hours.")] double? hours = null,
        [Description("Alias of the configured list. Omit to use the default list.")] string? list = null) =>
        _manager.DeleteLogEntry(list, new TimeLogSelector
        {
            TaskId = taskId,
            From = ParseDate(from),
            To = ParseDate(to),
            Person = person,
            Comment = comment,
            Hours = hours,
        });

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt;
        throw new ArgumentException($"Could not parse date '{value}'. Use yyyy-MM-dd or ISO 8601.");
    }

    /// <summary>
    /// Parses an inclusive upper-bound date. A date-only input (no time component) is taken as the
    /// end of that day, so a single-day range (e.g. since and until both "today") includes entries
    /// logged during the day rather than only ones starting exactly at midnight. An explicit time —
    /// including an explicit midnight — is honoured verbatim. Date-only is detected from the input
    /// itself (a time always carries a ':'), so an explicit "...00:00" is not mistaken for a bare
    /// date; the end-of-day is computed tick-wise so it can't overflow at DateTime.MaxValue.
    /// </summary>
    private static DateTime? ParseUpperBoundDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var dt = ParseDate(value)!.Value;
        return value.Contains(':') ? dt : dt.Date.AddTicks(TimeSpan.TicksPerDay - 1);
    }

    private static TimeUnit? ParseUnit(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (TimeUnits.TryParse(value, out var unit)) return unit;
        throw new ArgumentException(
            $"Unknown time unit '{value}' for {paramName}. Use minutes/hours/days/weekdays/weeks/months/years (or I/H/D/K/W/M/Y).");
    }

    private static CommentContentFormat ParseCommentsFormat(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return CommentContentFormat.Plain;
        if (CommentFormat.TryParseWritable(value, out var format)) return format;
        throw new ArgumentException(
            $"Unknown comment format '{value}'. Use plain, markdown, or html "
            + "(rich text and spreadsheet can't be authored here — edit them in ToDoList).");
    }
}
