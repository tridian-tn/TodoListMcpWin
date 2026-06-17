using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using TodoListMcp.App.Mcp;
using TodoListMcp.App.Tray;

namespace TodoListMcp.App;

internal static class Program
{
    // Session-local: one running instance per logged-in user.
    private const string SingleInstanceName = @"Local\TodoListMcp.SingleInstance";

    private static Mutex? _singleInstance;

    [STAThread]
    private static void Main(string[] args)
    {
        // One-shot, scriptable autostart management (used by installers / power users).
        if (TryHandleAutostartCommand(args)) return;

        // Enforce a single running instance.
        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceName, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "TodoList MCP is already running — look for its icon in the system tray (near the clock).",
                "TodoList MCP", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        AppPaths.EnsureCreated();
        AppPaths.EnsureUserConfig();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(AppPaths.LogDir, "todolistmcp-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        try
        {
            RunApp(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "TodoList MCP terminated unexpectedly.");
            MessageBox.Show(ex.Message, "TodoList MCP — fatal error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Log.CloseAndFlush();
            try { _singleInstance?.ReleaseMutex(); } catch (ApplicationException) { /* not owned */ }
            _singleInstance?.Dispose();
        }
    }

    /// <summary>
    /// Handles <c>--enable-autostart</c> / <c>--disable-autostart</c> and exits. Returns true if a
    /// command was handled (the caller should then return without starting the tray/server).
    /// </summary>
    private static bool TryHandleAutostartCommand(string[] args)
    {
        if (args.Length == 0) return false;
        switch (args[0].Trim().ToLowerInvariant())
        {
            case "--enable-autostart":
                StartupManager.SetEnabled(true);
                return true;
            case "--disable-autostart":
                StartupManager.SetEnabled(false);
                return true;
            default:
                return false;
        }
    }

    private static void RunApp(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        // Layer the user-editable config (in %APPDATA%) on top of the bundled appsettings.json.
        builder.Configuration.AddJsonFile(AppPaths.UserConfigFile, optional: true, reloadOnChange: true);

        builder.Services.Configure<TodoListMcpOptions>(
            builder.Configuration.GetSection(TodoListMcpOptions.SectionName));

        var options = builder.Configuration
            .GetSection(TodoListMcpOptions.SectionName)
            .Get<TodoListMcpOptions>() ?? new TodoListMcpOptions();

        var scheme = options.UseHttps ? "https" : "http";
        var url = $"{scheme}://localhost:{options.Port}";

        builder.Host.UseSerilog();

        X509Certificate2? certificate = null;
        if (options.UseHttps)
        {
            certificate = CertificateManager.GetOrCreate(AppPaths.CertificateFile, Log.Logger);
            if (options.TrustCertificate)
                CertificateManager.EnsureTrusted(certificate, Log.Logger);
        }

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            void Configure(ListenOptions listen)
            {
                if (certificate is not null) listen.UseHttps(certificate);
            }
            // Loopback only (IPv4 + IPv6) — never exposed beyond this machine.
            kestrel.Listen(IPAddress.Loopback, options.Port, Configure);
            kestrel.Listen(IPAddress.IPv6Loopback, options.Port, Configure);
        });

        builder.Services.AddSingleton<TodoListManager>();
        builder.Services
            .AddMcpServer()
            .WithHttpTransport(o => o.Stateless = true)
            .WithTools<TodoTools>();

        var app = builder.Build();
        app.MapMcp();

        try
        {
            app.StartAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to start the MCP server on {Url}.", url);
            MessageBox.Show(
                $"Could not start the MCP server on {url}.\n\n{ex.Message}\n\n" +
                "The port may already be in use; change it in the configuration file.",
                "TodoList MCP", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Log.Information("TodoList MCP listening on {Url} (endpoint: {Url}/).", url, url);

        using var tray = new TrayApplicationContext(
            url,
            AppPaths.UserConfigFile,
            AppPaths.LogDir,
            app.Services.GetRequiredService<IOptionsMonitor<TodoListMcpOptions>>(),
            certificate);

        Application.Run(tray);

        app.StopAsync().GetAwaiter().GetResult();
    }
}
