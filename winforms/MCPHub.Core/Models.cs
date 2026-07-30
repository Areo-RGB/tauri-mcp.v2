using System.Text.Json.Serialization;

namespace MCPHub.Core;

public sealed record AdbDevice(string Serial, string Model, string Device, string State);
public sealed record AdbCommandResult(bool Ok, string Command, string Stdout, string Stderr,
    IReadOnlyList<string> Lines, int? ExitCode, IReadOnlyList<string> Paths);
public sealed record ClipboardSnapshot(string Content, string Extension);
public sealed record ClipboardSaveResult(string Path, string Extension);
public sealed record ClipboardRunResult(string Output, string Extension, int ExitCode, string Path);
public sealed record YouTubeToolsStatus(bool YtDlp, bool Ffmpeg, bool Ffprobe, string OutputDir);
public sealed record YouTubeChapter(int Index, string Title, double StartTime, double EndTime, double Duration)
{
    [JsonIgnore] public bool Selected { get; set; } = true;
}
public sealed record YouTubeVideoInfo(string Id, string Title, double Duration, string Uploader, string Thumbnail,
    IReadOnlyList<YouTubeChapter> Chapters);
public sealed record YouTubeClipResult(int Index, string Title, string FilePath, double StartTime, double EndTime, double Duration);
public sealed record YouTubeProcessResult(string Title, string VideoPath, string OutputDir, IReadOnlyList<YouTubeClipResult> Clips);
public sealed record YouTubePlaylist(string Id, string Title, string Description, string PrivacyStatus, ulong ItemCount);
public sealed record YouTubeAuthStatus(bool Connected, string? ChannelTitle);
public sealed record YouTubeUploadClip(string Title, string FilePath, string? Description);
public sealed record YouTubeUploadedClip(string Title, string VideoId, string Url);
public sealed record YouTubeUploadResult(string PlaylistId, IReadOnlyList<YouTubeUploadedClip> Clips);
public sealed record ExtensionLogEntry(string Timestamp, string Level, string Message);

public sealed class ExtensionRequest
{
    public string? Action { get; set; }
    public string? Url { get; set; }
    public List<YouTubeChapter>? Chapters { get; set; }
    public string? PlaylistId { get; set; }
}
