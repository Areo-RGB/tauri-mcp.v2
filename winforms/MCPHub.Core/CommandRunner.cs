using System.Diagnostics;

namespace MCPHub.Core;

public sealed record CommandOutput(bool Success, int ExitCode, string Stdout, string Stderr);

public interface ICommandRunner
{
    Task<CommandOutput> RunAsync(string fileName, IEnumerable<string> arguments, string? workingDirectory = null,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default);
    Process Start(string fileName, IEnumerable<string> arguments, string? workingDirectory = null,
        string? outputPath = null);
    string? FindExecutable(params string[] names);
}

public sealed class CommandRunner : ICommandRunner
{
    public async Task<CommandOutput> RunAsync(string fileName, IEnumerable<string> arguments, string? workingDirectory = null,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        using var process = Create(fileName, arguments, workingDirectory);
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCts = timeout is null ? null : new CancellationTokenSource(timeout.Value);
        using var linked = timeoutCts is null ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try { await process.WaitForExitAsync(linked.Token); }
        catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true)
        {
            try { process.Kill(true); } catch { }
            await process.WaitForExitAsync(CancellationToken.None);
            return new(false, 124, await stdout, (await stderr) + $"\nExecution timed out after {timeout!.Value.TotalSeconds:0} seconds.");
        }
        return new(process.ExitCode == 0, process.ExitCode, (await stdout).TrimEnd(), (await stderr).TrimEnd());
    }

    public Process Start(string fileName, IEnumerable<string> arguments, string? workingDirectory = null, string? outputPath = null)
    {
        var process = Create(fileName, arguments, workingDirectory);
        if (outputPath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) Append(outputPath, e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Append(outputPath, e.Data); };
        }
        process.Start();
        if (outputPath is not null) { process.BeginOutputReadLine(); process.BeginErrorReadLine(); }
        return process;
    }

    public string? FindExecutable(params string[] names)
    {
        foreach (var name in names)
        {
            if (Path.IsPathRooted(name) && File.Exists(name)) return name;
            string[] variants = Path.HasExtension(name) ? [name] : [name, name + ".exe", name + ".cmd", name + ".bat", name + ".com"];
            foreach (var folder in Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [])
                foreach (var variant in variants)
                {
                    var candidate = Path.Combine(folder.Trim(), variant); if (File.Exists(candidate)) return candidate;
                }
        }
        return null;
    }

    private static Process Create(string fileName, IEnumerable<string> arguments, string? workingDirectory)
    {
        var extension = Path.GetExtension(fileName);
        var info = extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)
            ? new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true }
            : extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase)
                ? new ProcessStartInfo("pwsh.exe") { UseShellExecute = false, CreateNoWindow = true }
                : new ProcessStartInfo(fileName) { UseShellExecute = false, CreateNoWindow = true };
        if (workingDirectory is not null) info.WorkingDirectory = workingDirectory;
        if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)) foreach (var prefix in new[] { "/D", "/S", "/C", fileName }) info.ArgumentList.Add(prefix);
        else if (extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase)) foreach (var prefix in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", fileName }) info.ArgumentList.Add(prefix);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return new Process { StartInfo = info, EnableRaisingEvents = true };
    }

    private static readonly object LogLock = new();
    private static void Append(string path, string line)
    {
        lock (LogLock) File.AppendAllText(path, line + Environment.NewLine);
    }
}
