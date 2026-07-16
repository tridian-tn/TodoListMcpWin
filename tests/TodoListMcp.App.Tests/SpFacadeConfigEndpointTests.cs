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
/// Facade behaviour under different list configurations (alias resolution, ambiguity, missing files) —
/// scenarios the single-default-file harness in <see cref="SpFacadeEndpointTests"/> can't cover. Each
/// test spins up its own minimal host with a tailored <see cref="TodoListMcpOptions"/>.
/// </summary>
public sealed class SpFacadeConfigEndpointTests
{
    private const string OneTaskXml =
        """
        <?xml version="1.0" encoding="utf-16"?>
        <TODOLIST PROJECTNAME="{P}" NEXTUNIQUEID="2" FILEVERSION="1" FILEFORMAT="12"><TASK ID="1" TITLE="{T}" PERCENTDONE="0" POS="0" POSSTRING="1"/></TODOLIST>
        """;

    private static string WriteFixture(string project, string title)
    {
        var path = Path.Combine(Path.GetTempPath(), $"sp-cfg-{Guid.NewGuid():N}.tdl");
        File.WriteAllText(path, OneTaskXml.Replace("{P}", project).Replace("{T}", title), Encoding.Unicode);
        return path;
    }

    private static async Task<(WebApplication app, HttpClient client)> StartAsync(params TodoFileEntry[] files)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.Configure<TodoListMcpOptions>(o => { foreach (var f in files) o.Files.Add(f); });
        builder.Services.AddSingleton<TodoListManager>();

        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.MapSpFacadeV1();
        await app.StartAsync();
        return (app, new HttpClient { BaseAddress = new Uri(app.Urls.First()) });
    }

    private static async Task<JsonNode> Json(HttpResponseMessage r) =>
        JsonNode.Parse(await r.Content.ReadAsStringAsync())!;

    [Fact]
    public async Task File_param_selectsThatList()
    {
        var work = WriteFixture("Work", "W1");
        var home = WriteFixture("Home", "H1");
        var (app, client) = await StartAsync(
            new TodoFileEntry { Alias = "work", Path = work, Default = true },
            new TodoFileEntry { Alias = "home", Path = home });
        try
        {
            var json = await Json(await client.GetAsync("/integrations/sp/v1/issues?file=home"));
            Assert.Equal("home", json["meta"]!["file"]!.GetValue<string>());
            Assert.Equal("H1", json["data"]!.AsArray().Single()!["title"]!.GetValue<string>());
        }
        finally { client.Dispose(); await app.DisposeAsync(); File.Delete(work); File.Delete(home); }
    }

    [Fact]
    public async Task MultipleLists_noDefault_noFileParam_returns400()
    {
        var a = WriteFixture("A", "A1");
        var b = WriteFixture("B", "B1");
        var (app, client) = await StartAsync(
            new TodoFileEntry { Alias = "a", Path = a },
            new TodoFileEntry { Alias = "b", Path = b });
        try
        {
            var resp = await client.GetAsync("/integrations/sp/v1/issues");
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            Assert.Equal("bad_request", (await Json(resp))["error"]!["code"]!.GetValue<string>());
        }
        finally { client.Dispose(); await app.DisposeAsync(); File.Delete(a); File.Delete(b); }
    }

    [Fact]
    public async Task MissingFileOnDisk_returns500_readError_withoutLeakingPath()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"sp-missing-{Guid.NewGuid():N}.tdl");
        var (app, client) = await StartAsync(
            new TodoFileEntry { Alias = "gone", Path = missing, Default = true });
        try
        {
            var resp = await client.GetAsync("/integrations/sp/v1/issues");
            Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
            var json = await Json(resp);
            Assert.Equal("read_error", json["error"]!["code"]!.GetValue<string>());
            // SEC-3: the generic message must not disclose the filesystem path.
            Assert.DoesNotContain("sp-missing-", json["error"]!["message"]!.GetValue<string>());
        }
        finally { client.Dispose(); await app.DisposeAsync(); }
    }

    [Fact]
    public async Task Health_listsAllConfiguredAliases()
    {
        var a = WriteFixture("A", "A1");
        var b = WriteFixture("B", "B1");
        var (app, client) = await StartAsync(
            new TodoFileEntry { Alias = "a", Path = a, Default = true },
            new TodoFileEntry { Alias = "b", Path = b });
        try
        {
            var files = (await Json(await client.GetAsync("/integrations/sp/v1/health")))["meta"]!["files"]!
                .AsArray().Select(n => n!.GetValue<string>()).ToArray();
            Assert.Equal(new[] { "a", "b" }, files);
        }
        finally { client.Dispose(); await app.DisposeAsync(); File.Delete(a); File.Delete(b); }
    }
}
