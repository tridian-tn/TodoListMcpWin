using System.Text;
using TodoListMcp.Core;
using TodoListMcp.Core.Model;

namespace TodoListMcp.Core.Tests;

/// <summary>
/// Covers the time-log sidecar engine (<see cref="TimeLogDocument"/>): CSV format fidelity across
/// versions, delimiter detection, value encoding, filtering, validity, and the TIMESPENT increment.
/// </summary>
public class TimeLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tdlmcp_logtests_" + Guid.NewGuid().ToString("N"));

    public TimeLogTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ } }

    // A real-shape latest-version sample: a tracked task entry and a task-less comment-only entry.
    private const string LatestSample =
        "TODOTIMELOG VERSION 1\n" +
        "Task ID\tTitle\tUser ID\tStart Date\tStart Time\tEnd Date\tEnd Time\tTime Spent (Hrs)\tComment\tType\tPath\tColour\n" +
        "771\tFix bug\ttryst\t2025-01-27\t16:54\t2025-01-27\t17:17\t0.365\t\tTracked\tMPD-16\\\t\n" +
        "0\t\ttryst\t2026-01-12\t09:00\t2026-01-12\t16:00\t7.000\tSP DAY\t\t\t-65536\n";

    [Fact]
    public void Parses_latest_version_rows_including_a_taskless_entry()
    {
        var log = TimeLogDocument.Parse(LatestSample);
        var entries = log.Entries;
        Assert.Equal(2, entries.Count);

        var tracked = entries[0];
        Assert.Equal(771, tracked.TaskId);
        Assert.Equal("Fix bug", tracked.TaskTitle);
        Assert.Equal("tryst", tracked.Person);
        Assert.Equal(new DateTime(2025, 1, 27, 16, 54, 0), tracked.From);
        Assert.Equal(new DateTime(2025, 1, 27, 17, 17, 0), tracked.To);
        Assert.Equal(0.365, tracked.Hours, 3);
        Assert.Equal("Tracked", tracked.Type);
        Assert.Equal(@"MPD-16\", tracked.Path);

        var taskless = entries[1];
        Assert.Equal(0, taskless.TaskId);
        Assert.Equal("", taskless.TaskTitle);
        Assert.Equal("SP DAY", taskless.Comment);
        Assert.Null(taskless.Type); // ToDoList leaves Type blank on such rows
    }

    [Fact]
    public void Append_then_save_writes_utf16_bom_version_line_and_header()
    {
        var path = Path.Combine(_dir, "Tasks_Log.csv");
        var log = TimeLogDocument.Load(path); // missing file → empty log
        log.Append(new TimeLogEntry
        {
            TaskId = 5,
            TaskTitle = "Write tests",
            Person = "tryst",
            From = new DateTime(2026, 6, 17, 8, 0, 0),
            To = new DateTime(2026, 6, 17, 9, 30, 0),
            Hours = 1.5,
            Type = "Adjusted",
        });
        log.Save();

        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE, "expected UTF-16 LE BOM");

        var text = File.ReadAllText(path, Encoding.Unicode);
        Assert.StartsWith("TODOTIMELOG VERSION 1\n", text);
        Assert.Contains("Task ID\tTitle\tUser ID\t", text);
        Assert.Contains("Time Spent (Hrs)", text);
        Assert.Contains("Colour", text);
        // Hours are 3-dp; the period is the row's start/end split across date+time columns.
        Assert.Contains("5\tWrite tests\ttryst\t2026-06-17\t08:00\t2026-06-17\t09:30\t1.500\t", text);
        Assert.DoesNotContain("\r\n", text); // bare-LF line endings
    }

    [Fact]
    public void Round_trips_through_disk()
    {
        var path = Path.Combine(_dir, "Tasks_Log.csv");
        File.WriteAllText(path, LatestSample, new UnicodeEncoding(false, true));

        var log = TimeLogDocument.Load(path);
        log.Append(new TimeLogEntry
        {
            TaskId = 9,
            TaskTitle = "New",
            Person = "tryst",
            From = new DateTime(2026, 6, 20, 10, 0, 0),
            To = new DateTime(2026, 6, 20, 11, 0, 0),
            Hours = 1.0,
            Comment = "did a thing",
            Type = "Adjusted",
        });
        log.Save();

        var reloaded = TimeLogDocument.Load(path);
        Assert.Equal(3, reloaded.Entries.Count);
        var added = reloaded.Entries[2];
        Assert.Equal(9, added.TaskId);
        Assert.Equal("did a thing", added.Comment);
        Assert.Equal(1.0, added.Hours, 3);
    }

    [Fact]
    public void Existing_rows_are_preserved_verbatim_on_append()
    {
        // The legacy Colour value (-65536) is not modelled on write, but must survive an append.
        var log = TimeLogDocument.Parse(LatestSample);
        log.Append(new TimeLogEntry
        {
            TaskTitle = "", Person = "tryst", Hours = 0, Comment = "note only",
            From = new DateTime(2026, 6, 21), To = new DateTime(2026, 6, 21), Type = "Adjusted",
        });
        var text = log.ToText();
        Assert.Contains("-65536", text);
        Assert.Contains("SP DAY", text);
    }

    [Fact]
    public void Detects_comma_delimiter_on_read()
    {
        var commaCsv =
            "TODOTIMELOG VERSION 1\n" +
            "Task ID,Title,User ID,Start Date,Start Time,End Date,End Time,Time Spent (Hrs),Comment,Type,Path,Colour\n" +
            "12,Comma task,bob,2026-02-01,09:00,2026-02-01,10:30,1.500,,Adjusted,,\n";
        var log = TimeLogDocument.Parse(commaCsv);
        var e = Assert.Single(log.Entries);
        Assert.Equal(12, e.TaskId);
        Assert.Equal("Comma task", e.TaskTitle);
        Assert.Equal(1.5, e.Hours, 3);
    }

    [Fact]
    public void Detects_semicolon_delimiter_on_read()
    {
        var semiCsv =
            "TODOTIMELOG VERSION 1\n" +
            "Task ID;Title;User ID;Start Date;Start Time;End Date;End Time;Time Spent (Hrs);Comment;Type;Path;Colour\n" +
            "7;Semi task;bob;2026-03-01;09:00;2026-03-01;10:00;1.000;;Adjusted;;\n";
        var log = TimeLogDocument.Parse(semiCsv);
        var e = Assert.Single(log.Entries);
        Assert.Equal(7, e.TaskId);
        Assert.Equal("Semi task", e.TaskTitle);
        Assert.Equal(1.0, e.Hours, 3);
    }

    [Fact]
    public void Encodes_and_decodes_a_comment_with_delimiter_and_newline()
    {
        var path = Path.Combine(_dir, "Tasks_Log.csv");
        var log = TimeLogDocument.Load(path);
        var comment = "line one\nhas a\ttab and a comma,too";
        log.Append(new TimeLogEntry
        {
            TaskId = 3, TaskTitle = "Enc", Person = "tryst",
            From = new DateTime(2026, 6, 1, 9, 0, 0), To = new DateTime(2026, 6, 1, 10, 0, 0),
            Hours = 1.0, Comment = comment, Type = "Adjusted",
        });
        log.Save();

        // On disk: the tab-containing field is quoted, and the newline is encoded as a pipe.
        var raw = File.ReadAllText(path, Encoding.Unicode);
        Assert.Contains("\"line one|has a\ttab and a comma,too\"", raw);

        var reloaded = TimeLogDocument.Load(path);
        Assert.Equal(comment, reloaded.Entries[0].Comment);
    }

    [Fact]
    public void Reads_and_round_trips_a_legacy_ver0_row()
    {
        // A 6-field legacy row: ID, Title, Hours, Person, To, From.
        var ver0 =
            "Task ID\tTitle\tTime\tPerson\tTo\tFrom\n" +
            "42\tOld entry\t2.500\tbob\t2020-01-02\t2020-01-01\n";
        var log = TimeLogDocument.Parse(ver0);
        var e = Assert.Single(log.Entries);
        Assert.Equal(42, e.TaskId);
        Assert.Equal("Old entry", e.TaskTitle);
        Assert.Equal(2.5, e.Hours, 3);
        Assert.Equal("bob", e.Person);

        // Appending must leave the legacy row byte-for-byte (re-emitted from its original text).
        log.Append(new TimeLogEntry
        {
            TaskId = 1, TaskTitle = "New", Person = "tryst", Hours = 1,
            From = new DateTime(2026, 1, 1), To = new DateTime(2026, 1, 1), Comment = "x",
        });
        Assert.Contains("42\tOld entry\t2.500\tbob\t2020-01-02\t2020-01-01", log.ToText());
    }

    [Fact]
    public void Read_filters_by_task_person_and_date_range()
    {
        var log = TimeLogDocument.Parse(LatestSample);

        Assert.Single(log.Read(new TimeLogQuery { TaskId = 771 }));
        Assert.Single(log.Read(new TimeLogQuery { TaskId = 0 })); // task-less
        Assert.Equal(2, log.Read(new TimeLogQuery { Person = "TRYST" }).Count); // case-insensitive
        Assert.Single(log.Read(new TimeLogQuery { Since = new DateTime(2026, 1, 1) }));
        Assert.Single(log.Read(new TimeLogQuery { Until = new DateTime(2025, 12, 31) }));
    }

    [Fact]
    public void Append_rejects_an_entry_with_no_comment_and_no_hours()
    {
        var log = TimeLogDocument.Parse(LatestSample);
        Assert.Throws<ArgumentException>(() => log.Append(new TimeLogEntry
        {
            TaskId = 1, Hours = 0, Comment = null,
            From = new DateTime(2026, 1, 1), To = new DateTime(2026, 1, 1),
        }));
    }

    [Fact]
    public void Append_rejects_hours_without_a_set_period()
    {
        // Hours alone, with unset (default) From/To, is not a valid period — mirrors the source rule.
        var log = TimeLogDocument.Load(Path.Combine(_dir, "Tasks_Log.csv"));
        Assert.Throws<ArgumentException>(() => log.Append(new TimeLogEntry
        {
            TaskId = 1, Hours = 1.5, Comment = null, // From/To left at default
        }));
    }

    [Fact]
    public void Append_allows_a_taskless_comment_only_entry()
    {
        var log = TimeLogDocument.Load(Path.Combine(_dir, "Tasks_Log.csv"));
        log.Append(new TimeLogEntry
        {
            TaskId = 0, Hours = 0, Comment = "thinking time",
            From = new DateTime(2026, 1, 1), To = new DateTime(2026, 1, 1), Type = "Adjusted",
        });
        Assert.True(log.IsDirty);
        Assert.Equal("thinking time", Assert.Single(log.Entries).Comment);
    }

    [Fact]
    public void Degenerate_files_synthesise_a_proper_header_on_save()
    {
        // A BOM-only (empty) file and a version-line-only file must not re-emit a blank header line;
        // a save synthesises the latest header instead.
        foreach (var content in new[] { "", "TODOTIMELOG VERSION 1\n" })
        {
            var path = Path.Combine(_dir, $"deg_{content.Length}_Log.csv");
            File.WriteAllText(path, content, new UnicodeEncoding(false, true));

            var log = TimeLogDocument.Load(path);
            log.Append(new TimeLogEntry
            {
                TaskId = 1, TaskTitle = "T", Person = "tryst", Hours = 1,
                From = new DateTime(2026, 1, 1, 9, 0, 0), To = new DateTime(2026, 1, 1, 10, 0, 0),
                Comment = "x", Type = "Adjusted",
            });
            log.Save();

            var text = File.ReadAllText(path, Encoding.Unicode);
            Assert.StartsWith("TODOTIMELOG VERSION 1\n", text);
            Assert.Contains("Task ID\tTitle\tUser ID\t", text);
            Assert.DoesNotContain("\n\n", text); // no blank header line
            Assert.Single(TimeLogDocument.Load(path).Entries);
        }
    }

    [Fact]
    public void Missing_file_loads_as_empty_log()
    {
        var log = TimeLogDocument.Load(Path.Combine(_dir, "nope_Log.csv"));
        Assert.Empty(log.Entries);
        Assert.False(log.IsDirty);
    }

    // ---- Editing / deleting -----------------------------------------------

    [Fact]
    public void Update_changes_matched_entry_and_keeps_unset_fields()
    {
        var log = TimeLogDocument.Parse(LatestSample);
        var updated = log.Update(
            new TimeLogSelector { TaskId = 771 },
            new TimeLogEdit { From = new DateTime(2025, 1, 27, 10, 0, 0), To = new DateTime(2025, 1, 27, 11, 0, 0) });

        Assert.Equal(new DateTime(2025, 1, 27, 10, 0, 0), updated.From);
        Assert.Equal(new DateTime(2025, 1, 27, 11, 0, 0), updated.To);
        Assert.Equal("Fix bug", updated.TaskTitle); // untouched fields preserved
        Assert.Equal("Tracked", updated.Type);
        Assert.Equal(@"MPD-16\", updated.Path);
        Assert.True(log.IsDirty);

        // The other row is untouched.
        Assert.Equal("SP DAY", log.Entries[1].Comment);
    }

    [Fact]
    public void Update_re_serialises_the_edited_row_in_the_latest_layout()
    {
        // Edit the legacy Colour-bearing task-less row; its verbatim raw (incl. -65536) is dropped
        // and the row is rewritten in the 12-column layout, while the other row stays verbatim.
        var log = TimeLogDocument.Parse(LatestSample);
        log.Update(
            new TimeLogSelector { TaskId = 0, Comment = "SP DAY" },
            new TimeLogEdit { Comment = "rest day" });

        var text = log.ToText();
        Assert.Contains("rest day", text);
        Assert.DoesNotContain("SP DAY", text);
        Assert.DoesNotContain("-65536", text); // the dropped legacy Colour value
        Assert.Contains(@"771	Fix bug	tryst	2025-01-27	16:54", text); // the other row, verbatim
    }

    [Fact]
    public void Update_can_clear_a_comment_via_empty_string()
    {
        // Editing the timed task row (a valid period keeps it valid); an empty string clears the comment.
        var log = TimeLogDocument.Parse(LatestSample);
        var updated = log.Update(
            new TimeLogSelector { TaskId = 771 },
            new TimeLogEdit { Comment = "" });
        Assert.Null(updated.Comment);
    }

    [Fact]
    public void Update_rejects_an_edit_that_leaves_the_entry_invalid()
    {
        // A comment-only, zero-hour entry is valid only by virtue of its comment; clearing it is refused.
        var log = TimeLogDocument.Load(Path.Combine(_dir, "Tasks_Log.csv"));
        log.Append(new TimeLogEntry
        {
            TaskId = 0, Hours = 0, Comment = "note only",
            From = new DateTime(2026, 2, 1), To = new DateTime(2026, 2, 1), Type = "Adjusted",
        });
        Assert.Throws<ArgumentException>(() => log.Update(
            new TimeLogSelector { Comment = "note only" },
            new TimeLogEdit { Comment = "" }));
    }

    [Fact]
    public void Update_persists_across_a_disk_round_trip()
    {
        var path = Path.Combine(_dir, "Tasks_Log.csv");
        File.WriteAllText(path, LatestSample, new UnicodeEncoding(false, true));

        var log = TimeLogDocument.Load(path);
        log.Update(
            new TimeLogSelector { TaskId = 771 },
            new TimeLogEdit { Comment = "now with a note" });
        log.Save();

        var reloaded = TimeLogDocument.Load(path);
        Assert.Equal(2, reloaded.Entries.Count);
        Assert.Equal("now with a note", reloaded.Read(new TimeLogQuery { TaskId = 771 })[0].Comment);
    }

    [Fact]
    public void Delete_removes_the_matched_entry()
    {
        var log = TimeLogDocument.Parse(LatestSample);
        var removed = log.Delete(new TimeLogSelector { TaskId = 771 });

        Assert.Equal(771, removed.TaskId);
        Assert.True(log.IsDirty);
        var remaining = Assert.Single(log.Entries);
        Assert.Equal(0, remaining.TaskId); // only the task-less row is left
    }

    [Fact]
    public void Delete_then_save_drops_the_row_on_disk()
    {
        var path = Path.Combine(_dir, "Tasks_Log.csv");
        File.WriteAllText(path, LatestSample, new UnicodeEncoding(false, true));

        var log = TimeLogDocument.Load(path);
        log.Delete(new TimeLogSelector { TaskId = 0, Comment = "SP DAY" });
        log.Save();

        var reloaded = TimeLogDocument.Load(path);
        Assert.Equal(771, Assert.Single(reloaded.Entries).TaskId);
    }

    [Fact]
    public void Selector_matching_no_entry_throws_not_found()
    {
        var log = TimeLogDocument.Parse(LatestSample);
        Assert.Throws<TimeLogEntryNotFoundException>(() => log.Delete(new TimeLogSelector { TaskId = 999 }));
        Assert.Throws<TimeLogEntryNotFoundException>(() => log.Update(
            new TimeLogSelector { TaskId = 999 }, new TimeLogEdit { Comment = "x" }));
    }

    [Fact]
    public void Ambiguous_selector_throws_and_changes_nothing()
    {
        // Two rows share person "tryst"; selecting on person alone is ambiguous.
        var log = TimeLogDocument.Parse(LatestSample);
        var ex = Assert.Throws<AmbiguousTimeLogMatchException>(() =>
            log.Delete(new TimeLogSelector { Person = "tryst" }));
        Assert.Equal(2, ex.MatchCount);
        Assert.Equal(2, log.Entries.Count); // nothing removed
        Assert.False(log.IsDirty);
    }

    [Fact]
    public void Empty_selector_is_rejected()
    {
        var log = TimeLogDocument.Parse(LatestSample);
        Assert.Throws<ArgumentException>(() => log.Delete(new TimeLogSelector()));
    }

    [Fact]
    public void Update_with_no_changes_is_rejected()
    {
        var log = TimeLogDocument.Parse(LatestSample);
        Assert.Throws<ArgumentException>(() => log.Update(new TimeLogSelector { TaskId = 771 }, new TimeLogEdit()));
    }

    [Fact]
    public void Selector_matches_timestamps_to_the_minute()
    {
        // A selector From carrying seconds still matches the minute-precision stored row.
        var log = TimeLogDocument.Parse(LatestSample);
        var removed = log.Delete(new TimeLogSelector { From = new DateTime(2025, 1, 27, 16, 54, 38) });
        Assert.Equal(771, removed.TaskId);
    }

    [Fact]
    public void Update_then_save_writes_the_edited_row_in_layout_and_keeps_others_verbatim_on_disk()
    {
        var path = Path.Combine(_dir, "Tasks_Log.csv");
        File.WriteAllText(path, LatestSample, new UnicodeEncoding(false, true));

        var log = TimeLogDocument.Load(path);
        log.Update(
            new TimeLogSelector { TaskId = 771 },
            new TimeLogEdit
            {
                From = new DateTime(2025, 1, 27, 10, 0, 0),
                To = new DateTime(2025, 1, 27, 11, 0, 0),
                Comment = "moved",
            });
        log.Save();

        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE, "expected UTF-16 LE BOM");

        var text = File.ReadAllText(path, Encoding.Unicode);
        Assert.StartsWith("TODOTIMELOG VERSION 1\n", text);
        Assert.Contains("Task ID\tTitle\tUser ID\t", text); // header intact
        // The edited row is rewritten in the 12-column layout with the new period and comment, while
        // its unchanged columns (title/hours/type/path) survive.
        Assert.Contains("771\tFix bug\ttryst\t2025-01-27\t10:00\t2025-01-27\t11:00\t0.365\tmoved\tTracked\tMPD-16\\", text);
        // The untouched task-less row — including the legacy Colour value we don't model — is verbatim.
        Assert.Contains("0\t\ttryst\t2026-01-12\t09:00\t2026-01-12\t16:00\t7.000\tSP DAY\t\t\t-65536", text);
        Assert.DoesNotContain("\r\n", text);   // bare-LF endings
        Assert.DoesNotContain("\n\n", text);    // no blank/duplicated lines
    }

    [Fact]
    public void Delete_then_save_drops_only_the_matched_row_text_on_disk()
    {
        var path = Path.Combine(_dir, "Tasks_Log.csv");
        File.WriteAllText(path, LatestSample, new UnicodeEncoding(false, true));

        var log = TimeLogDocument.Load(path);
        log.Delete(new TimeLogSelector { TaskId = 771 });
        log.Save();

        var text = File.ReadAllText(path, Encoding.Unicode);
        Assert.StartsWith("TODOTIMELOG VERSION 1\n", text);
        Assert.Contains("Task ID\tTitle\tUser ID\t", text);
        Assert.DoesNotContain("Fix bug", text); // the deleted row is gone
        // The surviving row is verbatim, including the legacy Colour value.
        Assert.Contains("0\t\ttryst\t2026-01-12\t09:00\t2026-01-12\t16:00\t7.000\tSP DAY\t\t\t-65536", text);
        // Exactly one data row remains: version + header + one row + trailing newline → 3 newlines.
        Assert.Equal(3, text.Count(c => c == '\n'));
        Assert.DoesNotContain("\n\n", text);
    }

    [Fact]
    public void Delete_the_only_entry_leaves_a_valid_header_only_file()
    {
        var path = Path.Combine(_dir, "Tasks_Log.csv");
        var log = TimeLogDocument.Load(path);
        log.Append(new TimeLogEntry
        {
            TaskId = 5, TaskTitle = "Solo", Person = "tryst", Hours = 1, Comment = "x",
            From = new DateTime(2026, 1, 1, 9, 0, 0), To = new DateTime(2026, 1, 1, 10, 0, 0), Type = "Adjusted",
        });
        log.Save();

        // Re-load from disk, delete the sole entry, and persist the removal.
        var doc = TimeLogDocument.Load(path);
        doc.Delete(new TimeLogSelector { TaskId = 5 });
        doc.Save();

        var text = File.ReadAllText(path, Encoding.Unicode);
        Assert.StartsWith("TODOTIMELOG VERSION 1\n", text);
        Assert.Contains("Task ID\tTitle\tUser ID\t", text);
        Assert.DoesNotContain("Solo", text);
        Assert.DoesNotContain("\n\n", text); // header-only, no blank data line
        Assert.Empty(TimeLogDocument.Load(path).Entries);
    }

    [Fact]
    public void Update_truncates_new_times_to_minute_precision()
    {
        // The engine itself drops seconds, so a direct caller can't write a time the HH:mm row would
        // silently truncate (which would then fail to match on a later read).
        var log = TimeLogDocument.Parse(LatestSample);
        var updated = log.Update(
            new TimeLogSelector { TaskId = 771 },
            new TimeLogEdit { From = new DateTime(2025, 1, 27, 10, 30, 45) });
        Assert.Equal(new DateTime(2025, 1, 27, 10, 30, 0), updated.From);
    }

    [Fact]
    public void CountMatches_counts_entries_matching_the_selector()
    {
        var log = TimeLogDocument.Parse(LatestSample);
        Assert.Equal(1, log.CountMatches(new TimeLogSelector { TaskId = 771 }));
        Assert.Equal(2, log.CountMatches(new TimeLogSelector { Person = "tryst" }));
        Assert.Equal(0, log.CountMatches(new TimeLogSelector { TaskId = 999 }));
    }

    [Fact]
    public void CountMatches_rejects_an_empty_selector()
    {
        // Same contract as ResolveSingle: an empty selector is an error, not a match-everything count.
        var log = TimeLogDocument.Parse(LatestSample);
        Assert.Throws<ArgumentException>(() => log.CountMatches(new TimeLogSelector()));
    }

    [Fact]
    public void SaveAs_creates_missing_parent_directories()
    {
        // Separate-mode logs live in a <base>\ subfolder that may not exist yet on the first write.
        var nested = Path.Combine(_dir, "Tasks", "771_Log.csv");
        var log = TimeLogDocument.Load(nested); // missing → empty
        log.Append(new TimeLogEntry
        {
            TaskId = 771, TaskTitle = "T", Person = "tryst", Hours = 1, Comment = "x",
            From = new DateTime(2026, 1, 1, 9, 0, 0), To = new DateTime(2026, 1, 1, 10, 0, 0), Type = "Adjusted",
        });
        log.Save();

        Assert.True(File.Exists(nested));
        Assert.Single(TimeLogDocument.Load(nested).Entries);
    }

    [Fact]
    public void Update_treats_a_whitespace_only_comment_as_blank()
    {
        // A whitespace-only comment is normalised to null — so it both clears the comment and cannot
        // keep an otherwise-empty (zero-hour) entry valid, matching the append path's rule.
        var log = TimeLogDocument.Load(Path.Combine(_dir, "Tasks_Log.csv"));
        log.Append(new TimeLogEntry
        {
            TaskId = 0, Hours = 0, Comment = "note only",
            From = new DateTime(2026, 2, 1), To = new DateTime(2026, 2, 1), Type = "Adjusted",
        });
        Assert.Throws<ArgumentException>(() => log.Update(
            new TimeLogSelector { Comment = "note only" },
            new TimeLogEdit { Comment = "   " }));
    }
}
