using System.Globalization;
using System.Text;
using TodoListMcp.Core.Model;

namespace TodoListMcp.Core;

/// <summary>
/// Reads and writes an AbstractSpoon ToDoList time-log sidecar (<c>&lt;listname&gt;_Log.csv</c>) with
/// format fidelity. This is a structured, append-style log of time entries, kept beside the .tdl and
/// independent of the per-task TIMESPENT attribute.
///
/// Key format facts honoured here (verified against a ToDoList-written file and
/// <c>Core/ToDoList/TDCTaskTimeLog.cpp</c>):
///  - UTF-16 (LE, with BOM), bare-LF line endings, with a trailing newline.
///  - A literal first line <c>TODOTIMELOG VERSION 1</c>, then a header row, then one row per entry.
///  - The latest layout has 12 tab-separated columns: Task ID, Title, User ID, Start Date, Start
///    Time, End Date, End Time, Time Spent (Hrs), Comment, Type, Path, Colour.
///  - Dates are <c>yyyy-MM-dd</c>, times <c>HH:mm</c> (local); hours are formatted to 3 decimals.
///  - The delimiter is auto-detected on read (tab, then comma, then semicolon). A field is quoted
///    only when it contains the delimiter or a quote; embedded newlines are encoded as <c>|</c>.
///  - The row version is taken from the file's version line; legacy rows are detected by field count.
///
/// Existing rows are preserved verbatim across a load/append/save round-trip (their original text is
/// re-emitted), so columns and legacy formats this class does not fully model survive untouched.
/// </summary>
public sealed class TimeLogDocument
{
    private const string DefaultVersionLine = "TODOTIMELOG VERSION 1";

    private static readonly string[] LatestColumns =
    {
        "Task ID", "Title", "User ID", "Start Date", "Start Time", "End Date", "End Time",
        "Time Spent (Hrs)", "Comment", "Type", "Path", "Colour",
    };

    private readonly List<Row> _rows = new();
    private char _delimiter = '\t';
    private string? _versionLine;
    private string? _headerLine;

    /// <summary>Path the sidecar was loaded from (it need not exist yet), if any.</summary>
    public string? FilePath { get; private set; }

    /// <summary>True once an entry has been appended since load.</summary>
    public bool IsDirty { get; private set; }

    private TimeLogDocument() { }

    /// <summary>One stored row: its parsed projection plus the original text for faithful re-emit.</summary>
    private sealed record Row(TimeLogEntry Entry, string? Raw);

    // ---- Loading / saving --------------------------------------------------

    /// <summary>
    /// Loads a sidecar from disk. A missing file yields an empty log (so the first append simply
    /// creates the file). The file is read as UTF-16 or UTF-8 per its BOM.
    /// </summary>
    public static TimeLogDocument Load(string path)
    {
        var doc = new TimeLogDocument { FilePath = path };
        if (!File.Exists(path)) return doc;
        var text = ReadAllText(path);
        doc.ParseInto(text);
        return doc;
    }

    /// <summary>Parses a sidecar from an in-memory string (used by tests); no backing file.</summary>
    public static TimeLogDocument Parse(string text)
    {
        var doc = new TimeLogDocument();
        doc.ParseInto(text);
        return doc;
    }

    private void ParseInto(string text)
    {
        // Split on LF and drop a trailing CR so both LF and CRLF files read cleanly. A final empty
        // segment from the trailing newline is ignored.
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var i = 0;

        if (i < lines.Length && lines[i].StartsWith("TODOTIMELOG VERSION", StringComparison.OrdinalIgnoreCase))
            _versionLine = lines[i++];

        // A non-empty line at this position is the header. An empty one (a BOM-only file, or a file
        // with only the version line) leaves the header unset, so a save synthesises the latest one
        // rather than re-emitting a blank header line.
        if (i < lines.Length && lines[i].Length > 0)
        {
            _headerLine = lines[i++];
            _delimiter = DetectDelimiter(_headerLine);
        }

        for (; i < lines.Length; i++)
        {
            var raw = lines[i];
            if (raw.Length == 0) continue; // trailing newline / blank line
            _rows.Add(new Row(ParseRow(raw, _delimiter), raw));
        }
    }

