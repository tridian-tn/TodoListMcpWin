using System.Text.Json;
using System.Text.Json.Serialization;

namespace TodoListMcp.App.Sp;

/// <summary>
/// Constants and DTOs for the read-only Super Productivity issue-provider facade (v1). This surface is
/// deliberately separate from the MCP tool surface: it is a plain REST namespace consumed by the SP
/// plugin and is never advertised through MCP (tools/list), so an LLM cannot discover it. See
/// docs/contracts/sp-todolist-rest-v1.md in the sp-todolist repo for the pinned contract.
/// </summary>
internal static class SpContracts
{
    public const string Prefix = "/integrations/sp/v1";
    public const string ApiVersion = "1.0";

    /// <summary>
    /// camelCase (web defaults) but keep null fields — the contract requires every field present even
    /// when null (e.g. dueOn:null, assignee:null), so the plugin sees a stable shape.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}

internal sealed record SpEnvelope<T>(string ApiVersion, T? Data, object? Meta);

internal sealed record SpErrorEnvelope(string ApiVersion, SpError Error);

internal sealed record SpError(string Code, string Message);

internal sealed record SpIssue(
    string Id,
    string Title,
    string Body,
    bool IsDone,
    string State,
    string Url,
    long? LastUpdated,
    long? CreatedAt,
    string? DueOn,
    string? Assignee,
    IReadOnlyList<string> Labels,
    int? Priority,
    string ProjectNames,
    string? ParentName,
    int NumSubtasks);

internal sealed record SpIssueSummary(
    string Id,
    string Title,
    string Status,
    bool IsDone,
    string? Assignee,
    string? DueDate,
    string Url);
