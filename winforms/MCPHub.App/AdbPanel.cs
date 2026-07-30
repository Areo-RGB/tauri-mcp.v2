using MCPHub.Core;

namespace MCPHub.App;

internal sealed class AdbPanel : UserControl
{
    private readonly AdbService _service; private readonly CancellationToken _token; private readonly CheckedListBox _devices = new() { Dock = DockStyle.Fill, CheckOnClick = true, AccessibleName = "Connected Android devices", AccessibleDescription = "Select one or more ready devices." };
    private readonly TextBox _output = Ui.LogBox(); private readonly TextBox _apk = new() { Width = 520, AccessibleName = "APK file path" }; private readonly CheckBox _screenOff = new() { Text = "Start mirrors with screen off", AutoSize = true, Margin = new(8), AccessibleName = "Start mirrors with screen off" };
    private readonly List<AdbDevice> _items = [];
    public AdbPanel(AdbService service, CancellationToken token)
    {
        _service = service; _token = token; var top = Ui.Row(); top.Controls.Add(Ui.Label("Android / ADB / Scrcpy", true));
        top.Controls.AddRange([Ui.Button("&Refresh", async (_, _) => await RefreshDevices()), Ui.Button("Check &Scrcpy", async (_, _) => await Run(() => _service.GetScrcpyVersionAsync(token))), Ui.Button("&Mirror", async (_, _) => await Run(() => _service.StartMirrorsAsync(Selected(), _screenOff.Checked))), Ui.Button("&Screenshots", async (_, _) => await Run(() => _service.TakeScreenshotsAsync(Selected(), token))), Ui.Button("Export &specs", async (_, _) => await Run(() => _service.ExportSpecsAsync(Selected(), token)))]); top.Controls.Add(_screenOff);
        var apkRow = Ui.Row(); apkRow.Controls.Add(Ui.Label("APK")); apkRow.Controls.Add(_apk); apkRow.Controls.Add(Ui.Button("&Browse", (_, _) => Browse())); apkRow.Controls.Add(Ui.Button("&Install", async (_, _) => await Run(() => _service.InstallApkAsync(_apk.Text, Selected(), token))));
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 300 }; split.Panel1.Controls.Add(Ui.Group("Connected devices", _devices)); split.Panel2.Controls.Add(Ui.Group("Command output", _output));
        Controls.Add(split); Controls.Add(apkRow); Controls.Add(top); Load += async (_, _) => await RefreshDevices();
    }
    private async Task RefreshDevices() { try { var values = await _service.GetDevicesAsync(_token); _items.Clear(); _items.AddRange(values); _devices.Items.Clear(); foreach (var d in values) _devices.Items.Add($"{d.Model} ({d.Serial}) · {d.Device} · {d.State}", false); _output.Text = $"Found {values.Count(x => x.State == "device")} ready device(s)."; } catch (Exception e) { Ui.Error(this, e); } }
    private IEnumerable<string> Selected() => _devices.CheckedIndices.Cast<int>().Select(i => _items[i]).Where(x => x.State == "device").Select(x => x.Serial);
    private async Task Run(Func<Task<AdbCommandResult>> action) { try { var result = await action(); _output.Text = string.Join(Environment.NewLine, result.Lines); } catch (Exception e) { Ui.Error(this, e); _output.Text = e.Message; } }
    private void Browse() { using var dialog = new OpenFileDialog { Filter = "Android packages (*.apk)|*.apk", CheckFileExists = true }; if (dialog.ShowDialog(this) == DialogResult.OK) _apk.Text = dialog.FileName; }
}
