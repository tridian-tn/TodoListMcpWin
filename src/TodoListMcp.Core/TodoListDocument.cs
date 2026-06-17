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
///  - Assignees are &lt;PERSON&gt; child elements (or a single ALLOCATEDTO attribute).
///  - Categories are &lt;CATEGORY&gt; child elements (or a single CATEGORY attribute).
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
        if (q.MinPriority is int min && (t.Priority ?? -1) < min) return false;
        return true;
    }

    private TodoTask Project(XElement e, bool includeSubtasks = true) => new()
    {
        Id = (int?)e.Attribute("ID") ?? 0,
        Title = (string?)e.Attribute("TITLE") ?? "",
        Comments = ReadComments(e),
        Priority = ReadPriority(e),
        PercentDone = (int?)e.Attribute("PERCENTDONE") ?? 0,
        IsDone = e.Attribute("DONEDATE") is { } d && !string.IsNullOrWhiteSpace(d.Value),
        IsGoodAsDone = (string?)e.Attribute("GOODASDONE") == "1",
        DueDate = ReadOaDate(e, "DUEDATE"),
        StartDate = ReadOaDate(e, "STARTDATE"),
        DoneDate = ReadOaDate(e, "DONEDATE"),
        CreationDate = ReadOaDate(e, "CREATIONDATE"),
        LastModified = ReadOaDate(e, "LASTMOD"),
        Categories = ReadMulti(e, "CATEGORY", "CATEGORY"),
        AllocatedTo = ReadMulti(e, "ALLOCATEDTO", "PERSON"),
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

    private static int? ReadPriority(XElement e)
    {
        var p = (int?)e.Attribute("PRIORITY");
        return p is null or < 0 ? null : p;
    }

    private static IReadOnlyList<string> ReadMulti(XElement e, string attrName, string childName)
    {
        var values = new List<string>();
        var attr = (string?)e.Attribute(attrName);
        if (!string.IsNullOrWhiteSpace(attr)) values.Add(attr.Trim());
        foreach (var c in e.Elements(childName))
            if (!string.IsNullOrWhiteSpace(c.Value)) values.Add(c.Value.Trim());
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

        if (req.Priority is int pr) e.SetAttributeValue("PRIORITY", ClampPriority(pr));
        if (req.Comments is not null) SetComments(e, req.Comments);
        if (req.DueDate is DateTime due) SetDueDate(e, due);
        SetMulti(e, "CATEGORY", "CATEGORY", req.Categories);
        SetMulti(e, "ALLOCATEDTO", "PERSON", req.AllocatedTo);
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
        var now = _clock.Now;

        if (req.Title is not null) e.SetAttributeValue("TITLE", req.Title);
        if (req.Comments is not null) SetComments(e, req.Comments);

        if (req.ClearPriority) e.SetAttributeValue("PRIORITY", null);
        else if (req.Priority is int p) e.SetAttributeValue("PRIORITY", ClampPriority(p));

        if (req.PercentDone is int pd) e.SetAttributeValue("PERCENTDONE", Math.Clamp(pd, 0, 100));

        if (req.ClearDueDate)
        {
            e.SetAttributeValue("DUEDATE", null);
            e.SetAttributeValue("DUEDATESTRING", null);
        }
        else if (req.DueDate is DateTime due)
        {
            SetDueDate(e, due);
        }

        if (req.Categories is not null) SetMulti(e, "CATEGORY", "CATEGORY", req.Categories);
        if (req.AllocatedTo is not null) SetMulti(e, "ALLOCATEDTO", "PERSON", req.AllocatedTo);

        Touch(e, now);
        TouchRoot(now);
        return Project(e);
    }

    public TodoTask CompleteTask(int id)
    {
        var e = FindTaskElement(id) ?? throw new TaskNotFoundException(id);
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
        var parent = newParentId is int pid
            ? FindTaskElement(pid) ?? throw new TaskNotFoundException(pid)
            : _root;

        if (parent != _root && (parent == e || e.Descendants("TASK").Any(d => d == parent)))
            throw new InvalidOperationException("Cannot move a task into itself or one of its descendants.");

        e.Remove();
        var siblings = parent.Elements("TASK").ToList();
        if (index is int idx && idx >= 0 && idx < siblings.Count)
            siblings[idx].AddBeforeSelf(e);
        else
            parent.Add(e);

        Renumber();
        TouchRoot(_clock.Now);
        return Project(e);
    }

    // ---- Helpers -----------------------------------------------------------

    private XElement? FindTaskElement(int id) =>
        _root.Descendants("TASK").FirstOrDefault(t => (int?)t.Attribute("ID") == id);

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

    private void SetComments(XElement e, string text)
    {
        // Drop legacy attribute form and any rich-text override so plain text wins.
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
        child.Value = text;
        e.SetAttributeValue("COMMENTSTYPE", "PLAIN_TEXT");
    }

    private static void SetMulti(XElement e, string attrName, string childName, IReadOnlyList<string> values)
    {
        e.SetAttributeValue(attrName, null);
        foreach (var c in e.Elements(childName).ToList()) c.Remove();

        var vals = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (vals.Count == 0) return;
        if (vals.Count == 1)
        {
            // ToDoList writes a single value as an attribute.
            e.SetAttributeValue(attrName, vals[0]);
        }
        else
        {
            foreach (var v in vals) e.Add(new XElement(childName, v));
        }
    }

    private void SetDueDate(XElement e, DateTime due)
    {
        SetOaDate(e, "DUEDATE", due);
        e.SetAttributeValue("DUEDATESTRING", FormatStamp(due));
    }

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
    }

    private static int ClampPriority(int p) => Math.Clamp(p, 0, 10);

    private static string FormatStamp(DateTime dt) =>
        dt.ToString("d/M/yyyy h:mm tt", CultureInfo.InvariantCulture);
}
