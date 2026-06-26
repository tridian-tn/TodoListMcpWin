using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TodoListMcp.App;
using TodoListMcp.Core.Model;

namespace TodoListMcp.App.Tests;

/// <summary>
/// Covers the manager's time-log surface: where the sidecar lands, the default person, and the
/// optional TIMESPENT linkage that writes the .tdl and the _Log.csv together.
/// </summary>
public class LogTimeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tdlmcp_logtime_" + Guid.NewGuid().ToString("N"));
    private readonly string _tdl;
    private readonly string _logPath;

    public LogTimeTests()
    {
        Directory.CreateDirectory(_dir);
        _tdl = Path.Combine(_dir, "Tasks.tdl");
        _logPath = Path.Combine(_dir, "Tasks_Log.csv");
        File.WriteAllText(_tdl, MinimalTdl, Encoding.Unicode);
    }

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ } }

    private TodoListManager Manager()
    {
        var options = new TodoListMcpOptions { Files = { new TodoFileEntry { Alias = "work", Path = _tdl } } };
        return new TodoListManager(new StubOptionsMonitor(options), NullLogger<TodoListManager>.Instance);
    }

    [Fact]
    public void LogTime_creates_the_sidecar_beside_the_tdl_with_a_default_person()
    {
        var manager = Manager();
        var entry = manager.LogTime("work", new LogTimeRequest
        {
            TaskId = 1,
            Hours = 1.5,
            When = new DateTime(2026, 6, 17, 9, 30, 0),
            Comment = "did the thing",
        });

        Assert.True(File.Exists(_logPath), "expected the _Log.csv sidecar to be created");
        Assert.Equal(Environment.UserName, entry.Person);
        Assert.Equal("A", entry.TaskTitle); // snapshotted from the task
        Assert.Equal(new DateTime(2026, 6, 17, 8, 0, 0), entry.From); // when − hours
        Assert.Equal("Adjusted", entry.Type);

        var read = manager.ReadLog("work", new TimeLogQuery { TaskId = 1 });
        Assert.Single(read);
    }

    [Fact]
    public void LogTime_with_addToTimeSpent_increments_the_task_and_writes_both_files()
    {
        var manager = Manager();
        manager.LogTime("work", new LogTimeRequest
        {
            TaskId = 1,
            Hours = 2.0,
            Comment = "work",
            AddToTimeSpent = true,
        });

        // The .tdl task's TIMESPENT was incremented...
        var task = manager.Read("work", d => d.GetTask(1));
        Assert.Equal(2.0, task!.TimeSpent);
        Assert.Equal("hours", task.TimeSpentUnit);

        // ...and the sidecar row was written.
        var read = manager.ReadLog("work", new TimeLogQuery { TaskId = 1 });
        Assert.Equal(2.0, Assert.Single(read).Hours, 3);
    }

    [Fact]
    public void LogTime_against_a_missing_task_throws_and_writes_nothing()
    {
        var manager = Manager();
        Assert.Throws<TodoListMcp.Core.TaskNotFoundException>(() =>
            manager.LogTime("work", new LogTimeRequest { TaskId = 999, Hours = 1.0, Comment = "x" }));
        Assert.False(File.Exists(_logPath), "no sidecar should be written when the task is missing");
    }

    [Fact]
    public void LogTime_taskless_entry_does_not_need_a_task()
    {
        var manager = Manager();
        var entry = manager.LogTime("work", new LogTimeRequest { Hours = 0, Comment = "untracked thinking" });
        Assert.Equal(0, entry.TaskId);
        Assert.Equal("untracked thinking", entry.Comment);
        Assert.True(File.Exists(_logPath));
    }

    [Fact]
    public void LogTime_snapshots_the_task_ancestor_path()
    {
        var manager = Manager();
        // Task 2 is nested under task 1 ("A"), so its logged Path is "A\".
        var entry = manager.LogTime("work", new LogTimeRequest { TaskId = 2, Hours = 1.0, Comment = "child work" });
        Assert.Equal(@"A\", entry.Path);

        // And it round-trips through the sidecar.
        var read = Assert.Single(manager.ReadLog("work", new TimeLogQuery { TaskId = 2 }));
        Assert.Equal(@"A\", read.Path);
    }

    [Fact]
    public void LogTime_truncates_the_period_to_minute_precision()
    {
        var manager = Manager();
        // When omitted, the period derives from DateTime.Now — the persisted row stores only HH:mm,
        // so the returned entry must already be minute-aligned to match what a re-read yields.
        var entry = manager.LogTime("work", new LogTimeRequest { TaskId = 1, Hours = 0.5, Comment = "x" });
        Assert.Equal(0, entry.From.Second);
        Assert.Equal(0, entry.To.Second);

        var read = Assert.Single(manager.ReadLog("work", new TimeLogQuery { TaskId = 1 }));
        Assert.Equal(entry.From, read.From);
        Assert.Equal(entry.To, read.To);
    }

    private const string MinimalTdl =
        "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
        "<TODOLIST PROJECTNAME=\"T\" NEXTUNIQUEID=\"3\">" +
        "<TASK ID=\"1\" TITLE=\"A\" POS=\"0\" POSSTRING=\"1\">" +
        "<TASK ID=\"2\" TITLE=\"Child\" POS=\"0\" POSSTRING=\"1.1\"/></TASK></TODOLIST>";

    private sealed class StubOptionsMonitor : IOptionsMonitor<TodoListMcpOptions>
    {
        public StubOptionsMonitor(TodoListMcpOptions value) => CurrentValue = value;
        public TodoListMcpOptions CurrentValue { get; }
        public TodoListMcpOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<TodoListMcpOptions, string?> listener) => null;
    }
}
