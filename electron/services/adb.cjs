const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { spawn } = require("node:child_process");
const { findExecutable, runProcess, timestamp } = require("../lib/process.cjs");

function textResult(command, result, paths = []) {
  const stdout = result.stdout.toString("utf8").trim();
  const stderr = result.stderr.toString("utf8").trim();
  return {
    ok: result.code === 0,
    command,
    stdout,
    stderr,
    lines: [...stdout.split(/\r?\n/).filter(Boolean), ...stderr.split(/\r?\n/).filter(Boolean)],
    exitCode: result.code,
    paths,
  };
}

function userFolder(name) {
  const home = os.homedir();
  const candidate = path.join(home, name);
  return fs.existsSync(candidate) ? candidate : os.tmpdir();
}

class AdbService {
  async adb() {
    const executable = await findExecutable(["adb.exe", "adb"]);
    if (!executable) throw new Error("adb was not found on PATH. Install Android platform-tools.");
    return executable;
  }

  async scrcpy() {
    const executable = await findExecutable(["scrcpy.exe", "scrcpy.cmd", "scrcpy.bat", "scrcpy"]);
    if (!executable) throw new Error("scrcpy was not found on PATH.");
    return executable;
  }

  async runAdb(args) {
    const executable = await this.adb();
    const result = await runProcess(executable, args, { timeoutMs: 120_000 });
    return textResult(`adb ${args.join(" ")}`, result);
  }

  async getDevices() {
    const result = await this.runAdb(["devices", "-l"]);
    if (!result.ok) throw new Error(result.stderr || "adb devices failed.");
    return result.stdout
      .split(/\r?\n/)
      .slice(1)
      .filter(Boolean)
      .map((line) => {
        const fields = line.trim().split(/\s+/);
        const serial = fields[0];
        const state = fields[1];
        const values = Object.fromEntries(fields.slice(2).map((field) => {
          const separator = field.indexOf(":");
          return separator < 0 ? [field, ""] : [field.slice(0, separator), field.slice(separator + 1)];
        }));
        return {
          serial,
          state,
          model: (values.model || serial).replaceAll("_", " "),
          device: values.device || "android",
        };
      });
  }

  scrcpyInvocation(executable, args) {
    if (/\.(?:cmd|bat)$/i.test(executable)) {
      return { executable: "cmd.exe", args: ["/D", "/S", "/C", executable, ...args] };
    }
    return { executable, args };
  }

  async getScrcpyVersion() {
    const executable = await this.scrcpy();
    const command = this.scrcpyInvocation(executable, ["--version"]);
    const result = await runProcess(command.executable, command.args, { timeoutMs: 15_000 });
    return textResult("scrcpy --version", result);
  }

  async startMirror({ serials, turnScreenOff }) {
    if (!Array.isArray(serials) || !serials.length) throw new Error("Select at least one device.");
    const executable = await this.scrcpy();
    const lines = [];
    let ok = true;
    for (const serial of serials) {
      const args = ["-s", serial, ...(turnScreenOff ? ["--turn-screen-off"] : [])];
      const command = this.scrcpyInvocation(executable, args);
      try {
        const child = spawn(command.executable, command.args, {
          windowsHide: true,
          detached: false,
          stdio: "ignore",
        });
        child.unref();
        lines.push(`Started Scrcpy for ${serial}${turnScreenOff ? " with screen off" : ""}.`);
      } catch (error) {
        ok = false;
        lines.push(`Could not start Scrcpy for ${serial}: ${error.message}`);
      }
    }
    return {
      ok,
      command: "scrcpy mirror",
      stdout: lines.join("\n"),
      stderr: "",
      lines,
      exitCode: ok ? 0 : 1,
      paths: [],
    };
  }

  async screenshots({ serials }) {
    if (!Array.isArray(serials) || !serials.length) throw new Error("Select at least one device.");
    const executable = await this.adb();
    const folder = path.join(userFolder("Pictures"), "MCPHub", "ADB Screenshots");
    fs.mkdirSync(folder, { recursive: true });
    const stamp = timestamp().replaceAll("_", "-");
    const lines = [];
    const paths = [];
    let ok = true;
    for (const serial of serials) {
      const result = await runProcess(executable, ["-s", serial, "exec-out", "screencap", "-p"], {
        timeoutMs: 30_000,
      });
      if (result.code === 0 && result.stdout.length > 8) {
        const destination = path.join(folder, `adb-${serial}-${stamp}.png`);
        fs.writeFileSync(destination, result.stdout);
        paths.push(destination);
        lines.push(`Saved screenshot for ${serial}: ${destination}`);
      } else {
        ok = false;
        lines.push(`Screenshot failed for ${serial}: ${result.stderr.toString("utf8").trim()}`);
      }
    }
    return {
      ok,
      command: "adb screencap",
      stdout: lines.join("\n"),
      stderr: "",
      lines,
      exitCode: ok ? 0 : 1,
      paths,
    };
  }

  async exportSpecs({ serials }) {
    if (!Array.isArray(serials) || !serials.length) throw new Error("Select at least one device.");
    const folder = path.join(userFolder("Documents"), "MCPHub", "ADB Specs");
    fs.mkdirSync(folder, { recursive: true });
    const stamp = timestamp().replaceAll("_", "-");
    const lines = [];
    const paths = [];
    let ok = true;
    for (const serial of serials) {
      const result = await this.runAdb(["-s", serial, "shell", "getprop"]);
      const destination = path.join(folder, `adb-${serial}-${stamp}.txt`);
      fs.writeFileSync(destination, result.stdout, "utf8");
      lines.push(`Saved specs for ${serial}: ${destination}`);
      paths.push(destination);
      ok &&= result.ok;
    }
    return {
      ok,
      command: "adb shell getprop",
      stdout: lines.join("\n"),
      stderr: "",
      lines,
      exitCode: ok ? 0 : 1,
      paths,
    };
  }

  async installApk({ apkPath, serials }) {
    const apk = String(apkPath ?? "").trim();
    if (!fs.existsSync(apk) || !fs.statSync(apk).isFile()) throw new Error("Select an existing APK file.");
    if (!Array.isArray(serials) || !serials.length) throw new Error("Select at least one device.");
    const lines = [];
    let ok = true;
    for (const serial of serials) {
      const result = await this.runAdb(["-s", serial, "install", "-r", apk]);
      lines.push(`${serial}: ${result.ok ? result.stdout : result.stderr}`);
      ok &&= result.ok;
    }
    return {
      ok,
      command: `adb install ${apk}`,
      stdout: lines.join("\n"),
      stderr: "",
      lines,
      exitCode: ok ? 0 : 1,
      paths: [],
    };
  }
}

module.exports = { AdbService };
