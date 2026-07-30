using MCPHub.Core;

namespace MCPHub.App;

public sealed class MainForm : Form
{
    private readonly CommandRunner _runner = new();
    private readonly HttpClient _http = new();
    private readonly YouTubeService _youtube;
    private readonly ExtensionDispatcher _dispatcher;
    private readonly NamedPipeBackend _pipe;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _closing;

    public MainForm()
    {
        Text = "MCPHub"; Width = 1280; Height = 820; MinimumSize = new(900, 600); StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi; AccessibleRole = AccessibleRole.Window; AccessibleName = "MCPHub desktop control center"; AccessibleDescription = "ADB, YouTube clipping, and clipboard tools.";
        _youtube = new(_runner, _http); _dispatcher = new(_youtube); _pipe = new(_dispatcher); _pipe.Start();
        BuildLayout();
        FormClosing += OnClosing;
    }

    private void BuildLayout()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 62, Padding = new(16, 10, 16, 8), BackColor = Color.FromArgb(238, 240, 243), AccessibleRole = AccessibleRole.Grouping, AccessibleName = "Application header" };
        header.Controls.Add(new Label { Text = "MCPHub", Dock = DockStyle.Left, AutoSize = false, Width = 180, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Control.DefaultFont.FontFamily, 16, FontStyle.Bold), AccessibleRole = AccessibleRole.Text });
        header.Controls.Add(new Label { Text = "Native C# backend · .NET 10 · win-x64", Dock = DockStyle.Right, AutoSize = false, Width = 310, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.DimGray, AccessibleRole = AccessibleRole.Text });

        var tabs = new TabControl { Dock = DockStyle.Fill, Alignment = TabAlignment.Top, Multiline = false, Padding = new(18, 6), HotTrack = true, AccessibleRole = AccessibleRole.PageTabList, AccessibleName = "MCPHub tools" };
        AddTab(tabs, "ADB", new AdbPanel(new(_runner), _lifetime.Token));
        AddTab(tabs, "YouTube Clipper", new YouTubePanel(_youtube, _dispatcher, _lifetime.Token));
        AddTab(tabs, "Clipboard Saver", new ClipboardPanel(new(_runner), _lifetime.Token));
        Controls.Add(tabs); Controls.Add(header);
    }

    private static void AddTab(TabControl tabs, string title, Control content)
    {
        content.Dock = DockStyle.Fill;
        var page = new TabPage(title) { Padding = new(8), AccessibleName = title, AccessibleDescription = $"{title} tools" };
        page.Controls.Add(content);
        tabs.TabPages.Add(page);
    }

    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closing) return; e.Cancel = true; _closing = true; Enabled = false; Text = "MCPHub · stopping processes…"; _lifetime.Cancel();
        await _pipe.DisposeAsync(); _http.Dispose(); _lifetime.Dispose();
        FormClosing -= OnClosing; Close();
    }
}
