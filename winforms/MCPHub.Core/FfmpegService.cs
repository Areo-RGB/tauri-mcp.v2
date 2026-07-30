namespace MCPHub.Core;

public sealed class FfmpegService
{
    private readonly CommandRunner _runner;
    private CancellationTokenSource? _convertCts;

    public FfmpegService(CommandRunner runner) => _runner = runner;

    /// <summary>Returns ffmpeg version string or throws if not found.</summary>
    public async Task<string> GetVersionAsync(CancellationToken ct)
    {
        var result = await _runner.RunAsync("ffmpeg", "-version", ct);
        var first = result.Lines.FirstOrDefault() ?? string.Empty;
        return string.IsNullOrWhiteSpace(first)
            ? "ffmpeg not found on PATH. Place ffmpeg.exe next to the app or install it system-wide."
            : first;
    }

    /// <summary>Streams ffmpeg output lines while converting.</summary>
    public async IAsyncEnumerable<string> ConvertAsync(
        string inputPath,
        string ffmpegArgs,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        _convertCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var combined = _convertCts.Token;

        // ffmpeg writes progress to stderr; capture both
        var result = await _runner.RunAsync("ffmpeg", ffmpegArgs, combined);
        foreach (var line in result.Lines)
            yield return line;
    }

    /// <summary>Cancels an in-progress conversion.</summary>
    public void Cancel() => _convertCts?.Cancel();
}
