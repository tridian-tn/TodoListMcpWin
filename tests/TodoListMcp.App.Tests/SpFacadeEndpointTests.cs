using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TodoListMcp.App;
using TodoListMcp.App.Sp;

namespace TodoListMcp.App.Tests;

/// <summary>
/// In-process HTTP smoke test of the real SP facade routes over a fixture .tdl. The host is a
/// WinForms+Web hybrid (WinExe with a tray loop) so WebApplicationFactory can't bind it; instead we
/// build a minimal WebApplication around <see cref="SpFacade.MapSpFacadeV1"/> — the same routes,
/// CORS policy, and TodoListManager the app wires — and drive it with an HttpClient. Covers routing,
/// serialization, status codes, and CORS that the mapper unit test does not.
/// </summary>
public sealed class SpFacadeEndpointTests : IAsyncLifetime
{
    private const string FixtureXml =
        """
        <?xml version="1.0" encoding="utf-16"?>
        <TODOLIST PROJECTNAME="Test List" NEXTUNIQUEID="4" FILEVERSION="1" FILEFORMAT="12"><TASK ID="1" TITLE="Parent" PRIORITY="5" PERCENTDONE="0" POS="0" POSSTRING="1"><COMMENTS>Parent notes</COMMENTS><CATEGORY>Work</CATEGORY><TASK ID="2" TITLE="Child A" PRIORITY="8" PERCENTDONE="100" DONEDATE="45916.50000000" GOODASDONE="1" POS="0" POSSTRING="1.1"><PERSON>Bob</PERSON><PERSON>Jane</PERSON></TASK><TASK ID="3" TITLE="Child B" PRIORITY="2" PERCENTDONE="0" ALLOCATEDTO="Mary" DUEDATE="45916.79166667" DUEDATESTRING="16/9/2025" POS="1" POSSTRING="1.2"/></TASK></TODOLIST>
        """;

    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private string _tdlPath = null!;

    public async Task InitializeAsync()
    {
        _tdlPath = Path.Combine(Path.GetTempPath(), $"sp-facade-{Guid.NewGuid():N}.tdl");
        await File.WriteAllTextAsync(_tdlPath, FixtureXml, Encoding.Unicode);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.Configure<TodoListMcpOptions>(o =>
            o.Files.Add(new TodoFileEntry { Alias = "work", Path = _tdlPath, Default = true }));
        builder.Services.AddSingleton<TodoListManager>();

        _app = builder.Build();
        _app.Urls.Add("http://127.0.0.1:0");   // bind an ephemeral loopback port
        _app.MapSpFacadeV1();
        await _app.StartAsync();

        _client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        try { File.Delete(_tdlPath); } catch { /* best effort */ }
    }

    private static async Task<JsonNode> Json(HttpResponseMessage resp) =>
        JsonNode.Parse(await resp.Content.ReadAsStringAsync())!;

    [Fact]
    public async Task Health_returnsVersionAndFiles()
    {
        var resp = await _client.GetAsync("/integrations/sp/v1/health");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await Json(resp);
        Assert.Equal("1.0", json["apiVersion"]!.GetValue<string>());
        Assert.Contains("work", json["meta"]!["files"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public async Task Issues_openByDefault_flattensAndExcludesDone()
    {
        var json = await Json(await _client.GetAsync("/integrations/sp/v1/issues"));
        var ids = json["data"]!.AsArray().Select(n => n!["id"]!.GetValue<string>()).ToArray();
        Assert.Equal(new[] { "1", "3" }, ids);
    }

    [Fact]
    public async Task Issues_statusAll_countsAll()
    {
        var json = await Json(await _client.GetAsync("/integrations/sp/v1/issues?status=all"));
        Assert.Equal(3, json["data"]!.AsArray().Count);
        Assert.Equal(3, json["meta"]!["count"]!.GetValue<int>());
    }

    [Fact]
    public async Task Issues_query_matchesTitleWithinStatus()
    {
        var json = await Json(await _client.GetAsync("/integrations/sp/v1/issues?q=child"));
        var ids = json["data"]!.AsArray().Select(n => n!["id"]!.GetValue<string>()).ToArray();
        Assert.Equal(new[] { "3" }, ids);
    }

    [Fact]
    public async Task Issues_badStatus_returns400()
    {
        var resp = await _client.GetAsync("/integrations/sp/v1/issues?status=bogus");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("bad_request", (await Json(resp))["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Detail_mapsFieldsAndParent()
    {
        var json = await Json(await _client.GetAsync("/integrations/sp/v1/issues/3"));
        var d = json["data"]!;
        Assert.Equal("Child B", d["title"]!.GetValue<string>());
        Assert.Equal("2025-09-16", d["dueOn"]!.GetValue<string>());
        Assert.Equal("Mary", d["assignee"]!.GetValue<string>());
        Assert.Equal("Parent", d["parentName"]!.GetValue<string>());
        Assert.Equal("Test List", d["projectNames"]!.GetValue<string>());
    }

    [Fact]
    public async Task Detail_unknownId_returns404()
    {
        var resp = await _client.GetAsync("/integrations/sp/v1/issues/999");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal("not_found", (await Json(resp))["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Issues_unknownFile_returns400()
    {
        var resp = await _client.GetAsync("/integrations/sp/v1/issues?file=nope");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("bad_request", (await Json(resp))["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Detail_nonNumericId_returns400()
    {
        var resp = await _client.GetAsync("/integrations/sp/v1/issues/abc");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("bad_request", (await Json(resp))["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Issues_limit_capsAndFallsBackOnNonPositive()
    {
        Assert.Single((await Json(await _client.GetAsync("/integrations/sp/v1/issues?status=all&limit=1")))["data"]!.AsArray());
        // limit<=0 falls back to the default (returns all), not an empty list.
        Assert.Equal(3, (await Json(await _client.GetAsync("/integrations/sp/v1/issues?status=all&limit=0")))["data"]!.AsArray().Count);
    }

    [Fact]
    public async Task CrossOrigin_get_hasNoAllowOriginHeader()
    {
        // Security: a browser cross-origin fetch carries an Origin; the facade must NOT return
        // Access-Control-Allow-Origin, so the browser blocks that page from reading task data. The SP
        // plugin sends no Origin and is unaffected. The request itself still runs (read-only, harmless).
        using var req = new HttpRequestMessage(HttpMethod.Get, "/integrations/sp/v1/issues");
        req.Headers.Add("Origin", "http://evil.example");
        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.False(resp.Headers.Contains("Access-Control-Allow-Origin"),
            "facade must not grant cross-origin read access");
    }
}
