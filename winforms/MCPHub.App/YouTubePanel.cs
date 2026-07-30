using System.Diagnostics;
using MCPHub.Core;

namespace MCPHub.App;

internal sealed class YouTubePanel : UserControl
{
    private readonly YouTubeService _service; private readonly ExtensionDispatcher _dispatcher; private readonly CancellationToken _token;
    private readonly TextBox _url = new() { Width = 620 }; private readonly CheckedListBox _chapters = new() { Dock = DockStyle.Fill, CheckOnClick = true };
    private readonly TextBox _custom = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f) };
    private readonly TextBox _activity = Ui.LogBox(); private readonly Label _status = Ui.Label("Ready"); private readonly ComboBox _playlists = new() { Width = 280, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _playlistTitle = new() { Width = 180 }; private readonly ComboBox _privacy = new() { Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly List<YouTubeChapter> _items = []; private YouTubeVideoInfo? _video; private YouTubeProcessResult? _result; private IReadOnlyList<YouTubePlaylist> _playlistItems = [];
    public YouTubePanel(YouTubeService service, ExtensionDispatcher dispatcher, CancellationToken token)
    {
        _service = service; _dispatcher = dispatcher; _token = token; _privacy.Items.AddRange(["private", "unlisted", "public"]); _privacy.SelectedIndex = 0;
        var fetch = Ui.Row(); fetch.Controls.Add(Ui.Label("YouTube Chapter Clipper", true)); fetch.Controls.Add(_url); fetch.Controls.Add(Ui.Button("Fetch", async (_, _) => await Fetch(), 68)); fetch.Controls.Add(Ui.Button("Tools", (_, _) => Tools(), 64)); fetch.Controls.Add(_status);
        var chapterTools = Ui.Row(); chapterTools.Controls.AddRange([Ui.Button("All", (_, _) => SetAll(true), 52), Ui.Button("Clear", (_, _) => SetAll(false), 56), Ui.Button("Parse custom", (_, _) => ParseCustom(), 100), Ui.Button("Download & cut", async (_, _) => await Process(), 116)]);
        var chapterSplit = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 480 }; chapterSplit.Panel1.Controls.Add(Ui.Group("Selected chapters", _chapters)); chapterSplit.Panel2.Controls.Add(Ui.Group("Custom timestamps: Name: 0:26 - 0:56", _custom));
        var media = new Panel { Dock = DockStyle.Fill }; media.Controls.Add(chapterSplit); media.Controls.Add(chapterTools);

        var account = Ui.Row(); account.Controls.AddRange([Ui.Button("Connect", async (_, _) => await Connect(), 76), Ui.Button("Refresh playlists", async (_, _) => await LoadPlaylists(), 112)]); account.Controls.Add(_playlists); account.Controls.Add(Ui.Label("New")); account.Controls.Add(_playlistTitle); account.Controls.Add(_privacy); account.Controls.Add(Ui.Button("Create", async (_, _) => await CreatePlaylist(), 68)); account.Controls.Add(Ui.Button("Upload clips", async (_, _) => await Upload(), 98)); account.Controls.Add(Ui.Button("Disconnect", (_, _) => { _service.Disconnect(); _status.Text = "YouTube disconnected."; }, 86));
        var tabs = new TabControl { Dock = DockStyle.Fill }; var workflow = new TabPage("Clips"); workflow.Controls.Add(media); var extension = new TabPage("Extension activity"); extension.Controls.Add(_activity); tabs.TabPages.Add(workflow); tabs.TabPages.Add(extension);
        Controls.Add(tabs); Controls.Add(account); Controls.Add(fetch);
        Load += async (_, _) => { Tools(); try { if ((await _service.GetAuthStatusAsync(token)).Connected) await LoadPlaylists(); } catch { } UpdateLogs(); };
        _dispatcher.LogsChanged += UpdateLogs; _service.ExtensionVideoReceived += info => BeginInvoke(() => LoadVideo(info, $"Loaded {info.Chapters.Count} chapters from Chrome")); Disposed += (_, _) => _dispatcher.LogsChanged -= UpdateLogs;
    }
    private void Tools() { var tools = _service.GetToolsStatus(); _status.Text = tools.YtDlp && tools.Ffmpeg ? $"Tools ready · {tools.OutputDir}" : "yt-dlp or ffmpeg missing"; }
    private async Task Fetch() { try { _status.Text = "Reading metadata…"; LoadVideo(await _service.GetVideoInfoAsync(_url.Text, _token), "Video loaded"); } catch (Exception e) { Ui.Error(this, e); _status.Text = e.Message; } }
    private void LoadVideo(YouTubeVideoInfo video, string status) { _video = video; _url.Text = $"https://www.youtube.com/watch?v={video.Id}"; _items.Clear(); _items.AddRange(video.Chapters); _chapters.Items.Clear(); foreach (var chapter in _items) _chapters.Items.Add($"{chapter.Index:00} · {chapter.Title} · {Format(chapter.StartTime)}-{Format(chapter.EndTime)}", true); _result = null; _status.Text = $"{status} · {video.Title}"; }
    private void ParseCustom() { var values = TimestampParser.Parse(_custom.Text); if (_video is null) { Ui.Error(this, new InvalidOperationException("Fetch a video before adding timestamps.")); return; } _items.Clear(); _items.AddRange(values); _chapters.Items.Clear(); foreach (var chapter in values) _chapters.Items.Add($"{chapter.Index:00} · {chapter.Title} · {Format(chapter.StartTime)}-{Format(chapter.EndTime)}", true); _status.Text = $"Parsed {values.Count} custom clips."; }
    private void SetAll(bool value) { for (var i = 0; i < _chapters.Items.Count; i++) _chapters.SetItemChecked(i, value); }
    private List<YouTubeChapter> Selected() => _chapters.CheckedIndices.Cast<int>().Select(i => _items[i]).ToList();
    private async Task Process() { if (_video is null) return; try { _status.Text = "Downloading and cutting…"; _result = await _service.ProcessVideoAsync(_url.Text, Selected(), _token); _status.Text = $"Created {_result.Clips.Count} clips · {_result.OutputDir}"; } catch (Exception e) { Ui.Error(this, e); _status.Text = e.Message; } }
    private async Task Connect() { try { _status.Text = "Waiting for account connection…"; var account = await _service.AuthenticateAsync(_token); _status.Text = $"Connected · {account.ChannelTitle}"; await LoadPlaylists(); } catch (Exception e) { Ui.Error(this, e); _status.Text = e.Message; } }
    private async Task LoadPlaylists() { try { _playlistItems = await _service.GetPlaylistsAsync(_token); _playlists.DataSource = _playlistItems.ToList(); _playlists.DisplayMember = nameof(YouTubePlaylist.Title); _status.Text = $"Loaded {_playlistItems.Count} playlists."; } catch (Exception e) { Ui.Error(this, e); } }
    private async Task CreatePlaylist() { if (string.IsNullOrWhiteSpace(_playlistTitle.Text)) return; try { var item = await _service.CreatePlaylistAsync(_playlistTitle.Text, "", _privacy.SelectedItem?.ToString() ?? "private", _token); await LoadPlaylists(); _playlists.SelectedItem = _playlistItems.FirstOrDefault(x => x.Id == item.Id); } catch (Exception e) { Ui.Error(this, e); } }
    private async Task Upload() { if (_result is null || _playlists.SelectedItem is not YouTubePlaylist playlist) return; try { _status.Text = "Uploading clips…"; var clips = _result.Clips.Select(c => new YouTubeUploadClip(c.Title, c.FilePath, $"Clipped from {_result.Title}")).ToList(); var uploaded = await _service.UploadClipsAsync(playlist.Id, clips, _token); _status.Text = $"Uploaded {uploaded.Clips.Count} clips."; if (uploaded.Clips.Count > 0) System.Diagnostics.Process.Start(new ProcessStartInfo(uploaded.Clips[0].Url) { UseShellExecute = true }); } catch (Exception e) { Ui.Error(this, e); _status.Text = e.Message; } }
    private void UpdateLogs() { if (IsDisposed) return; if (InvokeRequired) { BeginInvoke(UpdateLogs); return; } _activity.Text = string.Join(Environment.NewLine, _dispatcher.Logs.Select(x => $"{x.Timestamp}  {x.Level,-7} {x.Message}")); }
    private static string Format(double seconds) => TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
}
