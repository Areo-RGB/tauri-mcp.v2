#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use regex::Regex;
use serde::{Deserialize, Serialize};
use std::{
    collections::VecDeque,
    fs::{self, File},
    io::{Read, Seek, SeekFrom},
    net::TcpListener,
    path::{Path, PathBuf},
    process::{Child, Command, Stdio},
    sync::{Arc, Mutex},
    time::Duration,
};
use tauri::Manager;
use wait_timeout::ChildExt;

#[cfg(windows)]
use std::os::windows::process::CommandExt;

#[cfg(windows)]
const CREATE_NO_WINDOW: u32 = 0x08000000;
const LOG_TAIL_BYTES: u64 = 16_384;
const WINDOWS_PROJECT_DIR: &str = r"C:\Users\paul\projects\mcp_UI\mcphub";
const WINDOWS_NGROK_CONFIG: &str = r"C:\Users\paul\AppData\Local\ngrok\ngrok.yml";
const WSL_NGROK_CONFIG: &str = r"C:\Users\paul\AppData\Local\ngrok\ngrok-wsl.yml";
const YOUTUBE_YT_DLP: &str = r"C:\Users\paul\projects\YouTube\backend\yt-dlp.exe";
const YOUTUBE_COOKIES: &str = r"C:\Users\paul\projects\YouTube\backend\cookies.txt";
const CHROME_EXECUTABLE: &str = r"C:\Users\paul\AppData\Local\Google\Chrome\Application\chrome.exe";
const CHAPTER_CLIPPER_SOCKET: &str = "127.0.0.1:32145";
const CLIPBOARD_SAMPLE_LIMIT: usize = 200_000;
const CLIPBOARD_RUN_TIMEOUT_SECONDS: u64 = 60;
const ALLOWED_SCRIPTS: [&str; 6] = [
    "build",
    "start",
    "backend:dev",
    "backend:debug",
    "dev",
    "debug",
];

#[derive(Clone, Copy)]
enum HubTarget {
    Windows,
    Wsl,
}

impl HubTarget {
    fn parse(value: &str) -> Result<Self, String> {
        match value {
            "windows" => Ok(Self::Windows),
            "wsl" => Ok(Self::Wsl),
            _ => Err("Unknown Hub target.".to_string()),
        }
    }

    fn label(self) -> &'static str {
        match self {
            Self::Windows => "Windows",
            Self::Wsl => "WSL",
        }
    }

    fn port(self) -> &'static str {
        match self {
            Self::Windows => "3000",
            Self::Wsl => "3001",
        }
    }

    fn ngrok_config(self) -> &'static str {
        match self {
            Self::Windows => WINDOWS_NGROK_CONFIG,
            Self::Wsl => WSL_NGROK_CONFIG,
        }
    }

    fn ngrok_tunnel(self) -> &'static str {
        match self {
            Self::Windows => "mcp-hub-windows",
            Self::Wsl => "mcp-hub-wsl",
        }
    }
}

struct HubProcess {
    child: Option<Child>,
    ngrok_child: Option<Child>,
    script: Option<String>,
    project_dir: Option<String>,
    last_exit_code: Option<i32>,
    log_path: PathBuf,
    ngrok_log_path: PathBuf,
}

impl HubProcess {
    fn new(name: &str) -> Self {
        Self {
            child: None,
            ngrok_child: None,
            script: None,
            project_dir: None,
            last_exit_code: None,
            log_path: std::env::temp_dir().join(format!("mcphub-{name}-process.log")),
            ngrok_log_path: std::env::temp_dir().join(format!("mcphub-{name}-ngrok.log")),
        }
    }
}

struct HubProcessState {
    windows: Mutex<HubProcess>,
    wsl: Mutex<HubProcess>,
}

