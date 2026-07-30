using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MCPHub.Core;

public sealed partial class YouTubeService(ICommandRunner runner, HttpClient client)
{
    private const string AuthUrl = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenUrl = "https://oauth2.googleapis.com/token";
    private const string ApiUrl = "https://www.googleapis.com/youtube/v3";
    private const string UploadUrl = "https://www.googleapis.com/upload/youtube/v3";
    private const string Scopes = "https://www.googleapis.com/auth/youtube https://www.googleapis.com/auth/youtube.upload";
    private static string TokenPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MCPHub", "youtube-token.json");
    public event Action<YouTubeVideoInfo>? ExtensionVideoReceived;

    public YouTubeToolsStatus GetToolsStatus() => new(FindYtDlp() is not null, runner.FindExecutable("ffmpeg.exe", "ffmpeg") is not null,
        runner.FindExecutable("ffprobe.exe", "ffprobe") is not null, DriveDirectory());

    public async Task<YouTubeVideoInfo> GetVideoInfoAsync(string url, CancellationToken token = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new ArgumentException("Enter a valid YouTube URL.");
        var yt = FindYtDlp() ?? throw new FileNotFoundException("yt-dlp was not found. Install it or place yt-dlp.exe in the YouTube backend folder.");
        var args = new List<string> { "--dump-single-json", "--skip-download", "--no-playlist", "--no-warnings" }; AddAccessArgs(args); args.Add(url);
        var result = await runner.RunAsync(yt, args, cancellationToken: token); Ensure(result, "yt-dlp metadata");
        return ParseVideoInfo(result.Stdout);
    }

