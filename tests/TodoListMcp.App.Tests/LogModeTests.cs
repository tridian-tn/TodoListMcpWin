using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TodoListMcp.App;
using TodoListMcp.Core;
using TodoListMcp.Core.Model;

namespace TodoListMcp.App.Tests;

/// <summary>
/// Covers the per-task "log separately" mode: the on-disk path layout (<see cref="TimeLogPaths"/>),
/// mode-resolved writes, mode-agnostic read merge, edit/delete routing to the owning per-task file,
/// effective-mode resolution, and the layout-mismatch warning.
/// </summary>
public class LogModeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tdlmcp_logmode_" + Guid.NewGuid().ToString("N"));
    private readonly string _tdl;

    public LogModeTests()
    {
        Directory.CreateDirectory(_dir);
        _tdl = Path.Combine(_dir, "Tasks.tdl");
        File.WriteAllText(_tdl, MinimalTdl, Encoding.Unicode);
    }

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ } }

    private (TodoListManager mgr, CapturingLogger<TodoListManager> log) Manager(
        LogMode defaultMode = LogMode.Combined, LogMode? listOverride = null)
    {
        var options = new TodoListMcpOptions
        {
            DefaultLogMode = defaultMode,
            Files = { new TodoFileEntry { Alias = "work", Path = _tdl, LogMode = listOverride } },
        };
        var log = new CapturingLogger<TodoListManager>();
        return (new TodoListManager(new StubOptionsMonitor(options), log), log);
    }

    // ---- Path layout -------------------------------------------------------

    [Fact]
    public void TimeLogPaths_resolves_the_combined_and_separate_layout()
    {
        Assert.Equal(Path.Combine(_dir, "Tasks_Log.csv"), TimeLogPaths.Combined(_tdl));
        Assert.Equal(Path.Combine(_dir, "Tasks"), TimeLogPaths.SeparateFolder(_tdl));
        Assert.Equal(Path.Combine(_dir, "Tasks", "771_Log.csv"), TimeLogPaths.Separate(_tdl, 771));
        Assert.Equal(Path.Combine(_dir, "Tasks", "0_Log.csv"), TimeLogPaths.Separate(_tdl, 0)); // task-less
        Assert.Equal(TimeLogPaths.Combined(_tdl), TimeLogPaths.WriteTarget(_tdl, LogMode.Combined, 5));
        Assert.Equal(TimeLogPaths.Separate(_tdl, 5), TimeLogPaths.WriteTarget(_tdl, LogMode.Separate, 5));
    }

    [Fact]
    public void AllExisting_unions_the_combined_file_and_per_task_files()
    {
        File.WriteAllText(TimeLogPaths.Combined(_tdl), "x", Encoding.Unicode);
        Directory.CreateDirectory(TimeLogPaths.SeparateFolder(_tdl));
        File.WriteAllText(TimeLogPaths.Separate(_tdl, 1), "x", Encoding.Unicode);
        File.WriteAllText(TimeLogPaths.Separate(_tdl, 2), "x", Encoding.Unicode);

        var all = TimeLogPaths.AllExisting(_tdl);
        Assert.Equal(3, all.Count);
        Assert.Equal(TimeLogPaths.Combined(_tdl), all[0]); // combined first, as ToDoList reads
        Assert.Contains(TimeLogPaths.Separate(_tdl, 1), all);
        Assert.Contains(TimeLogPaths.Separate(_tdl, 2), all);
    }

    // ---- Effective mode ----------------------------------------------------

    [Fact]
    public void EffectiveLogMode_prefers_the_list_override_then_the_global_default()
    {
        var (combinedDefault, _) = Manager(LogMode.Combined);
        Assert.Equal(LogMode.Combined, combinedDefault.EffectiveLogMode(combinedDefault.Files[0]));

        var (globalSeparate, _) = Manager(LogMode.Separate);
        Assert.Equal(LogMode.Separate, globalSeparate.EffectiveLogMode(globalSeparate.Files[0]));

        var (overridden, _) = Manager(LogMode.Combined, listOverride: LogMode.Separate);
        Assert.Equal(LogMode.Separate, overridden.EffectiveLogMode(overridden.Files[0]));
    }

    // ---- Writes ------------------------------------------------------------

    [Fact]
    public void LogTime_in_separate_mode_writes_a_per_task_file_not_the_combined_one()
    {
        var (mgr, _) = Manager(LogMode.Separate);
        mgr.LogTime("work", new LogTimeRequest { TaskId = 1, Hours = 1, Comment = "x" });

        Assert.True(File.Exists(TimeLogPaths.Separate(_tdl, 1)), "expected Tasks\\1_Log.csv");
        Assert.False(File.Exists(TimeLogPaths.Combined(_tdl)), "combined sidecar must not be written");
        Assert.Equal(1, Assert.Single(mgr.ReadLog("work", new TimeLogQuery())).TaskId);
    }

    [Fact]
    public void LogTime_in_separate_mode_routes_a_taskless_entry_to_zero_log()
    {
        var (mgr, _) = Manager(LogMode.Separate);
        mgr.LogTime("work", new LogTimeRequest { Hours = 0, Comment = "no task" });
        Assert.True(File.Exists(TimeLogPaths.Separate(_tdl, 0)), "expected Tasks\\0_Log.csv");
    }

    // ---- Read merge --------------------------------------------------------

    [Fact]
    public void ReadLog_merges_entries_written_in_both_layouts()
    {
        // One entry in the combined file, one in a per-task file — a mixed on-disk state.
        var (combined, _) = Manager(LogMode.Combined);
        combined.LogTime("work", new LogTimeRequest { TaskId = 1, Hours = 1, Comment = "combined one" });

        var (separate, _) = Manager(LogMode.Separate);
        separate.LogTime("work", new LogTimeRequest { TaskId = 2, Hours = 2, Comment = "separate one" });

        Assert.True(File.Exists(TimeLogPaths.Combined(_tdl)));
        Assert.True(File.Exists(TimeLogPaths.Separate(_tdl, 2)));

        var all = separate.ReadLog("work", new TimeLogQuery());
        Assert.Equal(2, all.Count);
        Assert.Contains(all, e => e.Comment == "combined one");
        Assert.Contains(all, e => e.Comment == "separate one");
    }

    // ---- Edit / delete routing --------------------------------------------

    [Fact]
    public void UpdateLogEntry_in_separate_mode_edits_only_the_owning_per_task_file()
    {
        var (mgr, _) = Manager(LogMode.Separate);
        mgr.LogTime("work", new LogTimeRequest { TaskId = 1, Hours = 1, Comment = "one" });
        mgr.LogTime("work", new LogTimeRequest { TaskId = 2, Hours = 2, Comment = "two" });

        var beforeTask1 = File.ReadAllText(TimeLogPaths.Separate(_tdl, 1), Encoding.Unicode);

        mgr.UpdateLogEntry("work",
            new TimeLogSelector { TaskId = 2, Comment = "two" },
            new TimeLogEdit { Comment = "two-edited" });

        // Task 2's file changed; task 1's is byte-for-byte identical.
        Assert.Contains("two-edited", File.ReadAllText(TimeLogPaths.Separate(_tdl, 2), Encoding.Unicode));
        Assert.Equal(beforeTask1, File.ReadAllText(TimeLogPaths.Separate(_tdl, 1), Encoding.Unicode));

        var task2 = Assert.Single(mgr.ReadLog("work", new TimeLogQuery { TaskId = 2 }));
        Assert.Equal("two-edited", task2.Comment);
    }

    [Fact]
    public void DeleteLogEntry_in_separate_mode_removes_from_the_owning_file()
    {
        var (mgr, _) = Manager(LogMode.Separate);
        mgr.LogTime("work", new LogTimeRequest { TaskId = 1, Hours = 1, Comment = "one" });
        mgr.LogTime("work", new LogTimeRequest { TaskId = 2, Hours = 2, Comment = "two" });

        mgr.DeleteLogEntry("work", new TimeLogSelector { TaskId = 2, Comment = "two" });

        Assert.Empty(mgr.ReadLog("work", new TimeLogQuery { TaskId = 2 }));
        Assert.Single(mgr.ReadLog("work", new TimeLogQuery { TaskId = 1 })); // untouched
    }

    [Fact]
    public void Edit_ambiguous_across_per_task_files_throws_and_changes_nothing()
    {
        // The same from/to/comment logged against two tasks → selecting on those alone is ambiguous,
        // even though the matches live in different files.
        var (mgr, _) = Manager(LogMode.Separate);
        var from = new DateTime(2026, 6, 1, 9, 0, 0);
        var to = new DateTime(2026, 6, 1, 10, 0, 0);
        mgr.LogTime("work", new LogTimeRequest { TaskId = 1, Hours = 1, From = from, To = to, Comment = "dup" });
        mgr.LogTime("work", new LogTimeRequest { TaskId = 2, Hours = 1, From = from, To = to, Comment = "dup" });

        var ex = Assert.Throws<AmbiguousTimeLogMatchException>(() =>
            mgr.DeleteLogEntry("work", new TimeLogSelector { Comment = "dup" }));
        Assert.Equal(2, ex.MatchCount);
        Assert.Equal(2, mgr.ReadLog("work", new TimeLogQuery()).Count); // nothing removed
    }

    [Fact]
    public void Edit_with_no_matching_entry_throws_not_found()
    {
        var (mgr, _) = Manager(LogMode.Separate);
        mgr.LogTime("work", new LogTimeRequest { TaskId = 1, Hours = 1, Comment = "one" });
        Assert.Throws<TimeLogEntryNotFoundException>(() =>
            mgr.DeleteLogEntry("work", new TimeLogSelector { Comment = "nope" }));
    }

    // ---- Mismatch warning --------------------------------------------------

    [Fact]
    public void LogTime_warns_when_the_on_disk_layout_disagrees_with_the_mode()
    {
        // A per-task file exists, but the list is configured Combined: the new entry lands in the
        // combined file, and a warning flags the discrepancy.
        Directory.CreateDirectory(TimeLogPaths.SeparateFolder(_tdl));
        File.WriteAllText(TimeLogPaths.Separate(_tdl, 1),
            "TODOTIMELOG VERSION 1\n", new UnicodeEncoding(false, true));

        var (mgr, log) = Manager(LogMode.Combined);
        mgr.LogTime("work", new LogTimeRequest { TaskId = 1, Hours = 1, Comment = "x" });

        Assert.True(File.Exists(TimeLogPaths.Combined(_tdl)));
        Assert.Contains(log.Warnings, w => w.Contains("per-task time-log files") && w.Contains("Combined"));
    }

    [Fact]
    public void LogTime_in_combined_mode_with_no_per_task_files_does_not_warn()
    {
        var (mgr, log) = Manager(LogMode.Combined);
        mgr.LogTime("work", new LogTimeRequest { TaskId = 1, Hours = 1, Comment = "x" });
        Assert.Empty(log.Warnings);
    }

    private const string MinimalTdl =
        "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
        "<TODOLIST PROJECTNAME=\"T\" NEXTUNIQUEID=\"3\">" +
        "<TASK ID=\"1\" TITLE=\"A\" POS=\"0\" POSSTRING=\"1\"/>" +
        "<TASK ID=\"2\" TITLE=\"B\" POS=\"1\" POSSTRING=\"2\"/></TODOLIST>";

    private sealed class StubOptionsMonitor : IOptionsMonitor<TodoListMcpOptions>
    {
        public StubOptionsMonitor(TodoListMcpOptions value) => CurrentValue = value;
        public TodoListMcpOptions CurrentValue { get; }
        public TodoListMcpOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<TodoListMcpOptions, string?> listener) => null;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Warnings.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
