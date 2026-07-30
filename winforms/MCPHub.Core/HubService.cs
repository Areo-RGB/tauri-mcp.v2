using System.Diagnostics;

namespace MCPHub.Core;

public sealed class HubService : IAsyncDisposable
{
    private sealed class OwnedProcess(string name)
    {
        public string Name { get; } = name;
        public Process? Hub { get; set; }
        public Process? Ngrok { get; set; }
        public string? Script { get; set; }
        public string? ProjectDir { get; set; }
        public int? LastExitCode { get; set; }
        public string HubLog { get; set; } = Path.Combine(Path.GetTempPath(), $"mcphub-{name}-process.log");
        public string NgrokLog { get; set; } = Path.Combine(Path.GetTempPath(), $"mcphub-{name}-ngrok.log");
    }

    private readonly ICommandRunner _runner;
    private readonly Dictionary<HubTarget, OwnedProcess> _processes = new()
    {
        [HubTarget.Windows] = new("windows"), [HubTarget.Wsl] = new("wsl")
    };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly WindowsProcessJob _job = new();

    public HubService(ICommandRunner runner) => _runner = runner;

    public static (string FileName, IReadOnlyList<string> Arguments, string? WorkingDirectory) BuildCommand(
        HubTarget target, string script, string projectDir)
    {
        if (!AppConstants.AllowedScripts.Contains(script)) throw new ArgumentException("That Hub script is not allowed.", nameof(script));
        return target == HubTarget.Windows
            ? ("corepack", ["pnpm", script], projectDir)
            : ("wsl.exe", ["--cd", projectDir, "--", "env", "PORT=3001", "corepack", "pnpm", script], null);
    }

    public async Task<HubProcessInfo> StartAsync(HubTarget target, string script, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = _processes[target];
            Refresh(state);
            if (state.Hub is not null) throw new InvalidOperationException($"The {target} Hub is already running.");
            var projectDir = target == HubTarget.Windows ? AppConstants.WindowsProjectDir : AppConstants.WslProjectDir;
            ValidateProjectDir(target, projectDir);
            state.HubLog = NewLogPath(state.Name, "process");
            var command = BuildCommand(target, script, projectDir);
            var executable = _runner.FindExecutable(command.FileName) ?? command.FileName;
            state.Hub = _runner.Start(executable, command.Arguments, command.WorkingDirectory, state.HubLog);
            _job.Add(state.Hub);
            state.Script = script; state.ProjectDir = projectDir; state.LastExitCode = null;
            if (script == "start") StartNgrok(target, state);
            return Info(state);
        }
        finally { _gate.Release(); }
    }

    public async Task<HubProcessInfo> StopAsync(HubTarget target, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = _processes[target];
            await StopProcessAsync(state.Hub); state.Hub = null;
            await StopProcessAsync(state.Ngrok); state.Ngrok = null;
            state.LastExitCode = null;
            return Info(state);
        }
        finally { _gate.Release(); }
    }

    public async Task<HubProcessInfo> RestartAsync(HubTarget target, CancellationToken cancellationToken = default)
    {
        var script = _processes[target].Script == "build" ? "build" : "start";
        await StopAsync(target, cancellationToken);
        return await StartAsync(target, script, cancellationToken);
    }

    public HubProcessInfo GetStatus(HubTarget target)
    {
        var state = _processes[target];
        Refresh(state);
        return Info(state);
    }

    public async Task StopAllAsync()
    {
        await StopAsync(HubTarget.Windows);
        await StopAsync(HubTarget.Wsl);
    }

    private void StartNgrok(HubTarget target, OwnedProcess state)
    {
        var config = target == HubTarget.Windows ? AppConstants.WindowsNgrokConfig : AppConstants.WslNgrokConfig;
        var tunnel = target == HubTarget.Windows ? "mcp-hub-windows" : "mcp-hub-wsl";
        if (!File.Exists(config)) { File.WriteAllText(state.NgrokLog, $"ngrok config was not found: {config}"); return; }
        state.NgrokLog = NewLogPath(state.Name, "ngrok");
        state.Ngrok = _runner.Start(_runner.FindExecutable("ngrok") ?? "ngrok", ["start", tunnel, "--config", config], outputPath: state.NgrokLog);
        _job.Add(state.Ngrok);
    }

    private static string NewLogPath(string name, string kind) =>
        Path.Combine(Path.GetTempPath(), $"mcphub-{name}-{kind}-{Environment.ProcessId}-{Guid.NewGuid():N}.log");

    private static void ValidateProjectDir(HubTarget target, string path)
    {
        if (target == HubTarget.Windows)
        {
            if (!Directory.Exists(path) || !File.Exists(Path.Combine(path, "package.json")))
                throw new DirectoryNotFoundException($"Windows MCPHub project was not found: {path}");
        }
        else if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/'))
            throw new DirectoryNotFoundException("The WSL MCPHub path is invalid.");
    }

    private static void Refresh(OwnedProcess state)
    {
        if (state.Hub is { HasExited: true } hub) { state.LastExitCode = hub.ExitCode; hub.Dispose(); state.Hub = null; }
        if (state.Ngrok is { HasExited: true } ngrok) { ngrok.Dispose(); state.Ngrok = null; }
    }

    private static HubProcessInfo Info(OwnedProcess state)
    {
        Refresh(state);
        var hubLog = ReadTail(state.HubLog); var ngrokLog = ReadTail(state.NgrokLog);
        return new(state.Hub is not null, state.Hub?.Id, state.Ngrok is not null, state.Ngrok?.Id,
            state.Script, state.ProjectDir, state.LastExitCode, hubLog + ngrokLog, hubLog, ngrokLog);
    }

    public static string ReadTail(string path)
    {
        if (!File.Exists(path)) return string.Empty;
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var count = (int)Math.Min(stream.Length, AppConstants.LogTailBytes);
        stream.Seek(-count, SeekOrigin.End);
        var bytes = new byte[count]; stream.ReadExactly(bytes);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static async Task StopProcessAsync(Process? process)
    {
        if (process is null) return;
        try { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); } catch { }
        process.Dispose();
    }

    public async ValueTask DisposeAsync() { await StopAllAsync(); _job.Dispose(); _gate.Dispose(); }
}
