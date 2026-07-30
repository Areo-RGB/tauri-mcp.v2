using System.Diagnostics;
using MCPHub.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MCPHub.App;

internal sealed class DashboardPanel : UserControl
{
    private readonly HubService _hubs; private readonly EndpointService _endpoint; private readonly CancellationToken _token;
    private readonly WebView2 _windows = new() { Dock = DockStyle.Fill }; private readonly WebView2 _wsl = new() { Dock = DockStyle.Fill };
    private readonly Label _title = Ui.Label("", true); private readonly Label _status = Ui.Label("Stopped"); private readonly TextBox _logs = Ui.LogBox();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 }; private HubTarget _target;
    private readonly Button _build; private readonly Button _start; private readonly Button _restart; private readonly Button _stop;

    public DashboardPanel(HubService hubs, EndpointService endpoint, CancellationToken token)
    {
        _hubs = hubs; _endpoint = endpoint; _token = token;
        var toolbar = Ui.Row(); toolbar.Controls.Add(_title); toolbar.Controls.Add(_status);
        _build = Ui.Button("Build", async (_, _) => await Run(() => _hubs.StartAsync(_target, "build", token)), 72);
        _start = Ui.Button("Start", async (_, _) => await Run(() => _hubs.StartAsync(_target, "start", token)), 72);
        _restart = Ui.Button("Restart", async (_, _) => await Run(() => _hubs.RestartAsync(_target, token)), 76);
        _stop = Ui.Button("Stop", async (_, _) => await Run(() => _hubs.StopAsync(_target, token)), 68);
        toolbar.Controls.AddRange([_build, _start, _restart, _stop, Ui.Button("Reload", (_, _) => Active().Reload(), 72), Ui.Button("Browser", (_, _) => Process.Start(new ProcessStartInfo(Url()) { UseShellExecute = true }), 78)]);
        var webStack = new Panel { Dock = DockStyle.Fill }; webStack.Controls.Add(_windows); webStack.Controls.Add(_wsl);
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 540, Panel2MinSize = 100 }; split.Panel1.Controls.Add(webStack); split.Panel2.Controls.Add(_logs);
        Controls.Add(split); Controls.Add(toolbar);
        Load += async (_, _) => { await Initialize(_windows, "http://localhost:3000"); await Initialize(_wsl, "http://localhost:3001"); RefreshStatus(); _timer.Start(); };
        _timer.Tick += async (_, _) => { RefreshStatus(); if (_target == HubTarget.Wsl && DateTime.Now.Second % 10 < 2) { var check = await _endpoint.CheckAsync(AppConstants.SshWslUrl, token); _status.Text += check.Reachable ? $" · SSH {check.LatencyMs} ms" : " · SSH offline"; } };
        Disposed += (_, _) => _timer.Dispose();
    }

    public void SelectTarget(HubTarget target) { _target = target; _title.Text = target == HubTarget.Windows ? "Windows MCPHub · :3000" : "WSL MCPHub · :3001"; _windows.Visible = target == HubTarget.Windows; _wsl.Visible = target == HubTarget.Wsl; Active().BringToFront(); RefreshStatus(); }
    private WebView2 Active() => _target == HubTarget.Windows ? _windows : _wsl; private string Url() => _target == HubTarget.Windows ? "http://localhost:3000" : "http://localhost:3001";
    private static async Task Initialize(WebView2 view, string url) { try { await view.EnsureCoreWebView2Async(); view.CoreWebView2.Navigate(url); } catch { } }
    private async Task Run(Func<Task<HubProcessInfo>> action) { Toggle(false); try { await action(); } catch (Exception e) { Ui.Error(this, e); } finally { Toggle(true); RefreshStatus(); } }
    private void Toggle(bool enabled) { _build.Enabled = enabled; _start.Enabled = enabled; _restart.Enabled = enabled; _stop.Enabled = enabled; }
    private void RefreshStatus() { var info = _hubs.GetStatus(_target); _status.Text = info.Running ? $"Live · PID {info.Pid} · ngrok {(info.NgrokRunning ? "on" : "off")}" : $"Stopped · ngrok {(info.NgrokRunning ? "on" : "off")}"; _logs.Text = string.IsNullOrWhiteSpace(info.LogTail) ? "No process output yet." : info.LogTail; }
}