impl Default for HubProcessState {
    fn default() -> Self {
        Self {
            windows: Mutex::new(HubProcess::new("windows")),
            wsl: Mutex::new(HubProcess::new("wsl")),
        }
    }
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct HubProcessInfo {
    running: bool,
    pid: Option<u32>,
    ngrok_running: bool,
    ngrok_pid: Option<u32>,
    script: Option<String>,
    project_dir: Option<String>,
    last_exit_code: Option<i32>,
    log_tail: String,
    hub_log_tail: String,
    ngrok_log_tail: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct EndpointReachability {
    reachable: bool,
    status_code: Option<u16>,
    latency_ms: u128,
    url: String,
    detail: String,
}

fn refresh_process(process: &mut HubProcess) -> Result<(), String> {
    let exit_code = match process.child.as_mut() {
        Some(child) => match child.try_wait() {
            Ok(Some(status)) => Some(status.code().unwrap_or(-1)),
            Ok(None) => None,
            Err(error) => return Err(format!("Failed to read Hub process status: {error}")),
        },
        None => None,
    };
    if let Some(code) = exit_code {
        process.child = None;
        process.last_exit_code = Some(code);
    }

    let ngrok_exited = match process.ngrok_child.as_mut() {
        Some(child) => match child.try_wait() {
            Ok(Some(_)) => true,
            Ok(None) => false,
            Err(error) => return Err(format!("Failed to read ngrok process status: {error}")),
        },
        None => false,
    };
    if ngrok_exited {
        process.ngrok_child = None;
    }
    Ok(())
}

fn read_log_tail(path: &Path) -> String {
    let Ok(mut file) = File::open(path) else {
        return String::new();
    };
    let Ok(length) = file.metadata().map(|metadata| metadata.len()) else {
        return String::new();
    };
    let start = length.saturating_sub(LOG_TAIL_BYTES);
    if file.seek(SeekFrom::Start(start)).is_err() {
        return String::new();
    }
    let mut buffer = Vec::new();
    if file.read_to_end(&mut buffer).is_err() {
        return String::new();
    }
    String::from_utf8_lossy(&buffer).into_owned()
}

fn process_info(process: &mut HubProcess) -> Result<HubProcessInfo, String> {
    refresh_process(process)?;
    let hub_log = read_log_tail(&process.log_path);
    let ngrok_log = read_log_tail(&process.ngrok_log_path);
    let log_tail = match (hub_log.is_empty(), ngrok_log.is_empty()) {
        (true, true) => String::new(),
        (false, true) => hub_log.clone(),
        (true, false) => format!("[ngrok]\n{ngrok_log}"),
        (false, false) => format!("{hub_log}\n\n[ngrok]\n{ngrok_log}"),
    };
    Ok(HubProcessInfo {
        running: process.child.is_some(),
        pid: process.child.as_ref().map(Child::id),
        ngrok_running: process.ngrok_child.is_some(),
        ngrok_pid: process.ngrok_child.as_ref().map(Child::id),
        script: process.script.clone(),
        project_dir: process.project_dir.clone(),
        last_exit_code: process.last_exit_code,
        log_tail,
        hub_log_tail: hub_log,
        ngrok_log_tail: ngrok_log,
    })
}

fn validate_project_dir(target: HubTarget, project_dir: &str) -> Result<String, String> {
    let trimmed = project_dir.trim();
    if trimmed.is_empty() {
        return Err("Select the MCPHub project folder first.".to_string());
    }

    match target {
        HubTarget::Windows => {
            let canonical = fs::canonicalize(trimmed)
                .map_err(|error| format!("Could not open MCPHub project folder: {error}"))?;
            if !canonical.join("package.json").is_file() {
                return Err("The selected folder does not contain package.json.".to_string());
            }
            Ok(canonical.to_string_lossy().into_owned())
        }
        HubTarget::Wsl => {
            let package_json = format!("{}/package.json", trimmed.trim_end_matches('/'));
            let status = create_wsl_command()
                .args(["--", "test", "-f", &package_json])
                .status()
                .map_err(|error| format!("Could not query WSL: {error}"))?;
            if !status.success() {
                return Err("The WSL folder does not contain package.json.".to_string());
            }
            Ok(trimmed.to_string())
        }
    }
}

fn create_wsl_command() -> Command {
    let mut command = Command::new("wsl.exe");
    #[cfg(windows)]
    command.creation_flags(CREATE_NO_WINDOW);
    command
}

fn create_hub_command(target: HubTarget, script: &str, project_dir: &str) -> Command {
    match target {
        HubTarget::Windows => {
            let mut command = Command::new("cmd.exe");
            command
                .args(["/D", "/S", "/C"])
                .arg(format!("pnpm {script}"))
                .current_dir(project_dir)
                .env("PORT", target.port());
            #[cfg(windows)]
            command.creation_flags(CREATE_NO_WINDOW);
            command
        }
        HubTarget::Wsl => {
            let mut command = create_wsl_command();
            command.args([
                "--cd",
                project_dir,
                "--",
                "env",
                "PORT=3001",
                "corepack",
                "pnpm",
                script,
            ]);
            command
        }
    }
}

fn create_ngrok_command(target: HubTarget) -> Command {
    let mut command = Command::new("ngrok");
    command.args([
        "start",
        target.ngrok_tunnel(),
        "--config",
        target.ngrok_config(),
    ]);
    #[cfg(windows)]
    command.creation_flags(CREATE_NO_WINDOW);
    command
}

fn start_ngrok(target: HubTarget, process: &mut HubProcess) -> Result<(), String> {
    if process.ngrok_child.is_some() {
        return Ok(());
    }
    if !Path::new(target.ngrok_config()).is_file() {
        return Err(format!(
            "ngrok config was not found: {}",
            target.ngrok_config()
        ));
    }
    let stdout = File::create(&process.ngrok_log_path)
        .map_err(|error| format!("Could not create the ngrok output log: {error}"))?;
    let stderr = stdout
        .try_clone()
        .map_err(|error| format!("Could not prepare the ngrok output log: {error}"))?;
    let child = create_ngrok_command(target)
        .stdin(Stdio::null())
        .stdout(Stdio::from(stdout))
        .stderr(Stdio::from(stderr))
        .spawn()
        .map_err(|error| format!("Could not start ngrok: {error}"))?;
    process.ngrok_child = Some(child);
    Ok(())
}

fn start_processes(
    target: HubTarget,
    process: &mut HubProcess,
    project_dir: &str,
    script: &str,
) -> Result<(), String> {
    let stdout = File::create(&process.log_path)
        .map_err(|error| format!("Could not create the Hub output log: {error}"))?;
    let stderr = stdout
        .try_clone()
        .map_err(|error| format!("Could not prepare the Hub output log: {error}"))?;
    let child = create_hub_command(target, script, project_dir)
        .stdin(Stdio::null())
        .stdout(Stdio::from(stdout))
        .stderr(Stdio::from(stderr))
        .spawn()
        .map_err(|error| {
            format!(
                "Could not run {} Hub command {script}: {error}",
                target.label()
            )
        })?;

    process.child = Some(child);
    process.script = Some(script.to_string());
    process.project_dir = Some(project_dir.to_string());
    process.last_exit_code = None;
    if script == "start" {
        if let Err(error) = start_ngrok(target, process) {
            let _ = fs::write(
                &process.ngrok_log_path,
                format!("ngrok startup failed: {error}\n"),
            );
        }
    }
    Ok(())
}

fn with_process<T>(
    state: &HubProcessState,
    target: HubTarget,
    action: impl FnOnce(&mut HubProcess) -> Result<T, String>,
) -> Result<T, String> {
    let mutex = match target {
        HubTarget::Windows => &state.windows,
        HubTarget::Wsl => &state.wsl,
    };
    let mut process = mutex
        .lock()
        .map_err(|_| format!("{} Hub process state is unavailable.", target.label()))?;
    action(&mut process)
}

#[tauri::command]
fn run_hub_script(
    state: tauri::State<'_, HubProcessState>,
    target: String,
    project_dir: String,
    script: String,
) -> Result<HubProcessInfo, String> {
    if !ALLOWED_SCRIPTS.contains(&script.as_str()) {
        return Err("That Hub script is not allowed.".to_string());
    }
    let target = HubTarget::parse(&target)?;
    let validated_dir = validate_project_dir(target, &project_dir)?;
    with_process(state.inner(), target, |process| {
        refresh_process(process)?;
        if process.child.is_some() {
            return Err(format!(
                "The {} Hub is already running. Stop it first.",
                target.label()
            ));
        }
        start_processes(target, process, &validated_dir, &script)?;
        process_info(process)
    })
}

#[tauri::command]
fn get_hub_process_status(
    state: tauri::State<'_, HubProcessState>,
    target: String,
) -> Result<HubProcessInfo, String> {
    let target = HubTarget::parse(&target)?;
    with_process(state.inner(), target, process_info)
}

fn stop_child(mut child: Child, label: &str) -> Result<(), String> {
    #[cfg(windows)]
    let stopped = Command::new("taskkill")
        .args(["/PID", &child.id().to_string(), "/T", "/F"])
        .creation_flags(CREATE_NO_WINDOW)
        .status()
        .map(|status| status.success())
        .unwrap_or(false);
    #[cfg(not(windows))]
    let stopped = false;
    if !stopped {
        child
            .kill()
            .map_err(|error| format!("Could not stop the {label} process: {error}"))?;
    }
    let _ = child.wait();
    Ok(())
}

#[tauri::command]
fn stop_hub_process(
    state: tauri::State<'_, HubProcessState>,
    target: String,
) -> Result<HubProcessInfo, String> {
    let target = HubTarget::parse(&target)?;
    with_process(state.inner(), target, |process| {
        refresh_process(process)?;
        let mut errors = Vec::new();
        if let Some(child) = process.child.take() {
            if let Err(error) = stop_child(child, &format!("{} Hub", target.label())) {
                errors.push(error);
            } else {
                process.last_exit_code = None;
            }
        }
        if let Some(child) = process.ngrok_child.take() {
            if let Err(error) = stop_child(child, &format!("{} ngrok", target.label())) {
                errors.push(error);
            }
        }
        if !errors.is_empty() {
            return Err(errors.join("; "));
        }
        process_info(process)
    })
}

#[tauri::command]
async fn check_endpoint_reachability(url: String) -> Result<EndpointReachability, String> {
    tauri::async_runtime::spawn_blocking(move || {
        let started = std::time::Instant::now();
        let mut command = Command::new("curl.exe");
        command.args([
            "--silent",
            "--show-error",
            "--output",
            "NUL",
            "--write-out",
            "%{http_code}",
            "--max-time",
            "10",
            "--header",
            "ngrok-skip-browser-warning: true",
            &url,
        ]);
        #[cfg(windows)]
        command.creation_flags(CREATE_NO_WINDOW);
        let output = command
            .output()
            .map_err(|error| format!("Could not run the endpoint check: {error}"))?;
        let latency_ms = started.elapsed().as_millis();
        let raw_status = String::from_utf8_lossy(&output.stdout).trim().to_string();
        let status_code = raw_status.parse::<u16>().ok();
        let reachable = output.status.success()
            && status_code.is_some_and(|status| (200..500).contains(&status));
        let stderr = String::from_utf8_lossy(&output.stderr).trim().to_string();
        Ok(EndpointReachability {
            reachable,
            status_code,
            latency_ms,
            url,
            detail: if stderr.is_empty() {
                if reachable {
                    "Endpoint responded".to_string()
                } else {
                    "Endpoint did not respond".to_string()
                }
            } else {
                stderr
            },
        })
    })
    .await
    .map_err(|error| format!("Endpoint check task failed: {error}"))?
}

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct AdbDevice {
    serial: String,
    model: String,
    device: String,
    state: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct AdbCommandResult {
    ok: bool,
    command: String,
    stdout: String,
    stderr: String,
    lines: Vec<String>,
    exit_code: Option<i32>,
    paths: Vec<String>,
}

fn adb_executable() -> Result<PathBuf, String> {
    find_executable(&["adb.exe", "adb"])
        .ok_or_else(|| "adb was not found on PATH. Install Android platform-tools.".to_string())
}

fn scrcpy_executable() -> Result<PathBuf, String> {
    find_executable(&["scrcpy.exe", "scrcpy"])
        .ok_or_else(|| "scrcpy was not found on PATH.".to_string())
}

fn scrcpy_command(executable: &Path) -> Command {
    let is_script = executable
        .extension()
        .and_then(|value| value.to_str())
        .is_some_and(|value| {
            value.eq_ignore_ascii_case("cmd") || value.eq_ignore_ascii_case("bat")
        });
    if is_script {
        let mut command = Command::new("cmd.exe");
        command.args(["/D", "/S", "/C"]).arg(executable);
        command
    } else {
        Command::new(executable)
    }
}

fn adb_result(command: String, output: std::process::Output) -> AdbCommandResult {
    let stdout = String::from_utf8_lossy(&output.stdout).trim().to_string();
    let stderr = String::from_utf8_lossy(&output.stderr).trim().to_string();
    let mut lines: Vec<String> = stdout.lines().map(str::to_string).collect();
    lines.extend(stderr.lines().map(str::to_string));
    AdbCommandResult {
        ok: output.status.success(),
        command,
        stdout,
        stderr,
        lines,
        exit_code: output.status.code(),
        paths: Vec::new(),
    }
}

fn run_adb(args: &[String]) -> Result<AdbCommandResult, String> {
    let executable = adb_executable()?;
    let command = format!("adb {}", args.join(" "));
    let mut process = scrcpy_command(&executable);
    process
        .args(args)
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped());
    #[cfg(windows)]
    process.creation_flags(CREATE_NO_WINDOW);
    let output = process
        .output()
        .map_err(|error| format!("Could not run {command}: {error}"))?;
    Ok(adb_result(command, output))
}

fn adb_serial_args(serial: &str, tail: &[&str]) -> Vec<String> {
    let mut args = vec!["-s".to_string(), serial.to_string()];
    args.extend(tail.iter().map(|value| (*value).to_string()));
    args
}

#[tauri::command]
fn get_adb_devices() -> Result<Vec<AdbDevice>, String> {
    let result = run_adb(&["devices".to_string(), "-l".to_string()])?;
    if !result.ok {
        return Err(if result.stderr.is_empty() {
            "adb devices failed.".to_string()
        } else {
            result.stderr
        });
    }
    let devices = result
        .stdout
        .lines()
        .skip(1)
        .filter_map(|line| {
            let mut fields = line.split_whitespace();
            let serial = fields.next()?.to_string();
            let state = fields.next()?.to_string();
            let mut model = serial.clone();
            let mut device = "android".to_string();
            for field in fields {
                if let Some(value) = field.strip_prefix("model:") {
                    model = value.replace('_', " ");
                }
                if let Some(value) = field.strip_prefix("device:") {
                    device = value.to_string();
                }
            }
            Some(AdbDevice {
                serial,
                model,
                device,
                state,
            })
        })
        .collect();
    Ok(devices)
}

#[tauri::command]
fn get_scrcpy_version() -> Result<AdbCommandResult, String> {
    let executable = scrcpy_executable()?;
    let mut process = Command::new(executable);
    process
        .arg("--version")
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped());
    #[cfg(windows)]
    process.creation_flags(CREATE_NO_WINDOW);
    let output = process
        .output()
        .map_err(|error| format!("Could not run scrcpy: {error}"))?;
    Ok(adb_result("scrcpy --version".to_string(), output))
}

#[tauri::command]
fn start_scrcpy_mirror(serials: Vec<String>) -> Result<AdbCommandResult, String> {
    let executable = scrcpy_executable()?;
    if serials.is_empty() {
        return Err("Select at least one device.".to_string());
    }
    let mut lines = Vec::new();
    let mut ok = true;
    for serial in &serials {
        let mut process = scrcpy_command(&executable);
        process.args(["-s", serial]);
        #[cfg(windows)]
        process.creation_flags(CREATE_NO_WINDOW);
        match process.spawn() {
            Ok(_) => lines.push(format!("Started Scrcpy for {serial}.")),
            Err(error) => {
                ok = false;
                lines.push(format!("Could not start Scrcpy for {serial}: {error}"));
            }
        }
    }
    Ok(AdbCommandResult {
        ok,
        command: "scrcpy mirror".to_string(),
        stdout: lines.join("\n"),
        stderr: String::new(),
        lines,
        exit_code: Some(if ok { 0 } else { 1 }),
        paths: Vec::new(),
    })
}

#[tauri::command]
fn take_adb_screenshots(serials: Vec<String>) -> Result<AdbCommandResult, String> {
    if serials.is_empty() {
        return Err("Select at least one device.".to_string());
    }
    let folder = dirs::picture_dir()
        .unwrap_or_else(std::env::temp_dir)
        .join("MCPHub")
        .join("ADB Screenshots");
    fs::create_dir_all(&folder)
        .map_err(|error| format!("Could not create screenshot folder: {error}"))?;
    let stamp = chrono::Local::now().format("%Y%m%d-%H%M%S");
    let mut result = AdbCommandResult {
        ok: true,
        command: "adb screencap".to_string(),
        stdout: String::new(),
        stderr: String::new(),
        lines: Vec::new(),
        exit_code: Some(0),
        paths: Vec::new(),
    };
    for serial in serials {
        let args = adb_serial_args(&serial, &["exec-out", "screencap", "-p"]);
        let executable = adb_executable()?;
        let mut process = Command::new(executable);
        process
            .args(&args)
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped());
        #[cfg(windows)]
        process.creation_flags(CREATE_NO_WINDOW);
        let output = process
            .output()
            .map_err(|error| format!("Could not capture {serial}: {error}"))?;
        if output.status.success() && output.stdout.len() > 8 {
            let path = folder.join(format!("adb-{serial}-{stamp}.png"));
            fs::write(&path, output.stdout)
                .map_err(|error| format!("Could not save screenshot: {error}"))?;
            result.paths.push(path.to_string_lossy().into_owned());
            result
                .lines
                .push(format!("Saved screenshot for {serial}: {}", path.display()));
        } else {
            result.ok = false;
            result.lines.push(format!(
                "Screenshot failed for {serial}: {}",
                String::from_utf8_lossy(&output.stderr).trim()
            ));
        }
    }
    result.stdout = result.lines.join("\n");
    result.exit_code = Some(if result.ok { 0 } else { 1 });
    Ok(result)
}

