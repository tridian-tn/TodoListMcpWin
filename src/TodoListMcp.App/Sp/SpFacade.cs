using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace TodoListMcp.App.Sp;

/// <summary>
/// Maps the read-only SP issue-provider REST routes (v1). Mounted alongside — but distinct from — the
/// MCP endpoint; it reuses <see cref="TodoListManager"/> so reads share the same per-file lock and
/// fresh-load behaviour. It applies <b>no</b> CORS policy by design (see the note in
/// <see cref="MapSpFacadeV1"/>): the SP plugin's fetch sends no Origin, so a non-CORS request needs no
/// grant, while a malicious web origin (which does send one) is browser-blocked from reading the response.
/// </summary>
internal static class SpFacade
{
    public static IEndpointRouteBuilder MapSpFacadeV1(this IEndpointRouteBuilder app)
    {
        // No CORS policy by design. The SP plugin's fetch sends no Origin header (a non-CORS request),
        // so it needs no cross-origin grant. And by NOT emitting Access-Control-Allow-Origin, a malicious
        // web page — which DOES send an Origin — is blocked by the browser from reading task data, even
        // though the loopback port is reachable. (If a future SP build sends an Origin, whitelist it here.)
        var group = app.MapGroup(SpContracts.Prefix);

        // Liveness + version handshake (the plugin's testConnection).
        group.MapGet("/health", (TodoListManager manager) =>
            Ok<object?>(null, new { provider = "todolist", files = manager.Files.Select(f => f.Alias).ToArray() }));

        // Task summaries: searchIssues (q) and getNewIssuesForBacklog (status=open).
        group.MapGet("/issues",
            (string? file, string? status, string? q, string? limit, TodoListManager manager, ILoggerFactory lf) =>
            Guarded(lf, () =>
            {
                var want = NormaliseStatus(status);
                if (want is null)
                    return Err(StatusCodes.Status400BadRequest, "bad_request", "status must be open|done|all.");

                // Parse limit here (not via model binding) so a bad value returns the SP error envelope
                // rather than ASP.NET's default 400. A non-positive limit falls back to the default.
                var lim = 200;
                if (!string.IsNullOrWhiteSpace(limit))
                {
                    if (!int.TryParse(limit, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                        return Err(StatusCodes.Status400BadRequest, "bad_request", $"limit '{limit}' is not an integer.");
                    if (parsed > 0) lim = parsed;
                }

                // Resolve once, up front: an unknown/ambiguous alias fails fast as 400 before any read,
                // and the meta reflects exactly the list that was read (no second, racy resolution).
                var entry = manager.Resolve(file);
                var items = manager.Read(entry.Alias, doc => SpIssueMapper.Summaries(doc, want, q, lim));
                return Ok(items, new { file = entry.Alias, status = want, count = items.Count });
            }));

        // Full detail for one task (getById / getFreshDataForIssueTask).
        group.MapGet("/issues/{id}", (string id, string? file, TodoListManager manager, ILoggerFactory lf) =>
            Guarded(lf, () =>
            {
                if (!int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var taskId))
                    return Err(StatusCodes.Status400BadRequest, "bad_request", $"Task id '{id}' is not numeric.");

                var entry = manager.Resolve(file);
                var issue = manager.Read(entry.Alias, doc => SpIssueMapper.Detail(doc, taskId));
                return issue is null
                    ? Err(StatusCodes.Status404NotFound, "not_found", $"No task {taskId}.")
                    : Ok(issue, new { file = entry.Alias });
            }));

        return app;
    }

    private static IResult Ok<T>(T data, object? meta) =>
        Results.Json(new SpEnvelope<T>(SpContracts.ApiVersion, data, meta), SpContracts.Json);

    private static IResult Err(int status, string code, string message) =>
        Results.Json(new SpErrorEnvelope(SpContracts.ApiVersion, new SpError(code, message)),
            SpContracts.Json, statusCode: status);

    private static IResult Guarded(ILoggerFactory loggerFactory, Func<IResult> body)
    {
        try
        {
            return body();
        }
        // Unknown alias / no lists configured / ambiguous default — a caller (config) problem. Resolve()'s
        // messages are worded for the MCP `list` parameter; the facade exposes it as `file`, so retarget
        // that reference. (They only name configured aliases, which /health already exposes, so returning
        // them is safe.)
        catch (InvalidOperationException ex)
        {
            return Err(StatusCodes.Status400BadRequest, "bad_request", ex.Message.Replace("'list'", "'file'"));
        }
        // Anything else (missing/locked/corrupt .tdl) — log the detail server-side and return a fixed
        // generic message, so filesystem paths (which contain the Windows username) never reach a client.
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("TodoListMcp.App.Sp.SpFacade").LogError(ex, "SP facade read failed.");
            return Err(StatusCodes.Status500InternalServerError, "read_error", "The list could not be read.");
        }
    }

    private static string? NormaliseStatus(string? status)
    {
        var s = string.IsNullOrWhiteSpace(status) ? "open" : status.Trim().ToLowerInvariant();
        return s is "open" or "done" or "all" ? s : null;
    }
}
