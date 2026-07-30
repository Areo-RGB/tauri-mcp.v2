using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MCPHub.Core;
using Xunit;

namespace MCPHub.Tests;

public sealed class CoreTests
{
    [Fact]
    public void Hub_commands_are_scoped_by_target()
    {
        var windows = HubService.BuildCommand(HubTarget.Windows, "start", AppConstants.WindowsProjectDir);
        Assert.Equal("corepack", windows.FileName); Assert.Equal(["pnpm", "start"], windows.Arguments);
        var wsl = HubService.BuildCommand(HubTarget.Wsl, "build", AppConstants.WslProjectDir);
        Assert.Equal("wsl.exe", wsl.FileName); Assert.Contains("PORT=3001", wsl.Arguments);
        Assert.Throws<ArgumentException>(() => HubService.BuildCommand(HubTarget.Windows, "anything", "x"));
    }

    [Fact]
    public void Adb_device_output_is_parsed_with_partial_states()
    {
        var result = AdbService.ParseDevices("List of devices attached\nABC device product:p model:Pixel_9 device:tokay\nXYZ unauthorized usb:1");
        Assert.Equal(2, result.Count); Assert.Equal("Pixel 9", result[0].Model); Assert.Equal("unauthorized", result[1].State);
    }

    [Theory]
    [InlineData("{\"ready\":true}", "json")]
    [InlineData("import pathlib\nprint(pathlib.Path.cwd())", "py")]
    [InlineData("interface User { name: string }", "ts")]
    [InlineData("# Clipboard notes\n\n- one", "md")]
    [InlineData("name,port\nwindows,3000", "csv")]
    [InlineData("ordinary clipboard text", "txt")]
    public void Clipboard_types_match_existing_behavior(string content, string expected)
        => Assert.Equal(expected, ClipboardService.DetectExtension(content));

    [Fact]
    public void Timestamp_parser_ignores_invalid_ranges()
    {
        var chapters = TimestampParser.Parse("Jump Squats: 0:26 - 0:56\nBad: 1:00 - 0:30\nLong: 1:01:00 - 1:02:05");
        Assert.Equal(2, chapters.Count); Assert.Equal(30, chapters[0].Duration); Assert.Equal(65, chapters[1].Duration);
    }

    [Fact]
    public void Youtube_metadata_and_names_are_stable()
    {
        var info = YouTubeService.ParseVideoInfo("""{"id":"abc","title":"Demo","duration":120,"uploader":"u","thumbnail":"t","chapters":[{"title":"One","start_time":0,"end_time":30},{"title":"Broken","start_time":40,"end_time":39}]}""");
        Assert.Equal("abc", info.Id); Assert.Single(info.Chapters); Assert.Equal("One", info.Chapters[0].Title);
        Assert.Equal("A_B", YouTubeService.SafeName(" A / B ", "fallback"));
        Assert.Equal("fallback", YouTubeService.SafeName("<>:/", "fallback"));
    }

    [Fact]
    public async Task Native_framing_round_trips_and_rejects_bad_lengths()
    {
        var token = TestContext.Current.CancellationToken;
        await using var stream = new MemoryStream(); await NativeMessageFraming.WriteAsync(stream, new { success = true, value = 7 }, token); stream.Position = 0;
        using var value = await NativeMessageFraming.ReadAsync(stream, token); Assert.True(value!.RootElement.GetProperty("success").GetBoolean()); Assert.Equal(7, value.RootElement.GetProperty("value").GetInt32());
        await using var bad = new MemoryStream([0, 0, 0, 0]); await Assert.ThrowsAsync<InvalidDataException>(async () => await NativeMessageFraming.ReadAsync(bad, token));
    }

    [Fact]
    public async Task Native_relay_returns_error_when_desktop_is_unavailable()
    {
        var token = TestContext.Current.CancellationToken;
        await using var input = new MemoryStream(); await NativeMessageFraming.WriteAsync(input, new { action = "ping" }, token); input.Position = 0; await using var output = new MemoryStream();
        await NativeHostRelay.RelayAsync(input, output, token); output.Position = 0; using var response = await NativeMessageFraming.ReadAsync(output, token);
        Assert.False(response!.RootElement.GetProperty("success").GetBoolean()); Assert.Contains("desktop app", response.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Command_runner_timeout_returns_124()
    {
        if (!OperatingSystem.IsWindows()) return; var runner = new CommandRunner(); var result = await runner.RunAsync("powershell.exe", ["-NoProfile", "-Command", "Start-Sleep -Seconds 2"], timeout: TimeSpan.FromMilliseconds(100), cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(result.Success); Assert.Equal(124, result.ExitCode); Assert.Contains("timed out", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }
}
