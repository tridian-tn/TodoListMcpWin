using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using TodoListMcp.Core.Model;

namespace TodoListMcp.Core;

/// <summary>
/// Reads and writes AbstractSpoon ToDoList (.tdl) files with format fidelity.
///
/// Key format facts honoured here:
///  - Files are UTF-16 (LE, with BOM) and declare encoding="utf-16".
///  - Dates are OLE-automation serials (days since 1899-12-30) → DateTime.FromOADate / ToOADate.
///  - PRIORITY is a 0–10 scale; -2 means "no priority".
///  - Notes live in a &lt;COMMENTS&gt; child element (COMMENTSTYPE="PLAIN_TEXT"), not an attribute.
///  - Assignees (&lt;PERSON&gt;), categories (&lt;CATEGORY&gt;) and file links (&lt;FILEREFPATH&gt;) are
///    multi-value child elements. ToDoList writes them as repeated child elements even for a single
///    value, so writes always emit elements to match its on-disk format; the single-value attribute
///    form (e.g. ALLOCATEDTO="x") is only a legacy form still accepted on read.
///  - A task is "done" when it has a DONEDATE.
///
/// Mutations operate directly on the loaded XML tree so that attributes and elements this
/// class does not understand are preserved across a load/save round-trip.
/// </summary>
public sealed class TodoListDocument
{
    private readonly XDocument _doc;
    private readonly XElement _root;
    private readonly IClock _clock;

    /// <summary>Path the document was loaded from, if any.</summary>
    public string? FilePath { get; private set; }

    /// <summary>Value written to LASTMODBY on mutated tasks.</summary>
    public string ModifiedBy { get; set; } = "TodoListMcp";

    /// <summary>
    /// True once a mutation has actually changed the document since it was loaded. Callers use
    /// this to skip rewriting the file when an operation was a no-op (e.g. deleting a missing ID).
    /// </summary>
    public bool IsDirty { get; private set; }

    private TodoListDocument(XDocument doc, IClock clock, string? path)
    {
        _doc = doc;
        _root = doc.Root ?? throw new InvalidTdlException("The file contains no root element.");
        if (_root.Name.LocalName != "TODOLIST")
            throw new InvalidTdlException($"Unexpected root element <{_root.Name.LocalName}>; expected <TODOLIST>.");
        _clock = clock;
        FilePath = path;
        // Normalise the declaration so a save always advertises utf-16.
        _doc.Declaration = new XDeclaration("1.0", "utf-16", null);
    }

    // ---- Loading / saving --------------------------------------------------

    /// <summary>Loads a .tdl file from disk (encoding auto-detected from the BOM).</summary>
    public static TodoListDocument Load(string path, IClock? clock = null)
    {
        XDocument doc;
        try
        {
            using var stream = File.OpenRead(path);
            doc = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException ex)
        {
            throw new InvalidTdlException($"'{path}' is not valid XML: {ex.Message}", ex);
        }
        return new TodoListDocument(doc, clock ?? SystemClock.Instance, path);
    }

