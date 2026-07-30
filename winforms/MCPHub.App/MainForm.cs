using MCPHub.Core;

namespace MCPHub.App;

public sealed class MainForm : Form
{
    private readonly CommandRunner _runner = new();
    private readonly HttpClient _http = new();
    private readonly HubService _hubs;
    private readonly YouTubeService _youtube;
    private readonly ExtensionDispatcher _dispatcher;
    private readonly NamedPipeBackend _pipe;
    private readonly Panel _content = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<string, Control> _views = new();
    private readonly Dictionary<string, Button> _navigation = new();
    private readonly CancellationTokenSource _lifetime = new();
    private bool _closing;

    public MainForm()
    {
        Text = "MCPHub"; Width = 1280; Height = 820; MinimumSize = new(900, 600); StartPosition = FormStartPosition.CenterScreen;
        _hubs = new(_runner); _youtube = new(_runner, _http); _dispatcher = new(_youtube); _pipe = new(_dispatcher); _pipe.Start();
        BuildLayout();
        SelectView("Windows");
        _ = StartWindowsHubAsync();
        FormClosing += OnClosing;
    }

    private async Task StartWindowsHubAsync()
    {
        try { await _hubs.StartAsync(HubTarget.Windows, "start", _lifetime.Token); }
        catch (Exception error) { File.WriteAllText(Path.Combine(Path.GetTempPath(), "mcphub-startup-error.log"), error.ToString()); }
    }

    private void BuildLayout()
    {
        var navigation = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 178, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new(8), BackColor = Color.FromArgb(238, 240, 243) };
        navigation.Controls.Add(new Label { Text = "MCPHub", Font = new Font(Control.DefaultFont.FontFamily, 15, FontStyle.Bold), AutoSize = true, Margin = new(8, 10, 3, 14) });
        var dashboards = new DashboardPanel(_hubs, new EndpointService(_http), _lifetime.Token);
        AddView("Windows", dashboards, navigation); AddView("WSL", dashboards, navigation);
        AddView("ADB", new AdbPanel(new(_runner), _lifetime.Token), navigation);
        AddView("YouTube", new YouTubePanel(_youtube, _dispatcher, _lifetime.Token), navigation);
        AddView("Clipboard", new ClipboardPanel(new(_runner), _lifetime.Token), navigation);
        navigation.Controls.Add(new Label { Text = "Native C# backend\n.NET 10 · win-x64", AutoSize = true, ForeColor = Color.DimGray, Margin = new(8, 22, 3, 3) });
        Controls.Add(_content); Controls.Add(navigation);
    }

    private void AddView(string name, Control view, Control navigation)
    {
        _views[name] = view; view.Dock = DockStyle.Fill; view.Visible = false; if (!_content.Controls.Contains(view)) _content.Controls.Add(view);
        var button = Ui.Button(name, (_, _) => SelectView(name), 152); button.TextAlign = ContentAlignment.MiddleLeft; navigation.Controls.Add(button); _navigation[name] = button;
    }

    private void SelectView(string name)
    {
        foreach (var view in _views.Values.Distinct()) view.Visible = false;
        foreach (var button in _navigation.Values) button.Font = Control.DefaultFont;
        var selected = _views[name]; selected.Visible = true; selected.BringToFront(); _navigation[name].Font = new Font(Control.DefaultFont, FontStyle.Bold);
        if (selected is DashboardPanel dashboard) dashboard.SelectTarget(name == "Windows" ? HubTarget.Windows : HubTarget.Wsl);
    }

    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closing) return; e.Cancel = true; _closing = true; Enabled = false; Text = "MCPHub · stopping processes…"; _lifetime.Cancel();
        await _pipe.DisposeAsync(); await _hubs.DisposeAsync(); _http.Dispose(); _lifetime.Dispose();
        FormClosing -= OnClosing; Close();
    }
}
