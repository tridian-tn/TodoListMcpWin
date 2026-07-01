namespace TodoListMcp.App;

/// <summary>Configuration for the MCP server, bound from the "TodoListMcp" section.</summary>
public sealed class TodoListMcpOptions
{
    public const string SectionName = "TodoListMcp";

    /// <summary>Loopback TCP port the MCP endpoint listens on.</summary>
    public int Port { get; set; } = 3001;

    /// <summary>
    /// Serve over HTTPS. Off by default: the endpoint is loopback-only, so plain HTTP never leaves
    /// the machine and avoids the certificate step. When true the app uses a self-signed localhost
    /// certificate; when false it serves plain HTTP.
    /// </summary>
    public bool UseHttps { get; set; } = false;

    /// <summary>
    /// Install the self-signed certificate into the current user's Trusted Root store so Claude
    /// accepts it. The first install shows a one-time Windows consent prompt (no admin needed).
    /// </summary>
    public bool TrustCertificate { get; set; } = true;

    /// <summary>Value written to LASTMODBY when this server changes a task.</summary>
    public string ModifiedBy { get; set; } = "TodoListMcp";

    /// <summary>
    /// Default time-log layout for lists that don't set their own <see cref="TodoFileEntry.LogMode"/>.
    /// Mirrors ToDoList's global "log tasks separately" preference, which this server can't read, so
    /// it's configured here instead. Defaults to <see cref="LogMode.Combined"/>.
    /// </summary>
    public LogMode DefaultLogMode { get; set; } = LogMode.Combined;

    /// <summary>The .tdl files this server is allowed to act on.</summary>
    public List<TodoFileEntry> Files { get; set; } = new();
}

/// <summary>How a list's logged-time entries are laid out on disk (see <see cref="TimeLogPaths"/>).</summary>
public enum LogMode
{
    /// <summary>One combined <c>&lt;base&gt;_Log.csv</c> beside the <c>.tdl</c> (ToDoList's default).</summary>
    Combined,

    /// <summary>One <c>&lt;taskID&gt;_Log.csv</c> per task in a <c>&lt;base&gt;\</c> folder.</summary>
    Separate,
}

/// <summary>A single configured ToDoList file.</summary>
public sealed class TodoFileEntry
{
    /// <summary>Short name used by tool callers to select this file.</summary>
    public string Alias { get; set; } = "";

    /// <summary>Absolute path to the .tdl file.</summary>
    public string Path { get; set; } = "";

    /// <summary>When true, this file is used when a tool call omits the list alias.</summary>
    public bool Default { get; set; }

    /// <summary>
    /// This list's time-log layout, overriding <see cref="TodoListMcpOptions.DefaultLogMode"/>. Null
    /// (the default) inherits the global default.
    /// </summary>
    public LogMode? LogMode { get; set; }
}
