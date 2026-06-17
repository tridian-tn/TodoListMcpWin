using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;

namespace TodoListMcp.App.Tray;

/// <summary>
/// Owns the system-tray icon and its menu. The MCP HTTP server runs in the background
/// for the lifetime of this context; exiting the menu shuts the whole app down.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly string _url;
    private readonly string _configFile;
    private readonly string _logDir;
    private readonly IOptionsMonitor<TodoListMcpOptions> _options;
    private readonly X509Certificate2? _certificate;
    private ToolStripMenuItem? _startupItem;

    public TrayApplicationContext(
        string url,
        string configFile,
        string logDir,
        IOptionsMonitor<TodoListMcpOptions> options,
        X509Certificate2? certificate)
    {
        _url = url;
        _configFile = configFile;
        _logDir = logDir;
        _options = options;
        _certificate = certificate;

        _icon = new NotifyIcon
        {
            Icon = TrayIconFactory.Create(),
            Visible = true,
            Text = Truncate($"TodoList MCP — {url}", 63),
            ContextMenuStrip = BuildMenu(),
        };
        _icon.DoubleClick += (_, _) => OpenConfig();
        _icon.ShowBalloonTip(3000, "TodoList MCP",
            $"Serving {ListCount()} list(s) on {url}", ToolTipIcon.Info);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem($"MCP server: {_url}") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());

        var lists = new ToolStripMenuItem("Configured lists");
        menu.Items.Add(lists);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Copy server URL", null, (_, _) => SafeSetClipboard(_url));
        menu.Items.Add("Open configuration…", null, (_, _) => OpenConfig());
        menu.Items.Add("Open log folder…", null, (_, _) => OpenPath(_logDir));
        if (_certificate is not null)
            menu.Items.Add("Trust HTTPS certificate (for Claude)…", null, (_, _) => TrustCertificate());

        menu.Items.Add(new ToolStripSeparator());
        _startupItem = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        _startupItem.Click += OnToggleStartup;
        menu.Items.Add(_startupItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        // Refresh dynamic state each time the menu opens.
        menu.Opening += (_, _) =>
        {
            RebuildListsMenu(lists);
            _startupItem.Checked = StartupManager.IsEnabled();
        };
        return menu;
    }

    private void RebuildListsMenu(ToolStripMenuItem parent)
    {
        parent.DropDownItems.Clear();
        var files = _options.CurrentValue.Files;
        if (files.Count == 0)
        {
            parent.DropDownItems.Add(new ToolStripMenuItem("(none configured)") { Enabled = false });
            return;
        }

        foreach (var f in files)
        {
            var label = f.Default ? $"{f.Alias}  ★" : f.Alias;
            var path = f.Path;
            var item = new ToolStripMenuItem($"{label}  —  {path}");
            item.Click += (_, _) => RevealInExplorer(path);
            parent.DropDownItems.Add(item);
        }
    }

    private void TrustCertificate()
    {
        if (_certificate is null) return;
        if (CertificateManager.IsTrusted(_certificate))
        {
            MessageBox.Show("The HTTPS certificate is already trusted.", "TodoList MCP",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        // May raise a one-time Windows consent prompt for installing a root certificate.
        var ok = CertificateManager.EnsureTrusted(_certificate, Serilog.Log.Logger);
        MessageBox.Show(
            ok ? "The HTTPS certificate is now trusted. Claude can connect over HTTPS."
               : "The certificate could not be trusted. See the log folder for details.",
            "TodoList MCP", MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void OnToggleStartup(object? sender, EventArgs e)
    {
        // CheckOnClick has already flipped Checked to the desired state.
        var desired = _startupItem!.Checked;
        try
        {
            StartupManager.SetEnabled(desired);
        }
        catch (Exception ex)
        {
            _startupItem.Checked = StartupManager.IsEnabled(); // revert to reality
            MessageBox.Show($"Could not update the 'Start with Windows' setting.\n\n{ex.Message}",
                "TodoList MCP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private int ListCount() => _options.CurrentValue.Files.Count;

    private void OpenConfig() => OpenPath(_configFile);

    private static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // Opening a file/folder is best-effort; nothing actionable if the shell refuses.
        }
    }

    private static void RevealInExplorer(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Path.GetDirectoryName(filePath)}\"") { UseShellExecute = true });
        }
        catch
        {
            // best-effort
        }
    }

    private static void SafeSetClipboard(string text)
    {
        try
        {
            if (!string.IsNullOrEmpty(text)) Clipboard.SetText(text);
        }
        catch
        {
            // Clipboard can transiently fail; not worth surfacing.
        }
    }

    private void ExitApp()
    {
        _icon.Visible = false;
        ExitThread();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    protected override void Dispose(bool disposing)
    {
        if (disposing) _icon.Dispose();
        base.Dispose(disposing);
    }
}
