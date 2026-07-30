namespace MCPHub.Core;

public sealed class AdbService(ICommandRunner runner)
{
    public static IReadOnlyList<AdbDevice> ParseDevices(string output) => output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Skip(1).Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(parts => parts.Length >= 2)
        .Select(parts =>
        {
            var serial = parts[0]; var state = parts[1];
            var model = parts.FirstOrDefault(x => x.StartsWith("model:"))?[6..].Replace('_', ' ') ?? serial;
            var device = parts.FirstOrDefault(x => x.StartsWith("device:"))?[7..] ?? "android";
            return new AdbDevice(serial, model, device, state);
        }).ToList();

    public async Task<IReadOnlyList<AdbDevice>> GetDevicesAsync(CancellationToken token = default)
    {
        var result = await RunAdbAsync(["devices", "-l"], token);
        if (!result.Ok) throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Stderr) ? "adb devices failed." : result.Stderr);
        return ParseDevices(result.Stdout);
    }

    public async Task<AdbCommandResult> GetScrcpyVersionAsync(CancellationToken token = default)
    {
        var executable = runner.FindExecutable("scrcpy.exe", "scrcpy.cmd", "scrcpy") ?? throw new FileNotFoundException("scrcpy was not found on PATH.");
        var output = await runner.RunAsync(executable, ["--version"], cancellationToken: token);
        return Result("scrcpy --version", output);
    }

    public Task<AdbCommandResult> StartMirrorsAsync(IEnumerable<string> serials, bool turnScreenOff)
    {
        var selected = serials.ToList(); if (selected.Count == 0) throw new ArgumentException("Select at least one device.");
        var executable = runner.FindExecutable("scrcpy.exe", "scrcpy.cmd", "scrcpy") ?? throw new FileNotFoundException("scrcpy was not found on PATH.");
        var lines = new List<string>(); var ok = true;
        foreach (var serial in selected)
        {
            try { runner.Start(executable, turnScreenOff ? ["-s", serial, "--turn-screen-off"] : ["-s", serial]); lines.Add($"Started Scrcpy for {serial}{(turnScreenOff ? " with screen off" : "")}."); }
            catch (Exception e) { ok = false; lines.Add($"Could not start Scrcpy for {serial}: {e.Message}"); }
        }
        return Task.FromResult(new AdbCommandResult(ok, "scrcpy mirror", string.Join('\n', lines), "", lines, ok ? 0 : 1, []));
    }

    public async Task<AdbCommandResult> TakeScreenshotsAsync(IEnumerable<string> serials, CancellationToken token = default)
    {
        var selected = Require(serials); var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "MCPHub", "ADB Screenshots"); Directory.CreateDirectory(folder);
        var paths = new List<string>(); var lines = new List<string>(); var ok = true;
        foreach (var serial in selected)
        {
            var exe = FindAdb();
            var path = Path.Combine(folder, $"adb-{serial}-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            // adb binary output cannot pass through text capture; use a shell-free redirect process.
            var start = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var arg in new[] { "-s", serial, "exec-out", "screencap", "-p" }) start.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(start)!; await using (var file = File.Create(path)) await process.StandardOutput.BaseStream.CopyToAsync(file, token); await process.WaitForExitAsync(token);
            if (process.ExitCode == 0) { paths.Add(path); lines.Add($"Saved screenshot for {serial}: {path}"); } else { ok = false; File.Delete(path); }
        }
        return new(ok, "adb screencap", string.Join('\n', lines), "", lines, ok ? 0 : 1, paths);
    }

    public async Task<AdbCommandResult> ExportSpecsAsync(IEnumerable<string> serials, CancellationToken token = default)
    {
        var selected = Require(serials); var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MCPHub", "ADB Specs"); Directory.CreateDirectory(folder);
        var paths = new List<string>(); var lines = new List<string>(); var ok = true;
        foreach (var serial in selected)
        {
            var result = await RunAdbAsync(["-s", serial, "shell", "getprop"], token); var path = Path.Combine(folder, $"adb-{serial}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            await File.WriteAllTextAsync(path, result.Stdout, token); paths.Add(path); lines.Add($"Saved specs for {serial}: {path}"); ok &= result.Ok;
        }
        return new(ok, "adb shell getprop", string.Join('\n', lines), "", lines, ok ? 0 : 1, paths);
    }

    public async Task<AdbCommandResult> InstallApkAsync(string apkPath, IEnumerable<string> serials, CancellationToken token = default)
    {
        if (!File.Exists(apkPath)) throw new FileNotFoundException("Select an existing APK file.", apkPath);
        var lines = new List<string>(); var ok = true;
        foreach (var serial in Require(serials)) { var result = await RunAdbAsync(["-s", serial, "install", "-r", apkPath], token); lines.Add($"{serial}: {(result.Ok ? result.Stdout : result.Stderr)}"); ok &= result.Ok; }
        return new(ok, $"adb install {apkPath}", string.Join('\n', lines), "", lines, ok ? 0 : 1, []);
    }

    private async Task<AdbCommandResult> RunAdbAsync(IReadOnlyList<string> args, CancellationToken token)
        => Result("adb " + string.Join(' ', args), await runner.RunAsync(FindAdb(), args, cancellationToken: token));
    private string FindAdb() => runner.FindExecutable("adb.exe", "adb") ?? throw new FileNotFoundException("adb was not found on PATH. Install Android platform-tools.");
    private static List<string> Require(IEnumerable<string> serials) { var list = serials.ToList(); if (list.Count == 0) throw new ArgumentException("Select at least one device."); return list; }
    private static AdbCommandResult Result(string command, CommandOutput output)
    {
        var lines = output.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Concat(output.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries)).ToList();
        return new(output.Success, command, output.Stdout, output.Stderr, lines, output.ExitCode, []);
    }
}