    /// <summary>Parses a .tdl document from an in-memory string (used by tests).</summary>
    public static TodoListDocument Parse(string xml, IClock? clock = null)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException ex)
        {
            throw new InvalidTdlException($"Document is not valid XML: {ex.Message}", ex);
        }
        return new TodoListDocument(doc, clock ?? SystemClock.Instance, null);
    }

    /// <summary>Saves back to <see cref="FilePath"/> atomically (temp file + replace).</summary>
    public void Save()
    {
        if (FilePath is null)
            throw new InvalidOperationException("This document was not loaded from a file; use SaveAs.");
        SaveAs(FilePath);
    }

    /// <summary>Saves to the given path atomically, preserving UTF-16 encoding and BOM.</summary>
    public void SaveAs(string path)
    {
        var tmp = path + ".tmp";
        WriteToFile(tmp);
        if (File.Exists(path))
            File.Replace(tmp, path, null);
        else
            File.Move(tmp, path);
        FilePath = path;
    }

    private void WriteToFile(string path)
    {
        var settings = new XmlWriterSettings
        {
            // UTF-16 little-endian, with BOM — matches what ToDoList writes.
            Encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
            Indent = false,
            OmitXmlDeclaration = false,
        };
        using var stream = File.Create(path);
        using var writer = XmlWriter.Create(stream, settings);
        _doc.Save(writer);
    }

    /// <summary>Serialises to an XML string (UTF-16 declaration); used by tests and diagnostics.</summary>
    public string ToXmlString()
    {
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = false };
        using (var writer = XmlWriter.Create(sb, settings))
            _doc.Save(writer);
        return sb.ToString();
    }

    // ---- Reading -----------------------------------------------------------

    public string? ProjectName => (string?)_root.Attribute("PROJECTNAME");

    public int? NextUniqueId => (int?)_root.Attribute("NEXTUNIQUEID");

    /// <summary>All top-level tasks with their full subtree.</summary>
    public IReadOnlyList<TodoTask> GetTasks() =>
        _root.Elements("TASK").Select(e => Project(e)).ToList();

    /// <summary>A single task (with its subtree), or null if not found.</summary>
    public TodoTask? GetTask(int id)
    {
        var e = FindTaskElement(id);
        return e is null ? null : Project(e);
    }

    /// <summary>Flat list of tasks matching every supplied criterion.</summary>
    public IReadOnlyList<TodoTask> Search(TaskQuery query)
    {
        var results = new List<TodoTask>();
        foreach (var e in _root.Descendants("TASK"))
        {
            var t = Project(e, includeSubtasks: false);
            if (Matches(t, query))
                results.Add(t);
        }
        return results;
    }

    private static bool Matches(TodoTask t, TaskQuery q)
    {
        if (!string.IsNullOrWhiteSpace(q.Text))
        {
            var needle = q.Text.Trim();
            var inTitle = t.Title.Contains(needle, StringComparison.OrdinalIgnoreCase);
            var inComments = t.Comments?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false;
            if (!inTitle && !inComments) return false;
        }
        if (!string.IsNullOrWhiteSpace(q.Category) &&
            !t.Categories.Any(c => c.Equals(q.Category.Trim(), StringComparison.OrdinalIgnoreCase)))
            return false;
        if (!string.IsNullOrWhiteSpace(q.Person) &&
            !t.AllocatedTo.Any(p => p.Equals(q.Person.Trim(), StringComparison.OrdinalIgnoreCase)))
            return false;
        if (q.Completed is bool done && t.IsDone != done) return false;
        if (q.Flagged is bool flagged && t.IsFlagged != flagged) return false;
        if (q.MinPriority is int min && (t.Priority ?? -1) < min) return false;
        if (q.MinRisk is int minRisk && (t.Risk ?? -1) < minRisk) return false;
        if (!string.IsNullOrWhiteSpace(q.Status) &&
            !string.Equals(t.Status, q.Status.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(q.ExternalId) &&
            !string.Equals(t.ExternalId, q.ExternalId.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(q.Version) &&
            !string.Equals(t.Version, q.Version.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(q.AllocatedBy) &&
            !string.Equals(t.AllocatedBy, q.AllocatedBy.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        var estHours = InHours(t.TimeEstimate, t.TimeEstimateUnit);
        if (q.MinEstimateHours is double minE && (estHours is null || estHours < minE)) return false;
        if (q.MaxEstimateHours is double maxE && (estHours is null || estHours > maxE)) return false;

        var spentHours = InHours(t.TimeSpent, t.TimeSpentUnit);
        if (q.MinSpentHours is double minS && (spentHours is null || spentHours < minS)) return false;
        if (q.MaxSpentHours is double maxS && (spentHours is null || spentHours > maxS)) return false;
        return true;
    }

    private TodoTask Project(XElement e, bool includeSubtasks = true) => new()
    {
        Id = (int?)e.Attribute("ID") ?? 0,
        Title = (string?)e.Attribute("TITLE") ?? "",
        ExternalId = TrimToNull((string?)e.Attribute("EXTERNALID")),
        Comments = ReadComments(e),
        CommentsSource = ReadCommentsSource(e),
        CommentsFormat = ReadCommentsFormat(e),
        Priority = ReadScale(e, "PRIORITY"),
        Risk = ReadScale(e, "RISK"),
        Status = TrimToNull((string?)e.Attribute("STATUS")),
        Version = TrimToNull((string?)e.Attribute("VERSION")),
        IsFlagged = (string?)e.Attribute("FLAG") == "1",
        IsLocked = IsTaskLocked(e),
        PercentDone = (int?)e.Attribute("PERCENTDONE") ?? 0,
        TimeEstimate = ReadTime(e, "TIMEESTIMATE"),
        TimeEstimateUnit = ReadTime(e, "TIMEESTIMATE") is null
            ? null : TimeUnits.ToWord(ReadTimeUnit(e, "TIMEESTUNITS")),
        TimeSpent = ReadTime(e, "TIMESPENT"),
        TimeSpentUnit = ReadTime(e, "TIMESPENT") is null
            ? null : TimeUnits.ToWord(ReadTimeUnit(e, "TIMESPENTUNITS")),
        IsDone = e.Attribute("DONEDATE") is { } d && !string.IsNullOrWhiteSpace(d.Value),
        IsGoodAsDone = (string?)e.Attribute("GOODASDONE") == "1",
        DueDate = ReadOaDate(e, "DUEDATE"),
        StartDate = ReadOaDate(e, "STARTDATE"),
        DoneDate = ReadOaDate(e, "DONEDATE"),
        CreationDate = ReadOaDate(e, "CREATIONDATE"),
        LastModified = ReadOaDate(e, "LASTMOD"),
        Categories = ReadMulti(e, "CATEGORY", "CATEGORY"),
        AllocatedTo = ReadMulti(e, "ALLOCATEDTO", "PERSON"),
        AllocatedBy = TrimToNull((string?)e.Attribute("ALLOCATEDBY")),
        FileLinks = ReadMulti(e, "FILEREFPATH", "FILEREFPATH", trim: false),
        Position = (string?)e.Attribute("POSSTRING") ?? "",
        Subtasks = includeSubtasks
            ? e.Elements("TASK").Select(child => Project(child)).ToList()
            : Array.Empty<TodoTask>(),
    };

    private static string? ReadComments(XElement e)
    {
        var child = e.Element("COMMENTS");
        if (child is not null) return child.Value;
        return (string?)e.Attribute("COMMENTS");
    }

    /// <summary>
    /// Reads the comment format (COMMENTSTYPE) as a friendly name; "plain" when comments exist
    /// without an explicit type, and null when the task has no comments at all.
    /// </summary>
    private static string? ReadCommentsFormat(XElement e)
    {
        var type = (string?)e.Attribute("COMMENTSTYPE");
        if (!string.IsNullOrWhiteSpace(type)) return CommentFormat.ToFriendly(type);
        // No explicit type: a rich <CUSTOMCOMMENTS> payload means formatted-but-unknown, not plain
        // (and HasFormattedComments agrees, so the guard and the reported format stay consistent).
        if (e.Element("CUSTOMCOMMENTS") is not null) return CommentFormat.Unknown;
        var hasComments = e.Element("COMMENTS") is not null || e.Attribute("COMMENTS") is not null;
        return hasComments ? CommentFormat.PlainText : null;
    }

    /// <summary>
    /// Decodes the editable comment source from &lt;CUSTOMCOMMENTS&gt;, but only for the formats this
    /// server can re-author (markdown/html) — making a read → edit → write round-trip lossless. Null
    /// for plain (use the &lt;COMMENTS&gt; mirror) and for rich/spreadsheet/unknown formats, whose
    /// payloads are opaque and can't be written back, so the mirror stays their only surfaced text.
    /// </summary>
    private static string? ReadCommentsSource(XElement e)
    {
        var format = ReadCommentsFormat(e);
        if (format is not (CommentFormat.Markdown or CommentFormat.Html)) return null;
        return CommentFormat.DecodeCustomComments(e.Element("CUSTOMCOMMENTS")?.Value);
    }

    /// <summary>
    /// True when a task carries formatted (non-plain-text) comments — a content-control type other
    /// than PLAIN_TEXT, or a &lt;CUSTOMCOMMENTS&gt; rich payload. Overwriting these via
    /// <see cref="SetComments"/> would discard the formatting, so <see cref="UpdateTask"/> refuses
    /// unless the caller opts in.
    /// </summary>
    private static bool HasFormattedComments(XElement e)
    {
        if (e.Element("CUSTOMCOMMENTS") is not null) return true;
        var type = (string?)e.Attribute("COMMENTSTYPE");
        return !string.IsNullOrWhiteSpace(type)
            && !string.Equals(type.Trim(), "PLAIN_TEXT", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads a 0–10 scale attribute (PRIORITY/RISK); ToDoList's -2 "unset" maps to null.</summary>
    private static int? ReadScale(XElement e, string name)
    {
        var p = (int?)e.Attribute(name);
        return p is null or < 0 ? null : p;
    }

    /// <summary>Trims and collapses empty/whitespace to null, so read and write agree on "unset".</summary>
    private static string? TrimToNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Reads a time amount (a plain decimal in its unit); ToDoList's 0 "no time" maps to null.</summary>
    private static double? ReadTime(XElement e, string name)
    {
        var s = (string?)e.Attribute(name);
        if (string.IsNullOrWhiteSpace(s)) return null;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;
    }

    /// <summary>Reads a *UNITS attribute; ToDoList defaults to hours when it's missing or unrecognised.</summary>
    private static TimeUnit ReadTimeUnit(XElement e, string name)
    {
        var s = (string?)e.Attribute(name);
        if (!string.IsNullOrWhiteSpace(s) && TimeUnits.FromFileCode(s.Trim()[0]) is TimeUnit u)
            return u;
        return TimeUnit.Hours;
    }

    /// <summary>Normalises a projected value + unit-word to hours for comparison; null when no value.</summary>
    private static double? InHours(double? value, string? unitWord)
    {
        if (value is not double v) return null;
        return TimeUnits.TryParse(unitWord, out var u) ? TimeUnits.ToHours(v, u) : v;
    }

    /// <summary>
    /// Reads a multi-value field from its attribute (legacy single-value form) and repeated child
    /// elements, de-duplicating case-insensitively to match ToDoList (its <c>AddTaskArrayItem</c>
    /// skips a value already present case-insensitively). When <paramref name="trim"/> is false the
    /// kept values are surfaced verbatim — used for file links, which must not be normalised.
    /// </summary>
    private static IReadOnlyList<string> ReadMulti(XElement e, string attrName, string childName, bool trim = true)
    {
        var values = new List<string>();
        var attr = (string?)e.Attribute(attrName);
        if (!string.IsNullOrWhiteSpace(attr)) values.Add(trim ? attr.Trim() : attr);
        foreach (var c in e.Elements(childName))
            if (!string.IsNullOrWhiteSpace(c.Value)) values.Add(trim ? c.Value.Trim() : c.Value);
        return values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static DateTime? ReadOaDate(XElement e, string name)
    {
        var s = (string?)e.Attribute(name);
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var oa) && oa > 0)
        {
            try { return DateTime.FromOADate(oa); }
            catch (ArgumentException) { return null; }
        }
        return null;
    }

    // ---- Mutations ---------------------------------------------------------

    public TodoTask AddTask(AddTaskRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            throw new ArgumentException("Title must not be empty.", nameof(req));

        var parent = req.ParentId is int pid
            ? FindTaskElement(pid) ?? throw new TaskNotFoundException(pid)
            : _root;

        var now = _clock.Now;
        var e = new XElement("TASK",
            new XAttribute("ID", AllocateId()),
            new XAttribute("TITLE", req.Title));
        e.SetAttributeValue("PERCENTDONE", 0);
        SetOaDate(e, "CREATIONDATE", now);
        e.SetAttributeValue("CREATIONDATESTRING", FormatStamp(now));

        if (req.Priority is int pr) e.SetAttributeValue("PRIORITY", ClampScale(pr));
        if (req.Risk is int rk) e.SetAttributeValue("RISK", ClampScale(rk));
        if (req.PercentDone is int pd) e.SetAttributeValue("PERCENTDONE", Math.Clamp(pd, 0, 100));
        if (req.TimeEstimate is double te)
            SetTime(e, "TIMEESTIMATE", "TIMEESTUNITS", te, req.TimeEstimateUnit ?? TimeUnit.Hours);
        if (req.TimeSpent is double ts)
            SetTime(e, "TIMESPENT", "TIMESPENTUNITS", ts, req.TimeSpentUnit ?? TimeUnit.Hours);
        if (req.Comments is not null) SetComments(e, req.Comments, req.CommentsFormat);
        if (req.DueDate is DateTime due) SetDueDate(e, due);
        if (req.StartDate is DateTime start) SetStartDate(e, start);
        if (req.Status is not null) SetStatus(e, req.Status);
        if (req.Version is not null) SetVersion(e, req.Version);
        if (req.Flag) SetFlag(e, true);
        if (req.ExternalId is not null) SetExternalId(e, req.ExternalId);
        SetMulti(e, "CATEGORY", "CATEGORY", req.Categories);
        SetMulti(e, "ALLOCATEDTO", "PERSON", req.AllocatedTo);
        SetMulti(e, "FILEREFPATH", "FILEREFPATH", req.FileLinks, trim: false);
        if (req.AllocatedBy is not null) SetAllocatedBy(e, req.AllocatedBy);
        Touch(e, now);

        var siblings = parent.Elements("TASK").ToList();
        if (req.Index is int idx && idx >= 0 && idx < siblings.Count)
            siblings[idx].AddBeforeSelf(e);
        else
            parent.Add(e);

        Renumber();
        TouchRoot(now);
        return Project(e);
    }

    public TodoTask UpdateTask(int id, UpdateTaskRequest req)
    {
        var e = FindTaskElement(id) ?? throw new TaskNotFoundException(id);
        EnsureNotLocked(e);
        // Validate the formatted-comments guard up front, before any mutation, so a refused
        // update never leaves the document partially changed (mirrors EnsureNotLocked).
        if (req.Comments is not null && !req.ReplaceFormattedComments && HasFormattedComments(e))
        {
            var fmt = ReadCommentsFormat(e);
            throw new FormattedCommentsException(
                id, string.IsNullOrEmpty(fmt) || fmt == CommentFormat.PlainText ? "formatted" : fmt);
        }
        var now = _clock.Now;

        if (req.Title is not null) e.SetAttributeValue("TITLE", req.Title);
        if (req.Comments is not null) SetComments(e, req.Comments, req.CommentsFormat);
        if (req.ExternalId is not null) SetExternalId(e, req.ExternalId);
        if (req.Status is not null) SetStatus(e, req.Status);
        if (req.Version is not null) SetVersion(e, req.Version);
        if (req.AllocatedBy is not null) SetAllocatedBy(e, req.AllocatedBy);
        if (req.Flag is bool flag) SetFlag(e, flag);

        if (req.ClearPriority) e.SetAttributeValue("PRIORITY", null);
        else if (req.Priority is int p) e.SetAttributeValue("PRIORITY", ClampScale(p));

        if (req.ClearRisk) e.SetAttributeValue("RISK", null);
        else if (req.Risk is int r) e.SetAttributeValue("RISK", ClampScale(r));

        if (req.PercentDone is int pd) e.SetAttributeValue("PERCENTDONE", Math.Clamp(pd, 0, 100));

        UpdateTime(e, "TIMEESTIMATE", "TIMEESTUNITS", req.ClearTimeEstimate, req.TimeEstimate, req.TimeEstimateUnit);
        UpdateTime(e, "TIMESPENT", "TIMESPENTUNITS", req.ClearTimeSpent, req.TimeSpent, req.TimeSpentUnit);

        if (req.ClearDueDate)
        {
            e.SetAttributeValue("DUEDATE", null);
            e.SetAttributeValue("DUEDATESTRING", null);
        }
        else if (req.DueDate is DateTime due)
        {
            SetDueDate(e, due);
        }

        if (req.ClearStartDate)
        {
            e.SetAttributeValue("STARTDATE", null);
            e.SetAttributeValue("STARTDATESTRING", null);
        }
        else if (req.StartDate is DateTime start)
        {
            SetStartDate(e, start);
        }

        if (req.Categories is not null) SetMulti(e, "CATEGORY", "CATEGORY", req.Categories);
        if (req.AllocatedTo is not null) SetMulti(e, "ALLOCATEDTO", "PERSON", req.AllocatedTo);
        if (req.FileLinks is not null) SetMulti(e, "FILEREFPATH", "FILEREFPATH", req.FileLinks, trim: false);

        Touch(e, now);
        TouchRoot(now);
        return Project(e);
    }

    public TodoTask CompleteTask(int id)
    {
        var e = FindTaskElement(id) ?? throw new TaskNotFoundException(id);
        EnsureNotLocked(e);
        var now = _clock.Now;
        SetOaDate(e, "DONEDATE", now);
        e.SetAttributeValue("DONEDATESTRING", FormatStamp(now));
        e.SetAttributeValue("PERCENTDONE", 100);
        // An explicitly-completed task is always "good as done"; keep ToDoList's cached flag in sync.
        e.SetAttributeValue("GOODASDONE", 1);
        Touch(e, now);
        TouchRoot(now);
        return Project(e);
    }

    public TodoTask ReopenTask(int id)
    {
        var e = FindTaskElement(id) ?? throw new TaskNotFoundException(id);
        EnsureNotLocked(e);
        var now = _clock.Now;
        e.SetAttributeValue("DONEDATE", null);
        e.SetAttributeValue("DONEDATESTRING", null);
        e.SetAttributeValue("PERCENTDONE", 0);
        // Clear the cached "done" flag; ToDoList recomputes any subtask-rollup on next load.
        e.SetAttributeValue("GOODASDONE", null);
        Touch(e, now);
        TouchRoot(now);
        return Project(e);
    }

    /// <summary>Removes a task and its subtree. Returns false if the ID was not found.</summary>
    public bool DeleteTask(int id)
    {
        var e = FindTaskElement(id);
        if (e is null) return false;
        // Mirror ToDoList: refuse to delete a locked task, or to delete a child out of a locked
        // parent. A locked descendant does not block deleting an ancestor (bCheckChildren=FALSE).
        EnsureNotLocked(e);
        EnsureParentNotLocked(e);
        e.Remove();
        Renumber();
        TouchRoot(_clock.Now);
        return true;
    }

    /// <summary>
    /// Moves a task under a new parent (null = top level) at the given zero-based index
    /// (null = append). Throws if the move would create a cycle.
    /// </summary>
    public TodoTask MoveTask(int id, int? newParentId, int? index = null)
    {
        var e = FindTaskElement(id) ?? throw new TaskNotFoundException(id);
        // Mirror ToDoList: refuse to move a locked task, to move it out of a locked parent, or
        // into a locked destination parent. A locked descendant does not block the move.
        EnsureNotLocked(e);
        EnsureParentNotLocked(e);
        var parent = newParentId is int pid
            ? FindTaskElement(pid) ?? throw new TaskNotFoundException(pid)
            : _root;
        if (parent != _root)
            EnsureNotLocked(parent);

        if (parent != _root && (parent == e || e.Descendants("TASK").Any(d => d == parent)))
            throw new InvalidOperationException("Cannot move a task into itself or one of its descendants.");

        var now = _clock.Now;
        e.Remove();
        var siblings = parent.Elements("TASK").ToList();
        if (index is int idx && idx >= 0 && idx < siblings.Count)
            siblings[idx].AddBeforeSelf(e);
        else
            parent.Add(e);

        // The moved task's parent/position changed, so stamp it — mirroring AddTask. The
        // renumbered siblings only get derived POS/POSSTRING and are left unstamped, as in AddTask.
        Touch(e, now);
        Renumber();
        TouchRoot(now);
        return Project(e);
    }

    // ---- Helpers -----------------------------------------------------------

    private XElement? FindTaskElement(int id) =>
        _root.Descendants("TASK").FirstOrDefault(t => (int?)t.Attribute("ID") == id);

    /// <summary>True when a task element is locked (read-only) in ToDoList.</summary>
    private static bool IsTaskLocked(XElement e) => (string?)e.Attribute("LOCK") == "1";

    /// <summary>Refuses to mutate a task the user has deliberately locked in ToDoList.</summary>
    private static void EnsureNotLocked(XElement e)
    {
        if (IsTaskLocked(e))
            throw new TaskLockedException((int?)e.Attribute("ID") ?? 0);
    }

    /// <summary>
    /// Refuses to restructure a task whose immediate parent is locked. ToDoList won't let you move
    /// or delete a child out of a locked parent (only the immediate parent is checked, matching
    /// its <c>SelectionHasLockedParent</c>). Editing a child's own attributes stays allowed unless
    /// the child itself is locked.
    /// </summary>
    private static void EnsureParentNotLocked(XElement e)
    {
        if (e.Parent is { } parent && parent.Name.LocalName == "TASK" && IsTaskLocked(parent))
            throw new TaskLockedException((int?)parent.Attribute("ID") ?? 0);
    }

    private int AllocateId()
    {
        var next = (int?)_root.Attribute("NEXTUNIQUEID");
        if (next is null or < 1)
        {
            var max = _root.Descendants("TASK").Select(t => (int?)t.Attribute("ID") ?? 0).DefaultIfEmpty(0).Max();
            next = max + 1;
        }
        _root.SetAttributeValue("NEXTUNIQUEID", next.Value + 1);
        return next.Value;
    }

    private void Renumber() => RenumberChildren(_root, null);

    private static void RenumberChildren(XElement parent, string? parentPos)
    {
        var i = 0;
        foreach (var t in parent.Elements("TASK"))
        {
            var oneBased = i + 1;
            var posString = parentPos is null ? oneBased.ToString() : $"{parentPos}.{oneBased}";
            t.SetAttributeValue("POS", i);
            t.SetAttributeValue("POSSTRING", posString);
            RenumberChildren(t, posString);
            i++;
        }
    }

    private void SetComments(XElement e, string text, CommentContentFormat format)
    {
        // Rewrite every comment representation from scratch: drop the legacy attribute form and any
        // existing rich payload, then re-emit per the requested format.
        e.SetAttributeValue("COMMENTS", null);
        e.Element("CUSTOMCOMMENTS")?.Remove();

        var child = e.Element("COMMENTS");
        if (string.IsNullOrEmpty(text))
        {
            child?.Remove();
            e.SetAttributeValue("COMMENTSTYPE", null);
            return;
        }
        if (child is null)
        {
            child = new XElement("COMMENTS");
            e.Add(child);
        }
        // <COMMENTS> always holds the plain-text mirror; non-plain formats also store the rich
        // source in <CUSTOMCOMMENTS> (base64 of UTF-16LE), exactly as ToDoList's content controls do.
        child.Value = CommentFormat.ToPlainMirror(format, text);
        e.SetAttributeValue("COMMENTSTYPE", CommentFormat.ToCommentsType(format));
        if (format != CommentContentFormat.Plain)
            child.AddAfterSelf(new XElement("CUSTOMCOMMENTS", CommentFormat.EncodeCustomComments(text)));
    }

    /// <summary>
    /// Writes a multi-value field as repeated child elements, replacing whatever was there. ToDoList
    /// always emits these as child elements — even a single value (its <c>SetTaskArray</c> uses
    /// <c>XIT_ELEMENT</c>) — and de-duplicates case-insensitively on add, so this mirrors both. When
    /// <paramref name="trim"/> is false the values are written verbatim — used for file links, whose
    /// paths must not be normalised (ToDoList does not trim array items either).
    /// </summary>
    private static void SetMulti(XElement e, string attrName, string childName, IReadOnlyList<string> values, bool trim = true)
    {
        // Clear both representations first: the legacy single-value attribute form (which ToDoList
        // still reads) and any existing child elements, so a replace can't leave a stale value behind.
        e.SetAttributeValue(attrName, null);
        foreach (var c in e.Elements(childName).ToList()) c.Remove();

        foreach (var v in values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => trim ? v.Trim() : v)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            e.Add(new XElement(childName, v));
        }
    }

    private void SetDueDate(XElement e, DateTime due)
    {
        SetOaDate(e, "DUEDATE", due);
        e.SetAttributeValue("DUEDATESTRING", FormatStamp(due));
    }

    private void SetStartDate(XElement e, DateTime start)
    {
        SetOaDate(e, "STARTDATE", start);
        e.SetAttributeValue("STARTDATESTRING", FormatStamp(start));
    }

    private static void SetStatus(XElement e, string status) =>
        e.SetAttributeValue("STATUS", TrimToNull(status));

    private static void SetExternalId(XElement e, string externalId) =>
        e.SetAttributeValue("EXTERNALID", TrimToNull(externalId));

    private static void SetVersion(XElement e, string version) =>
        e.SetAttributeValue("VERSION", TrimToNull(version));

    /// <summary>Writes a time amount (clamped to ≥ 0) and its unit code.</summary>
    private static void SetTime(XElement e, string valueName, string unitName, double value, TimeUnit unit)
    {
        e.SetAttributeValue(valueName, TimeUnits.Format(Math.Max(0, value)));
        e.SetAttributeValue(unitName, TimeUnits.ToFileCode(unit).ToString());
    }

    /// <summary>
    /// Applies an update to a time pair: clear removes both; a value sets the amount (keeping the
    /// existing unit unless one is given); a unit alone re-labels an existing amount.
    /// </summary>
    private static void UpdateTime(
        XElement e, string valueName, string unitName, bool clear, double? value, TimeUnit? unit)
    {
        if (clear)
        {
            e.SetAttributeValue(valueName, null);
            e.SetAttributeValue(unitName, null);
        }
        else if (value is double v)
        {
            SetTime(e, valueName, unitName, v, unit ?? ReadTimeUnit(e, unitName));
        }
        else if (unit is TimeUnit u && ReadTime(e, valueName) is not null)
        {
            // Only re-label when there's an actual amount; a 0/unset value reads as "no time".
            e.SetAttributeValue(unitName, TimeUnits.ToFileCode(u).ToString());
        }
    }

    private static void SetAllocatedBy(XElement e, string allocatedBy) =>
        e.SetAttributeValue("ALLOCATEDBY", TrimToNull(allocatedBy));

    private static void SetFlag(XElement e, bool flag) =>
        e.SetAttributeValue("FLAG", flag ? "1" : null);

    private static void SetOaDate(XElement e, string name, DateTime value) =>
        e.SetAttributeValue(name, value.ToOADate().ToString("0.00000000", CultureInfo.InvariantCulture));

    private void Touch(XElement e, DateTime now)
    {
        SetOaDate(e, "LASTMOD", now);
        e.SetAttributeValue("LASTMODSTRING", FormatStamp(now));
        e.SetAttributeValue("LASTMODBY", ModifiedBy);
    }

    private void TouchRoot(DateTime now)
    {
        SetOaDate(_root, "LASTMOD", now);
        _root.SetAttributeValue("LASTMODSTRING", FormatStamp(now));
        // Every successful mutation funnels through here; the no-op delete returns earlier.
        IsDirty = true;
    }

    private static int ClampScale(int v) => Math.Clamp(v, 0, 10);

    private static string FormatStamp(DateTime dt) =>
        dt.ToString("d/M/yyyy h:mm tt", CultureInfo.InvariantCulture);
}
