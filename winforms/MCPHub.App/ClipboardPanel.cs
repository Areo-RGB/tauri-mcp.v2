using MCPHub.Core;

namespace MCPHub.App;

internal sealed class ClipboardPanel : UserControl
{
    private sealed record Entry(string Text, string Extension, DateTime CapturedAt) { public override string ToString() => $".{Extension} · {CapturedAt:HH:mm:ss} · {Text.ReplaceLineEndings(" ").Trim()[..Math.Min(48, Text.Trim().Length)]}"; }
    private readonly ClipboardService _service; private readonly CancellationToken _token; private readonly TextBox _editor = new() { Dock = DockStyle.Fill, Multiline = true, AcceptsTab = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 10f), AccessibleName = "Clipboard editor" };
    private readonly ListBox _history = new() { Dock = DockStyle.Fill, AccessibleName = "Clipboard session history" }; private readonly Label _status = Ui.Label("Reading the Windows clipboard…"); private readonly Label _type = Ui.Label(".txt", true); private readonly CheckBox _live = new() { Text = "Live", Checked = true, AutoSize = true, Margin = new(8), AccessibleName = "Live clipboard polling" };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 }; private string? _lastClipboard; private bool _busy;
    public ClipboardPanel(ClipboardService service, CancellationToken token)
    {
        _service = service; _token = token; var toolbar = Ui.Row(); toolbar.Controls.Add(Ui.Label("Clipboard Saver", true)); toolbar.Controls.Add(_type);
        toolbar.Controls.AddRange([Ui.Button("Save to &Desktop", (_, _) => Save(), 118), Ui.Button("&Run", async (_, _) => await RunCode(), 64), Ui.Button("&Copy", (_, _) => CopyBack(), 64), Ui.Button("&Refresh", (_, _) => RefreshClipboard(true), 74)]); toolbar.Controls.Add(_live); toolbar.Controls.Add(_status);
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 840 }; split.Panel1.Controls.Add(_editor); split.Panel2.Controls.Add(Ui.Group("Session history", _history)); Controls.Add(split); Controls.Add(toolbar);
        _editor.TextChanged += (_, _) => { _type.Text = "." + ClipboardService.DetectExtension(_editor.Text); }; _history.DoubleClick += (_, _) => { if (_history.SelectedItem is Entry e) _editor.Text = e.Text; };
        Load += (_, _) => { RefreshClipboard(true); _timer.Start(); }; _timer.Tick += (_, _) => { if (_live.Checked && !_busy && _editor.Text == _lastClipboard) RefreshClipboard(false); }; Disposed += (_, _) => _timer.Dispose();
    }
    private void RefreshClipboard(bool force) { try { var text = Clipboard.ContainsText() ? Clipboard.GetText() : ""; if (force || text != _lastClipboard) { _editor.Text = text; _lastClipboard = text; Remember(text); _status.Text = text.Length == 0 ? "Clipboard is empty." : $"Ready · {text.Length:N0} characters"; } } catch (Exception e) { _status.Text = e.Message; } }
    private void Remember(string text) { if (text.Length == 0 || _history.Items.Cast<Entry>().Any(x => x.Text == text)) return; _history.Items.Insert(0, new Entry(text, ClipboardService.DetectExtension(text), DateTime.Now)); while (_history.Items.Count > 20) _history.Items.RemoveAt(_history.Items.Count - 1); }
    private void Save() { try { var result = _service.Save(_editor.Text); _status.Text = "Saved: " + result.Path; } catch (Exception e) { Ui.Error(this, e); } }
    private void CopyBack() { try { Clipboard.SetText(_editor.Text); _lastClipboard = _editor.Text; Remember(_editor.Text); _status.Text = "Editor content copied to the Windows clipboard."; } catch (Exception e) { Ui.Error(this, e); } }
    private async Task RunCode() { _busy = true; try { var result = await _service.RunAsync(_editor.Text, _token); _editor.Text = result.Output; Clipboard.SetText(result.Output); _lastClipboard = result.Output; Remember(result.Output); _status.Text = $"Run complete · exit {result.ExitCode} · {result.Path}"; } catch (Exception e) { Ui.Error(this, e); _status.Text = e.Message; } finally { _busy = false; } }
}
