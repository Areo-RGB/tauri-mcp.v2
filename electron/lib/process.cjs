const { spawn } = require("node:child_process");
const fs = require("node:fs");
const path = require("node:path");

const WINDOWS_HIDE = { windowsHide: true };

function runProcess(executable, args = [], options = {}) {
  return new Promise((resolve, reject) => {
    const child = spawn(executable, args, {
      ...WINDOWS_HIDE,
      cwd: options.cwd,
      env: options.env ?? process.env,
      shell: false,
      stdio: ["ignore", "pipe", "pipe"],
    });
    const stdout = [];
    const stderr = [];
    let timedOut = false;
    const timer = options.timeoutMs
      ? setTimeout(() => {
          timedOut = true;
          killTree(child.pid).finally(() => child.kill());
        }, options.timeoutMs)
      : null;

    child.stdout.on("data", (chunk) => stdout.push(chunk));
    child.stderr.on("data", (chunk) => stderr.push(chunk));
    child.once("error", (error) => {
      if (timer) clearTimeout(timer);
      reject(error);
    });
    child.once("close", (code) => {
      if (timer) clearTimeout(timer);
      resolve({
        code: timedOut ? 124 : (code ?? -1),
        stdout: Buffer.concat(stdout),
        stderr: Buffer.concat(stderr),
        timedOut,
      });
    });
  });
}

async function findExecutable(names) {
  const finder = process.platform === "win32" ? "where.exe" : "which";
  for (const name of names) {
    try {
      const result = await runProcess(finder, [name], { timeoutMs: 5_000 });
      if (result.code === 0) {
        const first = result.stdout.toString("utf8").split(/\r?\n/).find(Boolean);
        if (first) return first.trim();
      }
    } catch {
      // Try the next candidate.
    }
  }
  return null;
}

async function killTree(pid) {
  if (!pid) return;
  if (process.platform === "win32") {
    await runProcess("taskkill.exe", ["/PID", String(pid), "/T", "/F"], { timeoutMs: 10_000 }).catch(() => {});
    return;
  }
  try {
    process.kill(-pid, "SIGTERM");
  } catch {
    try {
      process.kill(pid, "SIGTERM");
    } catch {
      // Process already exited.
    }
  }
}

function readTail(filePath, bytes = 16_384) {
  try {
    const size = fs.statSync(filePath).size;
    const length = Math.min(size, bytes);
    const buffer = Buffer.alloc(length);
    const descriptor = fs.openSync(filePath, "r");
    fs.readSync(descriptor, buffer, 0, length, size - length);
    fs.closeSync(descriptor);
    return buffer.toString("utf8");
  } catch {
    return "";
  }
}

function ensureDirectory(directory) {
  fs.mkdirSync(directory, { recursive: true });
  return directory;
}

function loadDotEnv(filePath) {
  if (!fs.existsSync(filePath)) return;
  for (const line of fs.readFileSync(filePath, "utf8").split(/\r?\n/)) {
    const match = line.match(/^\s*(?:export\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)\s*$/);
    if (!match || Object.hasOwn(process.env, match[1])) continue;
    let value = match[2];
    if (
      (value.startsWith('"') && value.endsWith('"')) ||
      (value.startsWith("'") && value.endsWith("'"))
    ) {
      value = value.slice(1, -1);
    } else {
      value = value.replace(/\s+#.*$/, "");
    }
    process.env[match[1]] = value.replace(/\\n/g, "\n");
  }
}

function timestamp() {
  const date = new Date();
  const pad = (value) => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}_${pad(date.getHours())}-${pad(date.getMinutes())}-${pad(date.getSeconds())}`;
}

module.exports = {
  ensureDirectory,
  findExecutable,
  killTree,
  loadDotEnv,
  readTail,
  runProcess,
  timestamp,
};