    public static YouTubeVideoInfo ParseVideoInfo(string json)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        var duration = Number(root, "duration"); var chapters = new List<YouTubeChapter>();
        if (root.TryGetProperty("chapters", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                var start = Number(item, "start_time"); var end = item.TryGetProperty("end_time", out var endValue) && endValue.TryGetDouble(out var e) ? e : duration;
                if (end <= start) continue;
                chapters.Add(new(chapters.Count + 1, String(item, "title", "Chapter"), start, end, end - start));
            }
        }
        return new(String(root, "id"), String(root, "title", "YouTube video"), duration, String(root, "uploader"), String(root, "thumbnail"), chapters);
    }

    public async Task<YouTubeProcessResult> ProcessVideoAsync(string url, IReadOnlyList<YouTubeChapter> chapters, CancellationToken token = default)
    {
        if (chapters.Count == 0) throw new ArgumentException("Select or add at least one chapter.");
        var info = await GetVideoInfoAsync(url, token); var folder = Path.Combine(VideoOutputDirectory(), SafeName(info.Title, "YouTube_Video"));
        var clipsDir = Path.Combine(folder, "clips"); Directory.CreateDirectory(clipsDir);
        var yt = FindYtDlp()!; var template = Path.Combine(folder, "source.%(ext)s");
        var args = new List<string> { "--no-playlist", "--no-warnings", "-f", "bestvideo+bestaudio/best", "--merge-output-format", "mp4", "--print", "after_move:filepath", "-o", template };
        AddAccessArgs(args); args.Add(url);
        var download = await runner.RunAsync(yt, args, cancellationToken: token); Ensure(download, "yt-dlp download");
        var videoPath = download.Stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Reverse().FirstOrDefault(File.Exists)
            ?? Directory.EnumerateFiles(folder, "*.mp4").FirstOrDefault() ?? throw new FileNotFoundException("yt-dlp finished but the downloaded MP4 could not be found.");
        var ffmpeg = runner.FindExecutable("ffmpeg.exe", "ffmpeg") ?? throw new FileNotFoundException("ffmpeg was not found on PATH.");
        var clips = new List<YouTubeClipResult>();
        foreach (var chapter in chapters.Where(c => c.EndTime > c.StartTime && c.Duration >= .5))
        {
            var index = clips.Count + 1; var path = Path.Combine(clipsDir, $"{index:00}_{SafeName(chapter.Title, "Chapter")}.mp4");
            var cut = await runner.RunAsync(ffmpeg, ["-y", "-hide_banner", "-loglevel", "error", "-ss", chapter.StartTime.ToString(CultureInfo.InvariantCulture), "-i", videoPath,
                "-t", (chapter.EndTime - chapter.StartTime).ToString(CultureInfo.InvariantCulture), "-c:v", "libx264", "-crf", "18", "-preset", "fast", "-c:a", "aac", "-b:a", "192k", "-pix_fmt", "yuv420p", "-movflags", "+faststart", path], cancellationToken: token);
            Ensure(cut, $"ffmpeg clip {index}"); clips.Add(new(index, chapter.Title, path, chapter.StartTime, chapter.EndTime, chapter.EndTime - chapter.StartTime));
        }
        if (clips.Count == 0) throw new InvalidOperationException("No valid clips were produced.");
        var destination = MoveToDrive(folder); var finalVideo = Path.Combine(destination, Path.GetFileName(videoPath));
        var finalClips = clips.Select(c => c with { FilePath = Path.Combine(destination, "clips", Path.GetFileName(c.FilePath)) }).ToList();
        return new(info.Title, finalVideo, Path.Combine(destination, "clips"), finalClips);
    }

    public async Task<YouTubeAuthStatus> AuthenticateAsync(CancellationToken token = default)
    {
        var (id, secret) = OAuthConfig(); using var listener = new HttpListener(); var port = FreePort(); var redirect = $"http://127.0.0.1:{port}/"; listener.Prefixes.Add(redirect); listener.Start();
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)); var auth = AuthUrl + "?" + Form(new Dictionary<string, string> { ["client_id"] = id, ["redirect_uri"] = redirect, ["response_type"] = "code", ["scope"] = Scopes, ["access_type"] = "offline", ["prompt"] = "consent", ["state"] = state });
        OpenUrl(auth); using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var context = await listener.GetContextAsync().WaitAsync(timeout.Token); var query = context.Request.QueryString;
        var ok = query["state"] == state && !string.IsNullOrWhiteSpace(query["code"]); var body = ok ? "YouTube is connected. You can close this tab and return to MCPHub." : "YouTube connection was not completed. You can close this tab.";
        var bytes = Encoding.UTF8.GetBytes(body); context.Response.ContentLength64 = bytes.Length; await context.Response.OutputStream.WriteAsync(bytes, token); context.Response.Close();
        if (!ok) throw new InvalidOperationException(query["error"] is { } error ? $"Google OAuth was not completed: {error}" : "Google OAuth callback was invalid.");
        using var response = await client.PostAsync(TokenUrl, new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = query["code"]!, ["client_id"] = id, ["client_secret"] = secret, ["redirect_uri"] = redirect, ["grant_type"] = "authorization_code" }), token);
        var value = await JsonResponse(response, "Google OAuth token exchange", token); var refresh = value.GetProperty("refresh_token").GetString() ?? throw new InvalidOperationException("Google did not return a refresh token. Try connecting again.");
        SaveToken(new StoredToken(value.GetProperty("access_token").GetString()!, refresh, value.TryGetProperty("token_type", out var type) ? type.GetString() ?? "Bearer" : "Bearer", DateTimeOffset.UtcNow.ToUnixTimeSeconds() + value.GetProperty("expires_in").GetInt64()));
        return await GetAuthStatusAsync(token);
    }

    public async Task<YouTubeAuthStatus> GetAuthStatusAsync(CancellationToken token = default)
    {
        if (!File.Exists(TokenPath)) return new(false, null);
        var access = await AccessTokenAsync(token); using var response = await SendAuthorizedAsync(HttpMethod.Get, $"{ApiUrl}/channels?part=snippet&mine=true", access, null, token);
        var value = await JsonResponse(response, "YouTube channel lookup", token); var title = value.GetProperty("items").EnumerateArray().FirstOrDefault().TryGetProperty("snippet", out var snippet) && snippet.TryGetProperty("title", out var name) ? name.GetString() : null;
        return new(true, title);
    }

    public void Disconnect() { if (File.Exists(TokenPath)) File.Delete(TokenPath); }

    public async Task<IReadOnlyList<YouTubePlaylist>> GetPlaylistsAsync(CancellationToken token = default)
    {
        var access = await AccessTokenAsync(token); var result = new List<YouTubePlaylist>(); string? page = null;
        do
        {
            var url = $"{ApiUrl}/playlists?part=snippet,contentDetails,status&mine=true&maxResults=50" + (page is null ? "" : $"&pageToken={Uri.EscapeDataString(page)}");
            using var response = await SendAuthorizedAsync(HttpMethod.Get, url, access, null, token); var value = await JsonResponse(response, "YouTube playlist lookup", token);
            foreach (var item in value.GetProperty("items").EnumerateArray()) result.Add(new(item.GetProperty("id").GetString()!, item.GetProperty("snippet").GetProperty("title").GetString()!, item.GetProperty("snippet").GetProperty("description").GetString() ?? "", item.GetProperty("status").GetProperty("privacyStatus").GetString() ?? "private", item.GetProperty("contentDetails").GetProperty("itemCount").GetUInt64()));
            page = value.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        } while (page is not null);
        return result;
    }

    public async Task<YouTubePlaylist> CreatePlaylistAsync(string title, string description, string privacy, CancellationToken token = default)
    {
        privacy = privacy is "private" or "unlisted" or "public" ? privacy : "private"; var access = await AccessTokenAsync(token);
        var payload = new { snippet = new { title = title.Trim(), description = description.Trim() }, status = new { privacyStatus = privacy } };
        using var response = await SendAuthorizedAsync(HttpMethod.Post, $"{ApiUrl}/playlists?part=snippet,status", access, JsonContent.Create(payload), token); var item = await JsonResponse(response, "YouTube playlist creation", token);
        return new(item.GetProperty("id").GetString()!, title.Trim(), description.Trim(), privacy, 0);
    }

    public async Task<YouTubeUploadResult> UploadClipsAsync(string playlistId, IReadOnlyList<YouTubeUploadClip> clips, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(playlistId)) throw new ArgumentException("Select a YouTube playlist first."); if (clips.Count == 0) throw new ArgumentException("Create clips before uploading them to YouTube.");
        var access = await AccessTokenAsync(token); var uploaded = new List<YouTubeUploadedClip>();
        foreach (var clip in clips)
        {
            await using var file = File.OpenRead(clip.FilePath); var init = new HttpRequestMessage(HttpMethod.Post, $"{UploadUrl}/videos?uploadType=resumable&part=snippet,status"); init.Headers.Authorization = new("Bearer", access); init.Headers.Add("X-Upload-Content-Type", "video/mp4"); init.Headers.Add("X-Upload-Content-Length", file.Length.ToString()); init.Content = JsonContent.Create(new { snippet = new { title = clip.Title, description = clip.Description ?? "" }, status = new { privacyStatus = "private" } });
            using var initResponse = await client.SendAsync(init, token); if (!initResponse.IsSuccessStatusCode) await ThrowResponse(initResponse, $"Starting the YouTube upload for {clip.Title}", token);
            var location = initResponse.Headers.Location ?? throw new InvalidOperationException("YouTube did not return an upload URL."); using var upload = new HttpRequestMessage(HttpMethod.Put, location) { Content = new StreamContent(file) }; upload.Headers.Authorization = new("Bearer", access); upload.Content.Headers.ContentType = new("video/mp4");
            using var uploadResponse = await client.SendAsync(upload, token); var video = await JsonResponse(uploadResponse, $"YouTube upload for {clip.Title}", token); var videoId = video.GetProperty("id").GetString()!;
            var add = new { snippet = new { playlistId, resourceId = new { kind = "youtube#video", videoId } } }; using var addResponse = await SendAuthorizedAsync(HttpMethod.Post, $"{ApiUrl}/playlistItems?part=snippet", access, JsonContent.Create(add), token); if (!addResponse.IsSuccessStatusCode) await ThrowResponse(addResponse, $"Adding {clip.Title} to the playlist", token);
            uploaded.Add(new(clip.Title, videoId, $"https://www.youtube.com/watch?v={videoId}"));
        }
        return new(playlistId, uploaded);
    }

    public void NotifyExtensionVideo(YouTubeVideoInfo info) => ExtensionVideoReceived?.Invoke(info);
    public static string SafeName(string value, string fallback)
    {
        var clean = InvalidName().Replace(value, ""); clean = Whitespace().Replace(clean.Trim(), "_"); var limited = new string(clean.Take(80).ToArray()).Trim('.', '_', '-'); return limited.Length == 0 ? fallback : limited;
    }

    private async Task<string> AccessTokenAsync(CancellationToken token)
    {
        var stored = JsonSerializer.Deserialize<StoredToken>(await File.ReadAllTextAsync(TokenPath, token), JsonDefaults.Options) ?? throw new InvalidOperationException("Saved YouTube token is invalid.");
        if (stored.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60) return stored.AccessToken;
        var (id, secret) = OAuthConfig(); using var response = await client.PostAsync(TokenUrl, new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = id, ["client_secret"] = secret, ["refresh_token"] = stored.RefreshToken, ["grant_type"] = "refresh_token" }), token);
        var value = await JsonResponse(response, "YouTube token refresh", token); var next = new StoredToken(value.GetProperty("access_token").GetString()!, stored.RefreshToken, value.TryGetProperty("token_type", out var type) ? type.GetString() ?? stored.TokenType : stored.TokenType, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + value.GetProperty("expires_in").GetInt64()); SaveToken(next); return next.AccessToken;
    }

    private static (string Id, string Secret) OAuthConfig()
    {
        var id = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID"); var secret = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret)) throw new InvalidOperationException("Google OAuth configuration is missing. Add it to .env."); return (id, secret);
    }
    private static void SaveToken(StoredToken token) { Directory.CreateDirectory(Path.GetDirectoryName(TokenPath)!); File.WriteAllText(TokenPath, JsonSerializer.Serialize(token, JsonDefaults.Options)); }
    private static int FreePort() { var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
    private static string Form(IDictionary<string, string> values) => string.Join('&', values.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
    private static void OpenUrl(string url) { var executable = File.Exists(AppConstants.ChromeExecutable) ? AppConstants.ChromeExecutable : url; Process.Start(new ProcessStartInfo(executable, executable == url ? "" : url) { UseShellExecute = true }); }
    private string? FindYtDlp() => File.Exists(AppConstants.YouTubeYtDlp) ? AppConstants.YouTubeYtDlp : runner.FindExecutable("yt-dlp.exe", "yt-dlp");
    private static string VideoOutputDirectory() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Chapter Clipper");
    private static string DriveDirectory() => Environment.GetEnvironmentVariable("YOUTUBE_DRIVE_DIR") ?? AppConstants.YouTubeDriveDir;
    private static void AddAccessArgs(List<string> args) { if (File.Exists(AppConstants.YouTubeCookies)) { args.Add("--cookies"); args.Add(AppConstants.YouTubeCookies); } args.Add("--add-header"); args.Add("User-Agent:Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/126 Safari/537.36"); }
    private static void Ensure(CommandOutput output, string label) { if (!output.Success) throw new InvalidOperationException($"{label} failed{(string.IsNullOrWhiteSpace(output.Stderr) ? "" : $": {output.Stderr}")}"); }
    private static double Number(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : 0;
    private static string String(JsonElement item, string name, string fallback = "") => item.TryGetProperty(name, out var value) ? value.GetString() ?? fallback : fallback;
    private static async Task<JsonElement> JsonResponse(HttpResponseMessage response, string label, CancellationToken token) { if (!response.IsSuccessStatusCode) await ThrowResponse(response, label, token); return (await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token)).RootElement.Clone(); }
    private static async Task ThrowResponse(HttpResponseMessage response, string label, CancellationToken token) { var body = await response.Content.ReadAsStringAsync(token); throw new InvalidOperationException($"{label} failed ({(int)response.StatusCode}): {body}"); }
    private async Task<HttpResponseMessage> SendAuthorizedAsync(HttpMethod method, string url, string access, HttpContent? content, CancellationToken token) { var request = new HttpRequestMessage(method, url) { Content = content }; request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access); return await client.SendAsync(request, token); }
    private static string MoveToDrive(string source)
    {
        var drive = DriveDirectory(); Directory.CreateDirectory(drive); var destination = Path.Combine(drive, Path.GetFileName(source)); var suffix = 2; while (Directory.Exists(destination)) destination = Path.Combine(drive, $"{Path.GetFileName(source)}_{suffix++}");
        try { Directory.Move(source, destination); } catch (IOException) { CopyDirectory(source, destination); Directory.Delete(source, true); } return destination;
    }
    private static void CopyDirectory(string source, string destination) { Directory.CreateDirectory(destination); foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file))); foreach (var directory in Directory.EnumerateDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory))); }
    private sealed record StoredToken(string AccessToken, string RefreshToken, string TokenType, long ExpiresAt);
    [GeneratedRegex("[<>:\"\\\\/|?*\\x00-\\x1f]")] private static partial Regex InvalidName();
    [GeneratedRegex("[\\s_-]+")] private static partial Regex Whitespace();
}
