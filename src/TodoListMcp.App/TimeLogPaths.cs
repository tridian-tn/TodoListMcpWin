namespace TodoListMcp.App;

/// <summary>
/// Resolves the on-disk layout of a list's time-log sidecar(s), mirroring ToDoList's
/// <c>CTDCTaskTimeLog::GetLogPath</c>. Two layouts exist:
///  - <b>Combined</b>: one <c>&lt;base&gt;_Log.csv</c> beside the <c>.tdl</c> (the default).
///  - <b>Separate</b>: a sibling folder named after the <c>.tdl</c> base, holding one
///    <c>&lt;taskID&gt;_Log.csv</c> per task (task-less entries land in <c>0_Log.csv</c>), gated by
///    ToDoList's global "log tasks separately" preference.
///
/// Only the <em>write</em> target depends on the mode; reads are mode-agnostic — ToDoList's analysis
/// unions the combined file and every per-task file, so <see cref="AllExisting"/> does the same.
/// </summary>
internal static class TimeLogPaths
{
    /// <summary>The combined sidecar path: <c>&lt;dir&gt;\&lt;base&gt;_Log.csv</c>.</summary>
    public static string Combined(string tdlPath)
    {
        var full = Path.GetFullPath(tdlPath);
        var dir = Path.GetDirectoryName(full) ?? "";
        return Path.Combine(dir, Path.GetFileNameWithoutExtension(full) + "_Log.csv");
    }

    /// <summary>The per-task folder in separate mode: <c>&lt;dir&gt;\&lt;base&gt;\</c>.</summary>
    public static string SeparateFolder(string tdlPath)
    {
        var full = Path.GetFullPath(tdlPath);
        var dir = Path.GetDirectoryName(full) ?? "";
        return Path.Combine(dir, Path.GetFileNameWithoutExtension(full));
    }

    /// <summary>A single task's per-task sidecar: <c>&lt;dir&gt;\&lt;base&gt;\&lt;taskID&gt;_Log.csv</c>.</summary>
    public static string Separate(string tdlPath, int taskId) =>
        Path.Combine(SeparateFolder(tdlPath), taskId + "_Log.csv");

    /// <summary>The file a new entry is written to, given the effective mode and its task ID.</summary>
    public static string WriteTarget(string tdlPath, LogMode mode, int taskId) =>
        mode == LogMode.Separate ? Separate(tdlPath, taskId) : Combined(tdlPath);

    /// <summary>
    /// True when any per-task <c>&lt;taskID&gt;_Log.csv</c> files exist for this list. Best-effort: it
    /// only backs the advisory layout-mismatch warning, so an inaccessible folder yields false rather
    /// than throwing and aborting a log write.
    /// </summary>
    public static bool SeparateFilesExist(string tdlPath)
    {
        var folder = SeparateFolder(tdlPath);
        if (!Directory.Exists(folder)) return false;
        try { return Directory.EnumerateFiles(folder, "*_Log.csv").Any(); }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// Every existing sidecar file for this list, in ToDoList's read order: the combined file first
    /// (if present), then each per-task file (ascending by name) from the separate folder. Missing
    /// files simply don't appear, so a list logged in either mode — or a mix — reads back in full.
    /// </summary>
    public static IReadOnlyList<string> AllExisting(string tdlPath)
    {
        var files = new List<string>();

        var combined = Combined(tdlPath);
        if (File.Exists(combined)) files.Add(combined);

        var folder = SeparateFolder(tdlPath);
        if (Directory.Exists(folder))
            files.AddRange(Directory.EnumerateFiles(folder, "*_Log.csv")
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase));

        return files;
    }
}
