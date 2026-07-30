using System.Text.Json;
using System.Text.RegularExpressions;

namespace MCPHub.Core;

public sealed class ClipboardService(ICommandRunner runner)
{
    private const int SampleLimit = 200_000;

    public static string DetectExtension(string content)
    {
        var sample = content.Length > SampleLimit ? content[..SampleLimit].Trim() : content.Trim();
        if (sample.Length == 0) return "txt";
        if (Matches(@"(?is)^\s*<\?xml\b", sample)) return "xml";
        if (Matches(@"(?is)^\s*(?:<!doctype\s+html\b|<html\b)", sample)) return "html";
        if (Matches(@"(?is)^\s*<svg\b", sample)) return "svg";
        if (((sample[0] == '{' && sample[^1] == '}') || (sample[0] == '[' && sample[^1] == ']')) && IsJson(sample)) return "json";
        if (Matches(@"(?im)^\s*(?:@?echo\s+off\b|setlocal\b|endlocal\b|for\s+/(?:f|l|r|d)\b|if\s+(?:not\s+)?exist\b|%[\w]+%|(?:cls|pause|ver|vol)\s*$)", sample)) return "bat";
        var rules = new (string Extension, string Pattern)[]
        {
            ("ahk", @"(?im)^\s*(?:#requires\s+autohotkey|#singleinstance\b)"),
            ("py", @"(?im)^\s*(?:#!.*\bpython(?:3)?\b|from\s+[\w.]+\s+import\b|import\s+[\w.]+|(?:async\s+)?def\s+\w+\s*\(|class\s+\w+.*:|if\s+__name__\s*==|print\s*\()"),
            ("sh", @"(?im)^\s*#!.*\b(?:bash|sh|zsh)\b"),
            ("ps1", @"(?im)^\s*(?:param\s*\(|function\s+[\w-]+\s*\{|(?:get|set|new|remove|invoke|start|stop|write|out|select|where|foreach|measure|convertto|convertfrom|test)-\w+|\$[\w:]+\s*=)"),
            ("cs", @"(?im)^\s*(?:using\s+[\w.]+;|namespace\s+[\w.]+|(?:public|private|internal)\s+(?:static\s+)?class\s+\w+)"),
            ("java", @"(?im)^\s*(?:package\s+[\w.]+;|(?:public\s+)?class\s+\w+.*\{)"),
            ("go", @"(?im)^\s*(?:package\s+\w+\s*$|func\s+\w+\s*\([^)]*\))"),
            ("rs", @"(?im)^\s*(?:fn\s+\w+\s*\(|(?:pub\s+)?(?:struct|enum|trait|impl)\s+\w+)"),
            ("php", @"(?im)^\s*<\?php\b")
        };
        foreach (var rule in rules) if (Matches(rule.Pattern, sample)) return rule.Extension;
        if (Matches(@"(?im)^\s*(?:#include\s*(?:<[^>]+>|""[^""]+"")|(?:int|void)\s+main\s*\()", sample))
            return Matches(@"(?im)^\s*(?:class|namespace)\s+\w+|std::|#include\s*<iostream>", sample) ? "cpp" : "c";
        if (Matches(@"(?im)^\s*(?:interface|type|enum)\s+\w+|:\s*(?:string|number|boolean|unknown|never)\b|import\s+type\s", sample)) return "ts";
        if (Matches("(?im)^\\s*(?:import|export)\\s+.*\\bfrom\\s+['\"]|^\\s*(?:const|let|var)\\s+\\w+\\s*=|=>|console\\.", sample)) return "js";
        if (Matches(@"(?ims)^\s*(?:@[\w-]+\s*)?(?:html|body|:root|[#.][\w-]+)[^{]*\{[\s\S]*:[^;{}]+;", sample)) return "css";
        if (Matches(@"(?im)^\s*(?:select|insert\s+into|update\s+\w+\s+set|delete\s+from|create\s+(?:table|database|view)|alter\s+table)\b", sample)) return "sql";
        if (Matches(@"(?m)^\s*\[[\w.-]+\]\s*$", sample) && Matches("(?im)^\\s*[\\w.-]+\\s*=\\s*(?:[\"']|\\d|true|false|\\[)", sample)) return "toml";
        if (Matches(@"(?m)^\s{0,3}(?:#{1,6}\s+\S|[-*+]\s+\S|>\s+\S|```)", sample)) return "md";
        if (Matches(@"(?m)^(?:---\s*$)?[\s\S]*^\s*[\w.-]+\s*:\s*(?:\S.*)?$", sample)) return "yaml";
        if (LooksLikeCsv(sample)) return "csv";
        return "txt";
    }

    public ClipboardSaveResult Save(string content)
    {
        if (content.Length == 0) throw new InvalidOperationException("The clipboard has no text.");
        var extension = DetectExtension(content); var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        Directory.CreateDirectory(desktop); var baseName = $"clipboard_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}"; var path = Path.Combine(desktop, $"{baseName}.{extension}"); var suffix = 2;
        while (File.Exists(path)) path = Path.Combine(desktop, $"{baseName}_{suffix++}.{extension}");
        File.WriteAllText(path, content); return new(path, extension);
    }

    public async Task<ClipboardRunResult> RunAsync(string content, CancellationToken token = default)
    {
        var saved = Save(content); var command = BuildExecution(saved.Extension, saved.Path);
        var result = await runner.RunAsync(command.FileName, command.Arguments, Path.GetDirectoryName(saved.Path), TimeSpan.FromSeconds(AppConstants.ClipboardRunTimeoutSeconds), token);
        var output = string.Join(Environment.NewLine, new[] { result.Stdout, result.Stderr }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (string.IsNullOrWhiteSpace(output)) output = $"Code completed with exit code {result.ExitCode} and produced no output.";
        if (!result.Success) output = $".{saved.Extension} exited with code {result.ExitCode}{Environment.NewLine}{output}";
        return new(output, saved.Extension, result.ExitCode, saved.Path);
    }

    public (string FileName, IReadOnlyList<string> Arguments) BuildExecution(string extension, string path)
    {
        string Need(params string[] names) => runner.FindExecutable(names) ?? throw new FileNotFoundException($"{names[0]} was not found.");
        return extension switch
        {
            "py" => Python(Need("py.exe", "python.exe", "python3.exe"), path),
            "js" or "ts" => (Need("node.exe"), [path]),
            "ps1" => (Need("pwsh.exe", "powershell.exe"), ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", path]),
            "bat" => (Need("cmd.exe"), ["/D", "/S", "/C", path]),
            _ => throw new InvalidOperationException($"The analyzer detected .{extension}, which is not runnable. Supported clipboard code: Python, JavaScript/TypeScript, PowerShell, and batch.")
        };
    }

    private static (string, IReadOnlyList<string>) Python(string exe, string path)
        => Path.GetFileNameWithoutExtension(exe).Equals("py", StringComparison.OrdinalIgnoreCase) ? (exe, ["-3", path]) : (exe, [path]);
    private static bool Matches(string pattern, string sample) => Regex.IsMatch(sample, pattern);
    private static bool IsJson(string sample) { try { JsonDocument.Parse(sample); return true; } catch { return false; } }
    private static bool LooksLikeCsv(string sample)
    {
        var lines = sample.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Take(5).ToList(); if (lines.Count < 2) return false;
        var delimiter = lines[0].Contains('\t') ? '\t' : ','; var expected = lines[0].Split(delimiter).Length;
        return expected >= 2 && lines.All(line => line.Split(delimiter).Length == expected);
    }
}
