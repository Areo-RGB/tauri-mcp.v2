using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;

namespace MCPHub.Core;

public static class NativeMessageFraming
{
    public static async Task<JsonDocument?> ReadAsync(Stream stream, CancellationToken token = default)
    {
        var lengthBytes = new byte[4]; var read = await ReadExactAsync(stream, lengthBytes, token); if (read == 0) return null;
        if (read != 4) throw new InvalidDataException("Native message length header was incomplete.");
        var length = BinaryPrimitives.ReadUInt32LittleEndian(lengthBytes); if (length is 0 or > 16 * 1024 * 1024) throw new InvalidDataException("Native message length was invalid.");
        var body = new byte[length]; if (await ReadExactAsync(stream, body, token) != length) throw new InvalidDataException("Native message body was incomplete.");
        return JsonDocument.Parse(body);
    }

    public static async Task WriteAsync(Stream stream, object response, CancellationToken token = default)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(response, JsonDefaults.Options); var length = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)body.Length);
        await stream.WriteAsync(length, token); await stream.WriteAsync(body, token); await stream.FlushAsync(token);
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken token)
    {
        var offset = 0; while (offset < buffer.Length) { var read = await stream.ReadAsync(buffer.AsMemory(offset), token); if (read == 0) break; offset += read; } return offset;
    }
}

public sealed class ExtensionDispatcher(YouTubeService youtube)
{
    private readonly Queue<ExtensionLogEntry> _logs = new();
    public event Action? LogsChanged;
    public IReadOnlyList<ExtensionLogEntry> Logs { get { lock (_logs) return _logs.ToList(); } }

    public async Task<object> DispatchAsync(ExtensionRequest request, CancellationToken token)
    {
        try
        {
            switch (request.Action)
            {
                case "ping": Log("success", "Extension status check succeeded"); return new { success = true, ready = true };
                case "fetch-chapters":
                    Log("info", "Fetching chapters with yt-dlp"); var info = await youtube.GetVideoInfoAsync(request.Url ?? "", token); youtube.NotifyExtensionVideo(info); Log("success", $"yt-dlp found {info.Chapters.Count} chapter(s)"); return new { success = true, result = info };
                case "get-playlists":
                    Log("info", "Loading YouTube playlists"); var playlists = await youtube.GetPlaylistsAsync(token); Log("success", $"Loaded {playlists.Count} YouTube playlist(s)"); return new { success = true, result = playlists };
                case "process-upload":
                    Log("info", $"Processing and uploading {request.Chapters?.Count ?? 0} chapter(s)"); var processed = await youtube.ProcessVideoAsync(request.Url ?? "", request.Chapters ?? [], token); var clips = processed.Clips.Select(c => new YouTubeUploadClip(c.Title, c.FilePath, $"Clipped from {processed.Title}")).ToList(); var uploaded = await youtube.UploadClipsAsync(request.PlaylistId ?? "", clips, token); Log("success", $"Uploaded {uploaded.Clips.Count} clip(s) to YouTube"); return new { success = true, result = new { processed, uploaded } };
                case "process":
                    Log("info", $"Processing {request.Chapters?.Count ?? 0} chapter(s)"); var result = await youtube.ProcessVideoAsync(request.Url ?? "", request.Chapters ?? [], token); Log("success", $"Created {result.Clips.Count} clip(s)"); return new { success = true, result };
                default: throw new InvalidOperationException("Unknown extension action.");
            }
        }
        catch (Exception error) { Log("error", error.Message); return new { success = false, error = error.Message }; }
    }

    private void Log(string level, string message)
    {
        lock (_logs) { _logs.Enqueue(new(DateTime.Now.ToString("HH:mm:ss"), level, message)); while (_logs.Count > 100) _logs.Dequeue(); }
        LogsChanged?.Invoke();
    }
}

public sealed class NamedPipeBackend(ExtensionDispatcher dispatcher) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    public void Start() => _loop ??= Task.Run(() => AcceptLoop(_stop.Token));

    private async Task AcceptLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(AppConstants.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.WaitForConnectionAsync(token); using var document = await NativeMessageFraming.ReadAsync(pipe, token); if (document is null) continue;
                var request = document.RootElement.Deserialize<ExtensionRequest>(JsonDefaults.Options) ?? throw new InvalidDataException("Extension request was empty.");
                await NativeMessageFraming.WriteAsync(pipe, await dispatcher.DispatchAsync(request, token), token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception error) { if (pipe.IsConnected) await NativeMessageFraming.WriteAsync(pipe, new { success = false, error = error.Message }, CancellationToken.None); }
        }
    }

    public async ValueTask DisposeAsync() { _stop.Cancel(); if (_loop is not null) { try { await _loop; } catch { } } _stop.Dispose(); }
}

public static class NativeHostRelay
{
    public static async Task RelayAsync(Stream input, Stream output, CancellationToken token = default)
    {
        using var message = await NativeMessageFraming.ReadAsync(input, token); if (message is null) return;
        try
        {
            await using var pipe = new NamedPipeClientStream(".", AppConstants.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            var action = message.RootElement.TryGetProperty("action", out var value) ? value.GetString() : null; var timeout = action is "process" or "process-upload" ? TimeSpan.FromMinutes(30) : TimeSpan.FromSeconds(60);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token); cts.CancelAfter(timeout); await pipe.ConnectAsync(TimeSpan.FromSeconds(3), cts.Token);
            await NativeMessageFraming.WriteAsync(pipe, message.RootElement, cts.Token); using var response = await NativeMessageFraming.ReadAsync(pipe, cts.Token);
            await NativeMessageFraming.WriteAsync(output, response?.RootElement ?? JsonSerializer.SerializeToElement(new { success = false, error = "MCPHub returned no response." }), token);
        }
        catch (Exception error) { await NativeMessageFraming.WriteAsync(output, new { success = false, error = $"Could not connect to the MCPHub desktop app: {error.Message}" }, token); }
    }
}
