namespace TodoListMcp.App;

/// <summary>Configuration for the MCP server, bound from the "TodoListMcp" section.</summary>
public sealed class TodoListMcpOptions
{
    public const string SectionName = "TodoListMcp";

    /// <summary>Loopback TCP port the MCP endpoint listens on.</summary>
    public int Port { get; set; } = 3001;

    /// <summary>
    /// Serve over HTTPS (required by Claude's custom-connector flow). When true the app uses a
    /// self-signed localhost certificate; when false it falls back to plain HTTP.
    /// </summary>
    public bool UseHttps { get; set; } = true;

    /// <summary>
    /// Install the self-signed certificate into the current user's Trusted Root store so Claude
    /// accepts it. The first install shows a one-time Windows consent prompt (no admin needed).
    /// </summary>
    public bool TrustCertificate { get; set; } = true;

    /// <summary>Value written to LASTMODBY when this server changes a task.</summary>
    public string ModifiedBy { get; set; } = "TodoListMcp";

    /// <summary>The .tdl files this server is allowed to act on.</summary>
    public List<TodoFileEntry> Files { get; set; } = new();
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
}