#[tauri::command]
fn export_adb_specs(serials: Vec<String>) -> Result<AdbCommandResult, String> {
    if serials.is_empty() {
        return Err("Select at least one device.".to_string());
    }
    let folder = dirs::document_dir()
        .unwrap_or_else(std::env::temp_dir)
        .join("MCPHub")
        .join("ADB Specs");
    fs::create_dir_all(&folder)
        .map_err(|error| format!("Could not create specs folder: {error}"))?;
    let stamp = chrono::Local::now().format("%Y%m%d-%H%M%S");
    let mut result = AdbCommandResult {
        ok: true,
        command: "adb shell getprop".to_string(),
        stdout: String::new(),
        stderr: String::new(),
        lines: Vec::new(),
        exit_code: Some(0),
        paths: Vec::new(),
    };
    for serial in serials {
        let command = run_adb(&adb_serial_args(&serial, &["shell", "getprop"]))?;
        let path = folder.join(format!("adb-{serial}-{stamp}.txt"));
        fs::write(&path, &command.stdout)
            .map_err(|error| format!("Could not save specs: {error}"))?;
        result
            .lines
            .push(format!("Saved specs for {serial}: {}", path.display()));
        result.paths.push(path.to_string_lossy().into_owned());
        if !command.ok {
            result.ok = false;
        }
    }
    result.stdout = result.lines.join("\n");
    result.exit_code = Some(if result.ok { 0 } else { 1 });
    Ok(result)
}

