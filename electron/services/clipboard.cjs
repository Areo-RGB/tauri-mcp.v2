const fs = require("node:fs");
const path = require("node:path");
const { findExecutable, runProcess, timestamp } = require("../lib/process.cjs");

const SAMPLE_LIMIT = 200_000;

function looksLikeCsv(sample) {
  const lines = sample.split(/\r?\n/).filter((line) => line.trim()).slice(0, 5);
  if (lines.length < 2) return false;
  const delimiter = lines[0].includes("\t") ? "\t" : ",";
  const expected = lines[0].split(delimiter).length;
  return expected >= 2 && lines.every((line) => line.split(delimiter).length === expected);
}

function detectClipboardExtension(content) {
  const sample = String(content ?? "").slice(0, SAMPLE_LIMIT).trim();
  if (!sample) return "txt";
  if (/^\s*<\?xml\b/is.test(sample)) return "xml";
  if (/^\s*(?:<!doctype\s+html\b|<html\b)/is.test(sample)) return "html";
  if (/^\s*<svg\b/is.test(sample)) return "svg";
  if (
    ((sample.startsWith("{") && sample.endsWith("}")) ||
      (sample.startsWith("[") && sample.endsWith("]")))
  ) {
    try {
      JSON.parse(sample);
      return "json";
    } catch {}
  }
  if (/^\s*(?:@?echo\s+off\b|setlocal\b|endlocal\b|for\s+\/(?:f|l|r|d)\b|if\s+(?:not\s+)?exist\b|%[\w]+%|dir\s+\/(?:[a-z0-9:-]+\s*)+$|(?:cls|pause|ver|vol)\s*$)/im.test(sample)) {
    return "bat";
  }

  const rules = [
    ["ahk", /^\s*(?:#requires\s+autohotkey|#singleinstance\b)/im],
    ["py", /^\s*(?:#!.*\bpython(?:3)?\b|from\s+[\w.]+\s+import\b|import\s+[\w.]+|(?:async\s+)?def\s+\w+\s*\(|class\s+\w+.*:|if\s+__name__\s*==|print\s*\(|raise\s+\w+|(?:for|while|try|except|with)\b.*:\s*$)/im],
    ["sh", /^\s*#!.*\b(?:bash|sh|zsh)\b/im],
    ["ps1", /^\s*(?:param\s*\(|function\s+[\w-]+\s*\{|(?:get|set|new|remove|invoke|start|stop|write|out|select|where|foreach|measure|convertto|convertfrom|test)-\w+|\$[\w:]+\s*=)/im],
    ["cs", /^\s*(?:using\s+[\w.]+;|namespace\s+[\w.]+|(?:public|private|internal)\s+(?:static\s+)?class\s+\w+)/im],
    ["java", /^\s*(?:package\s+[\w.]+;|(?:public\s+)?class\s+\w+.*\{)/im],
    ["go", /^\s*(?:package\s+\w+\s*$|func\s+\w+\s*\([^)]*\))/im],
    ["rs", /^\s*(?:fn\s+\w+\s*\(|(?:pub\s+)?(?:struct|enum|trait|impl)\s+\w+)/im],
    ["php", /^\s*<\?php\b/im],
  ];
  for (const [extension, pattern] of rules) {
    if (pattern.test(sample)) return extension;
  }
  if (/^\s*(?:#include\s*(?:<[^>]+>|"[^"]+")|(?:int|void)\s+main\s*\()/im.test(sample)) {
    return /^\s*(?:class|namespace)\s+\w+|std::|#include\s*<iostream>/im.test(sample) ? "cpp" : "c";
  }
  if (/^\s*(?:interface|type|enum)\s+\w+|:\s*(?:string|number|boolean|unknown|never)\b|import\s+type\s|^\s*(?:const|let|var)\s+\w+\s*=\s*<[^>\r\n]+>\s*\([^)]*:\s*[^)]+\)|^\s*(?:export\s+)?(?:async\s+)?function\s+\w+\s*<[^>\r\n]+>\s*\([^)]*:\s*[^)]+\)/im.test(sample)) return "ts";
  if (/^\s*(?:import|export)\s+.*\bfrom\s+['"]|^\s*(?:const|let|var)\s+\w+\s*=|=>|console\.(?:log|error|warn)\s*\(|require\s*\(/im.test(sample)) return "js";
  if (/^\s*(?:@[\w-]+\s*)?(?:html|body|:root|[#.][\w-]+)[^{]*\{[\s\S]*:[^;{}]+;/im.test(sample)) return "css";
  if (/^\s*(?:select|insert\s+into|update\s+\w+\s+set|delete\s+from|create\s+(?:table|database|view)|alter\s+table)\b/im.test(sample)) return "sql";
  if (/^\s*\[[\w.-]+\]\s*$/m.test(sample) && /^\s*[\w.-]+\s*=\s*(?:["']|\d|true|false|\[)/im.test(sample)) return "toml";
  if (/^\s{0,3}(?:#{1,6}\s+\S|[-*+]\s+\S|>\s+\S|```)/m.test(sample)) return "md";
  if (/^(?:---\s*$)?[\s\S]*^\s*[\w.-]+\s*:\s*(?:\S.*)?$/m.test(sample)) return "yaml";
  if (looksLikeCsv(sample)) return "csv";
  return "txt";
}

class ClipboardService {
  constructor(clipboard, desktopPath) {
    this.clipboard = clipboard;
    this.desktopPath = desktopPath;
    this.cached = null;
  }

  snapshot() {
    const content = this.clipboard.readText() || "";
    if (this.cached?.content === content) return this.cached;
    this.cached = { content, extension: detectClipboardExtension(content) };
    return this.cached;
  }

  detect({ content }) {
    return { content, extension: detectClipboardExtension(content) };
  }

  saveContent(content) {
    if (!content) throw new Error("The clipboard has no text.");
    const extension = detectClipboardExtension(content);
    fs.mkdirSync(this.desktopPath, { recursive: true });
    const baseName = `clipboard_${timestamp()}`;
    let destination = path.join(this.desktopPath, `${baseName}.${extension}`);
    let counter = 2;
    while (fs.existsSync(destination)) {
      destination = path.join(this.desktopPath, `${baseName}_${counter}.${extension}`);
      counter += 1;
    }
    fs.writeFileSync(destination, content, "utf8");
    return { path: destination, extension };
  }

  save({ content }) {
    return this.saveContent(content);
  }

  set({ content }) {
    this.clipboard.writeText(content);
    this.cached = { content, extension: detectClipboardExtension(content) };
    return this.cached;
  }

  async executionCommand(extension, sourcePath) {
    if (extension === "py") {
      const executable = await findExecutable(["py.exe", "python.exe", "python3.exe", "python3", "python"]);
      if (!executable) throw new Error("Python code was detected, but Python was not found.");
      const launcher = /^py(?:\.exe)?$/i.test(path.basename(executable));
      return { executable, args: [...(launcher ? ["-3"] : []), sourcePath] };
    }
    if (extension === "js" || extension === "ts") {
      const executable = await findExecutable(["node.exe", "node"]);
      if (!executable) throw new Error("JavaScript was detected, but Node.js was not found.");
      return { executable, args: [sourcePath] };
    }
    if (extension === "ps1") {
      const executable = await findExecutable(["pwsh.exe", "powershell.exe", "pwsh", "powershell"]);
      if (!executable) throw new Error("PowerShell code was detected, but PowerShell was not found.");
      return {
        executable,
        args: ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", sourcePath],
      };
    }
    if (extension === "bat") {
      const executable = await findExecutable(["cmd.exe"]);
      if (!executable) throw new Error("Batch code was detected, but cmd.exe was not found.");
      return { executable, args: ["/D", "/S", "/C", sourcePath] };
    }
    throw new Error(`The analyzer detected .${extension}, which is not runnable. Supported clipboard code: Python, JavaScript/TypeScript, PowerShell, and batch.`);
  }

  async run({ content }) {
    const saved = this.saveContent(content);
    const command = await this.executionCommand(saved.extension, saved.path);
    const result = await runProcess(command.executable, command.args, {
      cwd: path.dirname(saved.path),
      timeoutMs: 60_000,
      env: {
        ...process.env,
        PYTHONUTF8: "1",
        PYTHONIOENCODING: "utf-8",
        NO_COLOR: "1",
      },
    });
    let text = result.stdout.toString("utf8").trimEnd();
    const stderr = result.stderr.toString("utf8").trimEnd();
    if (stderr) text += `${text ? "\n" : ""}${stderr}`;
    if (result.timedOut) text += `${text ? "\n" : ""}Execution timed out after 60 seconds.`;
    if (!text) text = `Code completed with exit code ${result.code} and produced no output.`;
    if (result.code !== 0) text = `.${saved.extension} exited with code ${result.code}\n${text}`;
    this.clipboard.writeText(text);
    this.cached = { content: text, extension: detectClipboardExtension(text) };
    return {
      output: text,
      extension: saved.extension,
      exitCode: result.code,
      path: saved.path,
    };
  }
}

module.exports = { ClipboardService, detectClipboardExtension };
