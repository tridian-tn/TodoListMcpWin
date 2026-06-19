namespace TodoListMcp.Core;

/// <summary>
/// Maps ToDoList's <c>COMMENTSTYPE</c> identifiers to friendly format names. The built-in
/// <c>PLAIN_TEXT</c> aside, each format is a "content control" identified by a GUID. The GUIDs
/// are verified against the abstractspoon/ToDoList_9.2 source (Core/RTFContentCtrl and
/// Plugins/ContentControl).
/// </summary>
public static class CommentFormat
{
    /// <summary>Friendly name for plain-text comments — the only format this server authors.</summary>
    public const string PlainText = "plain";

    /// <summary>Friendly name for the built-in Rich Text (RTF) content control.</summary>
    public const string Rich = "rich";

    /// <summary>Friendly name for the HTML content control.</summary>
    public const string Html = "html";

    /// <summary>Friendly name for the Markdown content control.</summary>
    public const string Markdown = "markdown";

    /// <summary>Friendly name for the Spreadsheet content control.</summary>
    public const string Spreadsheet = "spreadsheet";

    /// <summary>
    /// Friendly name for comments that carry a rich &lt;CUSTOMCOMMENTS&gt; payload but no recorded
    /// COMMENTSTYPE — formatted, but the specific format is not known. The plain-text mirror is not
    /// authoritative for these.
    /// </summary>
    public const string Unknown = "unknown";

    // ToDoList COMMENTSTYPE attribute values (uppercase, as ToDoList writes them).
    private const string PlainTextId = "PLAIN_TEXT";
    private const string RichId = "849CF988-79FE-418A-A40D-01FE3AFCAB2C";
    private const string HtmlId = "FE0B6B6E-2B61-4AEB-AA0D-98DBE5942F02";
    private const string MarkdownId = "BAA4E079-268B-4B9B-B7C8-6D15CCF058A2";
    private const string SpreadsheetId = "BBDCAEDF-B297-4E09-BBFB-B308358628B9";

    /// <summary>
    /// Maps a raw <c>COMMENTSTYPE</c> value to a friendly name (plain/rich/html/markdown/spreadsheet),
    /// or returns the (trimmed) raw value unchanged when it's an unrecognised content-control id.
    /// </summary>
    public static string ToFriendly(string raw) => raw.Trim().ToUpperInvariant() switch
    {
        PlainTextId => PlainText,
        RichId => Rich,
        HtmlId => Html,
        MarkdownId => Markdown,
        SpreadsheetId => Spreadsheet,
        _ => raw.Trim(),
    };
}