#[tauri::command]
fn install_adb_apk(apk_path: String, serials: Vec<String>) -> Result<AdbCommandResult, String> {
    let apk = Path::new(apk_path.trim());
    if !apk.is_file() {
        return Err("Select an existing APK file.".to_string());
    }
    if serials.is_empty() {
        return Err("Select at least one device.".to_string());
    }
    let mut result = AdbCommandResult {
        ok: true,
        command: format!("adb install {}", apk.display()),
        stdout: String::new(),
        stderr: String::new(),
        lines: Vec::new(),
        exit_code: Some(0),
        paths: Vec::new(),
    };
    for serial in serials {
        let mut args = adb_serial_args(&serial, &["install", "-r"]);
        args.push(apk.to_string_lossy().into_owned());
        let command = run_adb(&args)?;
        result.lines.push(format!(
            "{serial}: {}",
            if command.ok {
                command.stdout
            } else {
                command.stderr
            }
        ));
        if !command.ok {
            result.ok = false;
        }
    }
    result.stdout = result.lines.join("\n");
    result.exit_code = Some(if result.ok { 0 } else { 1 });
    Ok(result)
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct YouTubeToolsStatus {
    yt_dlp: bool,
    ffmpeg: bool,
    ffprobe: bool,
    output_dir: String,
}

#[derive(Clone, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct YouTubeChapter {
    index: usize,
    title: String,
    start_time: f64,
    end_time: f64,
    duration: f64,
}

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct YouTubeVideoInfo {
    id: String,
    title: String,
    duration: f64,
    uploader: String,
    thumbnail: String,
    chapters: Vec<YouTubeChapter>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct YouTubeClipResult {
    index: usize,
    title: String,
    file_path: String,
    start_time: f64,
    end_time: f64,
    duration: f64,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct YouTubeProcessResult {
    title: String,
    video_path: String,
    output_dir: String,
    clips: Vec<YouTubeClipResult>,
}

fn youtube_output_dir() -> PathBuf {
    dirs::video_dir()
        .unwrap_or_else(|| dirs::home_dir().unwrap_or_else(std::env::temp_dir))
        .join("Chapter Clipper")
}

fn youtube_executable(bundled: &str, names: &[&str]) -> Option<PathBuf> {
    let bundled_path = PathBuf::from(bundled);
    if bundled_path.is_file() {
        Some(bundled_path)
    } else {
        find_executable(names)
    }
}

fn yt_dlp_executable() -> Result<PathBuf, String> {
    youtube_executable(YOUTUBE_YT_DLP, &["yt-dlp.exe", "yt-dlp"]).ok_or_else(|| {
        "yt-dlp was not found. Install it or place yt-dlp.exe in the YouTube backend folder."
            .to_string()
    })
}

fn ffmpeg_executable() -> Result<PathBuf, String> {
    find_executable(&["ffmpeg.exe", "ffmpeg"])
        .ok_or_else(|| "ffmpeg was not found on PATH.".to_string())
}

fn media_command_output(mut command: Command, label: &str) -> Result<String, String> {
    command
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped());
    #[cfg(windows)]
    command.creation_flags(CREATE_NO_WINDOW);
    let output = command
        .output()
        .map_err(|error| format!("Could not run {label}: {error}"))?;
    if !output.status.success() {
        let stderr = String::from_utf8_lossy(&output.stderr).trim().to_string();
        return Err(format!(
            "{label} failed{}",
            if stderr.is_empty() {
                String::new()
            } else {
                format!(": {stderr}")
            }
        ));
    }
    Ok(String::from_utf8_lossy(&output.stdout).trim().to_string())
}

fn add_youtube_access_args(command: &mut Command) {
    if Path::new(YOUTUBE_COOKIES).is_file() {
        command.args(["--cookies", YOUTUBE_COOKIES]);
    }
    command.args(["--add-header", "User-Agent:Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/126 Safari/537.36"]);
}

fn youtube_info(url: &str) -> Result<YouTubeVideoInfo, String> {
    if !url.starts_with("https://") && !url.starts_with("http://") {
        return Err("Enter a valid YouTube URL.".to_string());
    }
    let mut command = Command::new(yt_dlp_executable()?);
    command.args([
        "--dump-single-json",
        "--skip-download",
        "--no-playlist",
        "--no-warnings",
    ]);
    add_youtube_access_args(&mut command);
    command.arg(url);
    let raw = media_command_output(command, "yt-dlp metadata")?;
    let value: serde_json::Value = serde_json::from_str(&raw)
        .map_err(|error| format!("yt-dlp returned invalid metadata: {error}"))?;
    let duration = value
        .get("duration")
        .and_then(|item| item.as_f64())
        .unwrap_or(0.0);
    let chapters = value
        .get("chapters")
        .and_then(|item| item.as_array())
        .map(|items| {
            items
                .iter()
                .enumerate()
                .filter_map(|(position, item)| {
                    let start = item.get("start_time")?.as_f64()?;
                    let end = item
                        .get("end_time")
                        .and_then(|value| value.as_f64())
                        .unwrap_or(duration);
                    if end <= start {
                        return None;
                    }
                    Some(YouTubeChapter {
                        index: position + 1,
                        title: item
                            .get("title")
                            .and_then(|value| value.as_str())
                            .unwrap_or("Chapter")
                            .to_string(),
                        start_time: start,
                        end_time: end,
                        duration: end - start,
                    })
                })
                .collect()
        })
        .unwrap_or_default();
    Ok(YouTubeVideoInfo {
        id: value
            .get("id")
            .and_then(|item| item.as_str())
            .unwrap_or_default()
            .to_string(),
        title: value
            .get("title")
            .and_then(|item| item.as_str())
            .unwrap_or("YouTube video")
            .to_string(),
        duration,
        uploader: value
            .get("uploader")
            .and_then(|item| item.as_str())
            .unwrap_or_default()
            .to_string(),
        thumbnail: value
            .get("thumbnail")
            .and_then(|item| item.as_str())
            .unwrap_or_default()
            .to_string(),
        chapters,
    })
}

fn safe_media_name(value: &str, fallback: &str) -> String {
    let invalid = Regex::new(r#"[<>:"\\/|?*\x00-\x1f]"#).expect("valid filename regex");
    let whitespace = Regex::new(r"[\s_-]+").expect("valid whitespace regex");
    let without_invalid = invalid.replace_all(value, "");
    let cleaned = whitespace.replace_all(without_invalid.trim(), "_");
    let result: String = cleaned.chars().take(80).collect();
    let result = result.trim_matches(['.', '_', '-']);
    if result.is_empty() {
        fallback.to_string()
    } else {
        result.to_string()
    }
}

#[tauri::command]
fn get_youtube_tools_status() -> YouTubeToolsStatus {
    YouTubeToolsStatus {
        yt_dlp: youtube_executable(YOUTUBE_YT_DLP, &["yt-dlp.exe", "yt-dlp"]).is_some(),
        ffmpeg: find_executable(&["ffmpeg.exe", "ffmpeg"]).is_some(),
        ffprobe: find_executable(&["ffprobe.exe", "ffprobe"]).is_some(),
        output_dir: youtube_output_dir().to_string_lossy().into_owned(),
    }
}

#[tauri::command]
async fn get_youtube_video_info(url: String) -> Result<YouTubeVideoInfo, String> {
    tauri::async_runtime::spawn_blocking(move || youtube_info(url.trim()))
        .await
        .map_err(|error| format!("Metadata task failed: {error}"))?
}

fn process_youtube_video_inner(
    url: String,
    chapters: Vec<YouTubeChapter>,
) -> Result<YouTubeProcessResult, String> {
    if chapters.is_empty() {
        return Err("Select or add at least one chapter.".to_string());
    }
    let info = youtube_info(url.trim())?;
    let folder = youtube_output_dir().join(safe_media_name(&info.title, "YouTube_Video"));
    let clips_dir = folder.join("clips");
    fs::create_dir_all(&clips_dir)
        .map_err(|error| format!("Could not create the output folder: {error}"))?;
    let output_template = folder.join("source.%(ext)s");
    let mut download = Command::new(yt_dlp_executable()?);
    download
        .args([
            "--no-playlist",
            "--no-warnings",
            "-f",
            "bestvideo+bestaudio/best",
            "--merge-output-format",
            "mp4",
            "--print",
            "after_move:filepath",
            "-o",
        ])
        .arg(&output_template);
    add_youtube_access_args(&mut download);
    download.arg(url.trim());
    let download_output = media_command_output(download, "yt-dlp download")?;
    let video_path = download_output
        .lines()
        .rev()
        .find_map(|line| {
            let candidate = PathBuf::from(line.trim());
            candidate.is_file().then_some(candidate)
        })
        .or_else(|| {
            fs::read_dir(&folder)
                .ok()?
                .filter_map(Result::ok)
                .map(|entry| entry.path())
                .find(|path| {
                    path.extension()
                        .and_then(|value| value.to_str())
                        .map(|value| value.eq_ignore_ascii_case("mp4"))
                        .unwrap_or(false)
                })
        })
        .ok_or_else(|| "yt-dlp finished but the downloaded MP4 could not be found.".to_string())?;
    let ffmpeg = ffmpeg_executable()?;
    let mut results = Vec::new();
    for (position, chapter) in chapters.iter().enumerate() {
        if chapter.end_time <= chapter.start_time || chapter.duration < 0.5 {
            continue;
        }
        let index = position + 1;
        let clip_path = clips_dir.join(format!(
            "{index:02}_{}.mp4",
            safe_media_name(&chapter.title, "Chapter")
        ));
        let mut cut = Command::new(&ffmpeg);
        cut.args([
            "-y",
            "-hide_banner",
            "-loglevel",
            "error",
            "-ss",
            &chapter.start_time.to_string(),
            "-i",
        ])
        .arg(&video_path)
        .args([
            "-t",
            &(chapter.end_time - chapter.start_time).to_string(),
            "-c:v",
            "libx264",
            "-crf",
            "18",
            "-preset",
            "fast",
            "-c:a",
            "aac",
            "-b:a",
            "192k",
            "-pix_fmt",
            "yuv420p",
            "-movflags",
            "+faststart",
        ])
        .arg(&clip_path);
        media_command_output(cut, &format!("ffmpeg clip {index}"))?;
        results.push(YouTubeClipResult {
            index,
            title: chapter.title.clone(),
            file_path: clip_path.to_string_lossy().into_owned(),
            start_time: chapter.start_time,
            end_time: chapter.end_time,
            duration: chapter.end_time - chapter.start_time,
        });
    }
    if results.is_empty() {
        return Err("No valid clips were produced.".to_string());
    }
    Ok(YouTubeProcessResult {
        title: info.title,
        video_path: video_path.to_string_lossy().into_owned(),
        output_dir: clips_dir.to_string_lossy().into_owned(),
        clips: results,
    })
}

#[tauri::command]
async fn process_youtube_video(
    url: String,
    chapters: Vec<YouTubeChapter>,
) -> Result<YouTubeProcessResult, String> {
    tauri::async_runtime::spawn_blocking(move || process_youtube_video_inner(url, chapters))
        .await
        .map_err(|error| format!("Media task failed: {error}"))?
}

#[tauri::command]
fn open_youtube_chrome() -> Result<(), String> {
    if !Path::new(CHROME_EXECUTABLE).is_file() {
        return Err(format!("Chrome was not found at {CHROME_EXECUTABLE}"));
    }
    Command::new(CHROME_EXECUTABLE)
        .arg("https://www.youtube.com/")
        .spawn()
        .map_err(|error| format!("Could not launch Chrome: {error}"))?;
    Ok(())
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct ChapterClipperSocketRequest {
    action: Option<String>,
    url: Option<String>,
    chapters: Option<Vec<YouTubeChapter>>,
}

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct ChapterClipperLogEntry {
    timestamp: String,
    level: String,
    message: String,
}

#[derive(Clone, Default)]
struct ChapterClipperLogState(Arc<Mutex<VecDeque<ChapterClipperLogEntry>>>);

#[derive(Clone, Default)]
struct LatestYouTubeVideoState(Arc<Mutex<Option<YouTubeVideoInfo>>>);

fn chapter_clipper_log(
    logs: &Arc<Mutex<VecDeque<ChapterClipperLogEntry>>>,
    level: &str,
    message: impl Into<String>,
) {
    let Ok(mut entries) = logs.lock() else { return };
    entries.push_back(ChapterClipperLogEntry {
        timestamp: chrono::Local::now().format("%H:%M:%S").to_string(),
        level: level.to_string(),
        message: message.into(),
    });
    while entries.len() > 100 {
        entries.pop_front();
    }
}

#[tauri::command]
fn get_chapter_clipper_logs(
    state: tauri::State<'_, ChapterClipperLogState>,
) -> Vec<ChapterClipperLogEntry> {
    state
        .0
        .lock()
        .map(|entries| entries.iter().cloned().collect())
        .unwrap_or_default()
}

#[tauri::command]
fn get_latest_extension_video(
    state: tauri::State<'_, LatestYouTubeVideoState>,
) -> Option<YouTubeVideoInfo> {
    state.0.lock().ok().and_then(|video| video.clone())
}

fn start_chapter_clipper_socket(
    logs: Arc<Mutex<VecDeque<ChapterClipperLogEntry>>>,
    latest_video: Arc<Mutex<Option<YouTubeVideoInfo>>>,
) -> Result<(), String> {
    let listener = TcpListener::bind(CHAPTER_CLIPPER_SOCKET)
        .map_err(|error| format!("Could not start Chapter Clipper socket: {error}"))?;
    chapter_clipper_log(
        &logs,
        "info",
        format!("Listening on ws://{CHAPTER_CLIPPER_SOCKET}"),
    );
    std::thread::spawn(move || {
        for stream in listener.incoming().flatten() {
            let logs = Arc::clone(&logs);
            let latest_video = Arc::clone(&latest_video);
            std::thread::spawn(move || {
                let Ok(mut socket) = tungstenite::accept(stream) else {
                    chapter_clipper_log(&logs, "error", "Extension WebSocket handshake failed");
                    return;
                };
                chapter_clipper_log(&logs, "info", "Extension connected");
                while let Ok(message) = socket.read() {
                    if !message.is_text() {
                        continue;
                    }
                    let request = serde_json::from_str::<ChapterClipperSocketRequest>(
                        message.to_text().unwrap_or_default(),
                    );
                    let payload = match request {
                        Ok(request) if request.action.as_deref() == Some("ping") => {
                            chapter_clipper_log(
                                &logs,
                                "success",
                                "Extension status check succeeded",
                            );
                            serde_json::json!({ "success": true, "ready": true })
                        }
                        Ok(request) if request.action.as_deref() == Some("fetch-chapters") => {
                            let url = request.url.unwrap_or_default();
                            chapter_clipper_log(&logs, "info", "Fetching chapters with yt-dlp");
                            match youtube_info(&url) {
                                Ok(info) => {
                                    chapter_clipper_log(
                                        &logs,
                                        "success",
                                        format!("yt-dlp found {} chapter(s)", info.chapters.len()),
                                    );
                                    if let Ok(mut latest) = latest_video.lock() {
                                        *latest = Some(info.clone());
                                    }
                                    serde_json::json!({ "success": true, "result": info })
                                }
                                Err(error) => {
                                    chapter_clipper_log(&logs, "error", &error);
                                    serde_json::json!({ "success": false, "error": error })
                                }
                            }
                        }
                        Ok(request) => {
                            let url = request.url.unwrap_or_default();
                            let chapters = request.chapters.unwrap_or_default();
                            chapter_clipper_log(
                                &logs,
                                "info",
                                format!("Processing {} chapter(s)", chapters.len()),
                            );
                            match process_youtube_video_inner(url, chapters) {
                                Ok(result) => {
                                    chapter_clipper_log(
                                        &logs,
                                        "success",
                                        format!("Created {} clip(s)", result.clips.len()),
                                    );
                                    serde_json::json!({ "success": true, "result": result })
                                }
                                Err(error) => {
                                    chapter_clipper_log(&logs, "error", &error);
                                    serde_json::json!({ "success": false, "error": error })
                                }
                            }
                        }
                        Err(error) => {
                            let error = format!("Invalid extension request: {error}");
                            chapter_clipper_log(&logs, "error", &error);
                            serde_json::json!({ "success": false, "error": error })
                        }
                    };
                    if socket
                        .send(tungstenite::Message::Text(payload.to_string().into()))
                        .is_err()
                    {
                        break;
                    }
                }
            });
        }
    });
    Ok(())
}

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct ClipboardSnapshot {
    content: String,
    extension: String,
}

#[derive(Default)]
struct ClipboardCache(Mutex<Option<ClipboardSnapshot>>);

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct ClipboardSaveResult {
    path: String,
    extension: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct ClipboardRunResult {
    output: String,
    extension: String,
    exit_code: i32,
    path: String,
}

fn regex_matches(pattern: &str, sample: &str) -> bool {
    Regex::new(pattern)
        .map(|regex| regex.is_match(sample))
        .unwrap_or(false)
}

fn looks_like_csv(sample: &str) -> bool {
    let lines: Vec<&str> = sample
        .lines()
        .filter(|line| !line.trim().is_empty())
        .take(5)
        .collect();
    if lines.len() < 2 {
        return false;
    }
    let delimiter = if lines[0].contains('\t') { '\t' } else { ',' };
    let expected = lines[0].split(delimiter).count();
    expected >= 2
        && lines
            .iter()
            .all(|line| line.split(delimiter).count() == expected)
}

fn detect_clipboard_extension(content: &str) -> String {
    let end = content
        .char_indices()
        .nth(CLIPBOARD_SAMPLE_LIMIT)
        .map(|(index, _)| index)
        .unwrap_or(content.len());
    let sample = content[..end].trim();
    if sample.is_empty() {
        return "txt".to_string();
    }

    if regex_matches(r"(?is)^\s*<\?xml\b", sample) {
        return "xml".to_string();
    }
    if regex_matches(r"(?is)^\s*(?:<!doctype\s+html\b|<html\b)", sample) {
        return "html".to_string();
    }
    if regex_matches(r"(?is)^\s*<svg\b", sample) {
        return "svg".to_string();
    }
    if ((sample.starts_with('{') && sample.ends_with('}'))
        || (sample.starts_with('[') && sample.ends_with(']')))
        && serde_json::from_str::<serde_json::Value>(sample).is_ok()
    {
        return "json".to_string();
    }
    if regex_matches(
        r"(?im)^\s*(?:@?echo\s+off\b|setlocal\b|endlocal\b|for\s+/(?:f|l|r|d)\b|if\s+(?:not\s+)?exist\b|%[\w]+%|dir\s+/(?:[a-z0-9:-]+\s*)+$|(?:cls|pause|ver|vol)\s*$)",
        sample,
    ) {
        return "bat".to_string();
    }

    let language_rules = [
        (
            "ahk",
            r"(?im)^\s*(?:#requires\s+autohotkey|#singleinstance\b)",
        ),
        (
            "py",
            r"(?im)^\s*(?:#!.*\bpython(?:3)?\b|from\s+[\w.]+\s+import\b|import\s+[\w.]+|(?:async\s+)?def\s+\w+\s*\(|class\s+\w+.*:|if\s+__name__\s*==|print\s*\(|raise\s+\w+|(?:for|while|try|except|with)\b.*:\s*$)",
        ),
        ("sh", r"(?im)^\s*#!.*\b(?:bash|sh|zsh)\b"),
        (
            "ps1",
            r"(?im)^\s*(?:param\s*\(|function\s+[\w-]+\s*\{|(?:get|set|new|remove|invoke|start|stop|write|out|select|where|foreach|measure|convertto|convertfrom|test)-\w+|\$[\w:]+\s*=)",
        ),
        (
            "cs",
            r"(?im)^\s*(?:using\s+[\w.]+;|namespace\s+[\w.]+|(?:public|private|internal)\s+(?:static\s+)?class\s+\w+)",
        ),
        (
            "java",
            r"(?im)^\s*(?:package\s+[\w.]+;|(?:public\s+)?class\s+\w+.*\{)",
        ),
        (
            "go",
            r"(?im)^\s*(?:package\s+\w+\s*$|func\s+\w+\s*\([^)]*\))",
        ),
        (
            "rs",
            r"(?im)^\s*(?:fn\s+\w+\s*\(|(?:pub\s+)?(?:struct|enum|trait|impl)\s+\w+)",
        ),
        ("php", r"(?im)^\s*<\?php\b"),
    ];
    for (extension, pattern) in language_rules {
        if regex_matches(pattern, sample) {
            return extension.to_string();
        }
    }

    if regex_matches(
        r#"(?im)^\s*(?:#include\s*(?:<[^>]+>|"[^"]+")|(?:int|void)\s+main\s*\()"#,
        sample,
    ) {
        return if regex_matches(
            r"(?im)^\s*(?:class|namespace)\s+\w+|std::|#include\s*<iostream>",
            sample,
        ) {
            "cpp"
        } else {
            "c"
        }
        .to_string();
    }
    if regex_matches(
        r#"(?im)^\s*(?:interface|type|enum)\s+\w+|:\s*(?:string|number|boolean|unknown|never)\b|import\s+type\s|^\s*(?:const|let|var)\s+\w+\s*=\s*<[^>\r\n]+>\s*\([^)]*:\s*[^)]+\)|^\s*(?:export\s+)?(?:async\s+)?function\s+\w+\s*<[^>\r\n]+>\s*\([^)]*:\s*[^)]+\)"#,
        sample,
    ) {
        return "ts".to_string();
    }
    if regex_matches(
        r#"(?im)^\s*(?:import|export)\s+.*\bfrom\s+['"]|^\s*(?:const|let|var)\s+\w+\s*=|=>|console\.(?:log|error|warn)\s*\(|require\s*\("#,
        sample,
    ) {
        return "js".to_string();
    }
    if regex_matches(
        r"(?ims)^\s*(?:@[\w-]+\s*)?(?:html|body|:root|[#.][\w-]+)[^{]*\{[\s\S]*:[^;{}]+;",
        sample,
    ) {
        return "css".to_string();
    }
    if regex_matches(
        r"(?im)^\s*(?:select|insert\s+into|update\s+\w+\s+set|delete\s+from|create\s+(?:table|database|view)|alter\s+table)\b",
        sample,
    ) {
        return "sql".to_string();
    }
    if regex_matches(r"(?m)^\s*\[[\w.-]+\]\s*$", sample)
        && regex_matches(
            r#"(?im)^\s*[\w.-]+\s*=\s*(?:["']|\d|true|false|\[)"#,
            sample,
        )
    {
        return "toml".to_string();
    }
    if regex_matches(r"(?m)^\s{0,3}(?:#{1,6}\s+\S|[-*+]\s+\S|>\s+\S|```)", sample) {
        return "md".to_string();
    }
    if regex_matches(
        r"(?m)^(?:---\s*$)?[\s\S]*^\s*[\w.-]+\s*:\s*(?:\S.*)?$",
        sample,
    ) {
        return "yaml".to_string();
    }
    if looks_like_csv(sample) {
        return "csv".to_string();
    }
    "txt".to_string()
}

fn read_clipboard_text() -> Result<String, String> {
    let mut clipboard = arboard::Clipboard::new()
        .map_err(|error| format!("Could not open the clipboard: {error}"))?;
    clipboard
        .get_text()
        .or_else(|_| Ok(String::new()))
        .map_err(|error: arboard::Error| error.to_string())
}

fn write_clipboard_text(content: &str) -> Result<(), String> {
    let mut clipboard = arboard::Clipboard::new()
        .map_err(|error| format!("Could not open the clipboard: {error}"))?;
    clipboard
        .set_text(content.to_string())
        .map_err(|error| format!("Could not update the clipboard: {error}"))
}

fn save_clipboard_content(content: &str) -> Result<(PathBuf, String), String> {
    if content.is_empty() {
        return Err("The clipboard has no text.".to_string());
    }
    let extension = detect_clipboard_extension(content);
    let desktop =
        dirs::desktop_dir().ok_or_else(|| "Could not locate the Desktop folder.".to_string())?;
    fs::create_dir_all(&desktop)
        .map_err(|error| format!("Could not create the Desktop folder: {error}"))?;
    let timestamp = chrono::Local::now().format("%Y-%m-%d_%H-%M-%S");
    let base_name = format!("clipboard_{timestamp}");
    let mut destination = desktop.join(format!("{base_name}.{extension}"));
    let mut counter = 2;
    while destination.exists() {
        destination = desktop.join(format!("{base_name}_{counter}.{extension}"));
        counter += 1;
    }
    fs::write(&destination, content.as_bytes())
        .map_err(|error| format!("Could not save clipboard text: {error}"))?;
    Ok((destination, extension))
}

fn find_executable(names: &[&str]) -> Option<PathBuf> {
    for name in names {
        let output = Command::new("where.exe")
            .arg(name)
            .creation_flags(CREATE_NO_WINDOW)
            .output()
            .ok()?;
        if output.status.success() {
            if let Some(path) = String::from_utf8_lossy(&output.stdout).lines().next() {
                return Some(PathBuf::from(path.trim()));
            }
        }
    }
    None
}

fn clipboard_execution_command(extension: &str, source_path: &Path) -> Result<Command, String> {
    let mut command = match extension {
        "py" => {
            let executable = find_executable(&["py.exe", "python.exe", "python3.exe"]).ok_or_else(|| "Python code was detected, but Python was not found.".to_string())?;
            let is_launcher = executable.file_stem().and_then(|name| name.to_str()).map(|name| name.eq_ignore_ascii_case("py")).unwrap_or(false);
            let mut command = Command::new(executable);
            if is_launcher { command.arg("-3"); }
            command.arg(source_path); command
        }
        "js" | "ts" => {
            let executable = find_executable(&["node.exe"]).ok_or_else(|| "JavaScript was detected, but Node.js was not found.".to_string())?;
            let mut command = Command::new(executable); command.arg(source_path); command
        }
        "ps1" => {
            let executable = find_executable(&["pwsh.exe", "powershell.exe"]).ok_or_else(|| "PowerShell code was detected, but PowerShell was not found.".to_string())?;
            let mut command = Command::new(executable);
            command.args(["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File"]).arg(source_path); command
        }
        "bat" => {
            let executable = find_executable(&["cmd.exe"]).ok_or_else(|| "Batch code was detected, but cmd.exe was not found.".to_string())?;
            let mut command = Command::new(executable); command.args(["/D", "/S", "/C"]).arg(source_path); command
        }
        _ => return Err(format!("The analyzer detected .{extension}, which is not runnable. Supported clipboard code: Python, JavaScript/TypeScript, PowerShell, and batch.")),
    };
    command
        .current_dir(source_path.parent().unwrap_or_else(|| Path::new(".")))
        .env("PYTHONUTF8", "1")
        .env("PYTHONIOENCODING", "utf-8")
        .env("NO_COLOR", "1")
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .creation_flags(CREATE_NO_WINDOW);
    Ok(command)
}

#[tauri::command]
fn get_clipboard_snapshot(
    state: tauri::State<'_, ClipboardCache>,
) -> Result<ClipboardSnapshot, String> {
    let content = read_clipboard_text()?;
    let mut cached = state
        .0
        .lock()
        .map_err(|_| "Clipboard state is unavailable.".to_string())?;
    if let Some(snapshot) = cached.as_ref() {
        if snapshot.content == content {
            return Ok(snapshot.clone());
        }
    }
    let extension = detect_clipboard_extension(&content);
    let snapshot = ClipboardSnapshot { content, extension };
    *cached = Some(snapshot.clone());
    Ok(snapshot)
}

#[tauri::command]
fn detect_clipboard_type(content: String) -> ClipboardSnapshot {
    let extension = detect_clipboard_extension(&content);
    ClipboardSnapshot { content, extension }
}

#[tauri::command]
fn save_clipboard_text(content: String) -> Result<ClipboardSaveResult, String> {
    let (path, extension) = save_clipboard_content(&content)?;
    Ok(ClipboardSaveResult {
        path: path.to_string_lossy().into_owned(),
        extension,
    })
}

#[tauri::command]
fn set_clipboard_text(
    state: tauri::State<'_, ClipboardCache>,
    content: String,
) -> Result<ClipboardSnapshot, String> {
    write_clipboard_text(&content)?;
    let snapshot = ClipboardSnapshot {
        extension: detect_clipboard_extension(&content),
        content,
    };
    *state
        .0
        .lock()
        .map_err(|_| "Clipboard state is unavailable.".to_string())? = Some(snapshot.clone());
    Ok(snapshot)
}

#[tauri::command]
fn run_clipboard_text(
    state: tauri::State<'_, ClipboardCache>,
    content: String,
) -> Result<ClipboardRunResult, String> {
    let (path, extension) = save_clipboard_content(&content)?;
    let mut child = clipboard_execution_command(&extension, &path)?
        .spawn()
        .map_err(|error| format!("Could not run clipboard code: {error}"))?;
    let timed_out = match child
        .wait_timeout(Duration::from_secs(CLIPBOARD_RUN_TIMEOUT_SECONDS))
        .map_err(|error| format!("Could not wait for clipboard code: {error}"))?
    {
        Some(_) => false,
        None => {
            let _ = child.kill();
            true
        }
    };
    let output = child
        .wait_with_output()
        .map_err(|error| format!("Could not collect clipboard output: {error}"))?;
    let exit_code = if timed_out {
        124
    } else {
        output.status.code().unwrap_or(-1)
    };
    let mut text = String::from_utf8_lossy(&output.stdout)
        .trim_end()
        .to_string();
    let stderr = String::from_utf8_lossy(&output.stderr)
        .trim_end()
        .to_string();
    if !stderr.is_empty() {
        if !text.is_empty() {
            text.push('\n');
        }
        text.push_str(&stderr);
    }
    if timed_out {
        if !text.is_empty() {
            text.push('\n');
        }
        text.push_str("Execution timed out after 60 seconds.");
    }
    if text.is_empty() {
        text = format!("Code completed with exit code {exit_code} and produced no output.");
    }
    if exit_code != 0 {
        text = format!(".{extension} exited with code {exit_code}\n{text}");
    }
    write_clipboard_text(&text)?;
    *state
        .0
        .lock()
        .map_err(|_| "Clipboard state is unavailable.".to_string())? = Some(ClipboardSnapshot {
        content: text.clone(),
        extension: detect_clipboard_extension(&text),
    });
    Ok(ClipboardRunResult {
        output: text,
        extension,
        exit_code,
        path: path.to_string_lossy().into_owned(),
    })
}

fn autostart_windows_hub(state: &HubProcessState) {
    let project_dir = match validate_project_dir(HubTarget::Windows, WINDOWS_PROJECT_DIR) {
        Ok(path) => path,
        Err(error) => {
            let _ = fs::write(
                std::env::temp_dir().join("mcphub-windows-process.log"),
                format!("Automatic Windows Hub startup failed: {error}\n"),
            );
            return;
        }
    };
    let _ = with_process(state, HubTarget::Windows, |process| {
        start_processes(HubTarget::Windows, process, &project_dir, "start")
    });
}

fn main() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .manage(HubProcessState::default())
        .manage(ClipboardCache::default())
        .manage(ChapterClipperLogState::default())
        .manage(LatestYouTubeVideoState::default())
        .setup(|app| {
            let clipper_logs = Arc::clone(&app.state::<ChapterClipperLogState>().0);
            let latest_video = Arc::clone(&app.state::<LatestYouTubeVideoState>().0);
            start_chapter_clipper_socket(clipper_logs, latest_video)
                .map_err(std::io::Error::other)?;
            let main_window =
                tauri::WebviewWindowBuilder::new(app, "main", tauri::WebviewUrl::default())
                    .title("MCPHub")
                    .inner_size(1280.0, 820.0)
                    .min_inner_size(900.0, 600.0)
                    .resizable(true)
                    .center();

            main_window.build()?;

            #[cfg(windows)]
            {
                autostart_windows_hub(app.state::<HubProcessState>().inner());
            }
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            run_hub_script,
            get_hub_process_status,
            stop_hub_process,
            check_endpoint_reachability,
            get_adb_devices,
            get_scrcpy_version,
            start_scrcpy_mirror,
            take_adb_screenshots,
            export_adb_specs,
            install_adb_apk,
            get_youtube_tools_status,
            get_youtube_video_info,
            process_youtube_video,
            open_youtube_chrome,
            get_chapter_clipper_logs,
            get_latest_extension_video,
            get_clipboard_snapshot,
            detect_clipboard_type,
            save_clipboard_text,
            set_clipboard_text,
            run_clipboard_text
        ])
        .run(tauri::generate_context!())
        .expect("error while running MCPHub frontend");
}

#[cfg(test)]
mod clipboard_tests {
    use super::detect_clipboard_extension;

    #[test]
    fn detects_common_clipboard_types() {
        assert_eq!(detect_clipboard_extension(r#"{"ready":true}"#), "json");
        assert_eq!(
            detect_clipboard_extension("import pathlib\nprint(pathlib.Path.cwd())"),
            "py"
        );
        assert_eq!(
            detect_clipboard_extension("interface User { name: string }"),
            "ts"
        );
        assert_eq!(
            detect_clipboard_extension("# Clipboard notes\n\n- one\n- two"),
            "md"
        );
        assert_eq!(
            detect_clipboard_extension("name,port\nwindows,3000\nwsl,3001"),
            "csv"
        );
        assert_eq!(detect_clipboard_extension("ordinary clipboard text"), "txt");
    }
}