    /// <summary>Saves back to <see cref="FilePath"/> atomically (temp file + replace).</summary>
    public void Save()
    {
        if (FilePath is null)
            throw new InvalidOperationException("This log was not loaded from a file.");
        SaveAs(FilePath);
    }

    /// <summary>Saves to the given path atomically, as UTF-16 LE with BOM and LF line endings.</summary>
    public void SaveAs(string path)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, BuildText(), new UnicodeEncoding(bigEndian: false, byteOrderMark: true));
        if (File.Exists(path))
            File.Replace(tmp, path, null);
        else
            File.Move(tmp, path);
        FilePath = path;
    }

    /// <summary>Serialises the whole log to a string (used by tests and diagnostics).</summary>
    public string ToText() => BuildText();

    private string BuildText()
    {
        var sb = new StringBuilder();
        sb.Append(_versionLine ?? DefaultVersionLine).Append('\n');
        sb.Append(_headerLine ?? BuildHeader(_delimiter)).Append('\n');
        foreach (var row in _rows)
            sb.Append(row.Raw ?? FormatRow(row.Entry, _delimiter)).Append('\n');
        return sb.ToString();
    }

    // ---- Reading -----------------------------------------------------------

    /// <summary>All entries, oldest first (file order).</summary>
    public IReadOnlyList<TimeLogEntry> Entries => _rows.Select(r => r.Entry).ToList();

    /// <summary>Entries matching every supplied criterion (AND); task-less entries are included.</summary>
    public IReadOnlyList<TimeLogEntry> Read(TimeLogQuery query)
    {
        IEnumerable<TimeLogEntry> q = _rows.Select(r => r.Entry);
        if (query.TaskId is int tid) q = q.Where(e => e.TaskId == tid);
        if (query.Since is DateTime since) q = q.Where(e => e.To >= since);
        if (query.Until is DateTime until) q = q.Where(e => e.From <= until);
        if (!string.IsNullOrWhiteSpace(query.Person))
            q = q.Where(e => string.Equals(e.Person, query.Person.Trim(), StringComparison.OrdinalIgnoreCase));
        return q.ToList();
    }

    // ---- Appending ---------------------------------------------------------

    /// <summary>
    /// Appends one entry. Mirrors ToDoList's validity rule: the entry must have a comment, or a
    /// non-zero period with <see cref="TimeLogEntry.From"/> ≤ <see cref="TimeLogEntry.To"/>.
    /// </summary>
    public void Append(TimeLogEntry entry)
    {
        if (!IsValidToLog(entry))
            throw new ArgumentException(
                "A log entry needs a comment, or a non-zero number of hours over a valid period.", nameof(entry));
        _rows.Add(new Row(entry, null));
        IsDirty = true;
    }

    private static bool IsValidToLog(TimeLogEntry e) =>
        !string.IsNullOrEmpty(e.Comment) || (e.Hours != 0 && e.From != default && e.To >= e.From);

    // ---- Row formatting / parsing -----------------------------------------

    private static string BuildHeader(char delim) => string.Join(delim, LatestColumns);

    private static string FormatRow(TimeLogEntry e, char delim)
    {
        var fields = new[]
        {
            e.TaskId.ToString(CultureInfo.InvariantCulture),
            e.TaskTitle,
            e.Person ?? "",
            e.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            e.From.ToString("HH:mm", CultureInfo.InvariantCulture),
            e.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            e.To.ToString("HH:mm", CultureInfo.InvariantCulture),
            e.Hours.ToString("0.000", CultureInfo.InvariantCulture),
            e.Comment ?? "",
            e.Type ?? "",
            e.Path ?? "",
            "", // Colour: always empty (this server does not set an alt-colour)
        };
        return string.Join(delim, fields.Select(f => Encode(f, delim)));
    }

    /// <summary>
    /// Projects a raw row into an entry. The 12-column latest layout is mapped fully; a legacy
    /// 6-column VER_0 row (ID, Title, Hours, Person, To, From) is mapped best-effort. Either way the
    /// original text is retained for a faithful re-emit, so an imperfect parse never corrupts the file.
    /// </summary>
    private static TimeLogEntry ParseRow(string raw, char delim)
    {
        var f = SplitRow(raw, delim);

        if (f.Count >= 12)
        {
            return new TimeLogEntry
            {
                TaskId = ParseInt(f[0]),
                TaskTitle = f[1],
                Person = NullIfEmpty(f[2]),
                From = ParseDateTime(f[3], f[4]),
                To = ParseDateTime(f[5], f[6]),
                Hours = ParseDouble(f[7]),
                Comment = NullIfEmpty(f[8]),
                Type = NullIfEmpty(f[9]),
                Path = NullIfEmpty(f[10]),
            };
        }

        if (f.Count == 6)
        {
            return new TimeLogEntry
            {
                TaskId = ParseInt(f[0]),
                TaskTitle = f[1],
                Hours = ParseDouble(f[2]),
                Person = NullIfEmpty(f[3]),
                To = ParseDateTime(f[4], ""),
                From = ParseDateTime(f[5], ""),
            };
        }

        // Unknown layout: surface what we safely can; the raw text still round-trips.
        return new TimeLogEntry
        {
            TaskId = f.Count > 0 ? ParseInt(f[0]) : 0,
            TaskTitle = f.Count > 1 ? f[1] : "",
        };
    }

    // ---- Encoding helpers --------------------------------------------------

    private static char DetectDelimiter(string header)
    {
        if (header.Contains('\t')) return '\t';
        if (header.Contains(',')) return ',';
        if (header.Contains(';')) return ';';
        return '\t';
    }

    /// <summary>
    /// Encodes a field: newlines → <c>|</c>, and quotes only when it contains the delimiter or a
    /// quote. This mirrors ToDoList exactly, including its limitation: <c>|</c> is the newline
    /// sentinel with no escape, so a literal <c>|</c> in a value decodes back to a newline on the
    /// next read — ToDoList's own encoder behaves identically, so we do not diverge to "fix" it.
    /// </summary>
    private static string Encode(string value, char delim)
    {
        var s = value.Replace("\r", "").Replace('\n', '|');
        if (s.IndexOf(delim) >= 0 || s.IndexOf('"') >= 0)
            s = "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    /// <summary>Splits a row on the delimiter, honouring double-quoted fields, then decodes <c>|</c> back to newline.</summary>
    private static List<string> SplitRow(string line, char delim)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else if (c == '"' && sb.Length == 0) inQuotes = true;
            else if (c == delim) { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        for (var i = 0; i < fields.Count; i++)
            fields[i] = fields[i].Replace('|', '\n');
        return fields;
    }

    private static int ParseInt(string s) =>
        int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static double ParseDouble(string s) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static DateTime ParseDateTime(string date, string time)
    {
        var d = date.Trim();
        var t = time.Trim();
        var combined = string.IsNullOrEmpty(t) ? d : $"{d} {t}";
        var formats = new[] { "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd" };
        if (DateTime.TryParseExact(combined, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt;
        return DateTime.TryParse(combined, CultureInfo.InvariantCulture, DateTimeStyles.None, out var any)
            ? any : default;
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

    private static string ReadAllText(string path)
    {
        // Honour the BOM: ToDoList writes UTF-16 LE for tab-delimited logs and UTF-8 otherwise.
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        return Encoding.UTF8.GetString(bytes);
    }
}
