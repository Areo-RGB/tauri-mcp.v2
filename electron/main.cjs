const path = require("node:path");
const { app, BrowserWindow, clipboard, dialog, ipcMain, shell } = require("electron");
const { AdbService } = require("./services/adb.cjs");
const { ClipboardService } = require("./services/clipboard.cjs");
const { YouTubeService } = require("./services/youtube.cjs");
const { loadDotEnv } = require("./lib/process.cjs");

let mainWindow;
let youtube;

function assertTrustedRenderer(event) {
  const source = event.senderFrame?.url ?? "";
  const trusted = source.startsWith("file://") || source.startsWith("http://127.0.0.1:5174/");
  if (!trusted) throw new Error("Blocked IPC request from an untrusted renderer.");
}

function registerDesktopBridge() {
  const adb = new AdbService();
  const clipboards = new ClipboardService(clipboard, app.getPath("desktop"));
  youtube = new YouTubeService({
    shell,
    tokenPath: path.join(app.getPath("appData"), "MCPHub", "youtube-token.json"),
    videosPath: app.getPath("videos"),
  });

  const commands = new Map([
    ["get_adb_devices", () => adb.getDevices()],
    ["get_scrcpy_version", () => adb.getScrcpyVersion()],
    ["start_scrcpy_mirror", (args) => adb.startMirror(args)],
    ["take_adb_screenshots", (args) => adb.screenshots(args)],
    ["export_adb_specs", (args) => adb.exportSpecs(args)],
    ["install_adb_apk", (args) => adb.installApk(args)],
    ["get_clipboard_snapshot", () => clipboards.snapshot()],
    ["detect_clipboard_type", (args) => clipboards.detect(args)],
    ["save_clipboard_text", (args) => clipboards.save(args)],
    ["set_clipboard_text", (args) => clipboards.set(args)],
    ["run_clipboard_text", (args) => clipboards.run(args)],
    ["get_youtube_tools_status", () => youtube.toolsStatus()],
    ["get_youtube_video_info", (args) => youtube.videoInfo(args)],
    ["process_youtube_video", (args) => youtube.processVideo(args)],
    ["youtube_authenticate", () => youtube.authenticate()],
    ["get_youtube_auth_status", () => youtube.authStatus()],
    ["disconnect_youtube", () => youtube.disconnect()],
    ["get_youtube_playlists", () => youtube.playlists()],
    ["create_youtube_playlist", (args) => youtube.createPlaylist(args)],
    ["upload_youtube_clips", (args) => youtube.uploadClips(args)],
    ["open_youtube_chrome", () => youtube.openBrowser("https://www.youtube.com/")],
    ["get_chapter_clipper_logs", () => [...youtube.logs]],
    ["get_latest_extension_video", () => youtube.latestVideo],
  ]);

  ipcMain.handle("mcphub:invoke", async (event, command, args = {}) => {
    assertTrustedRenderer(event);
    const handler = commands.get(command);
    if (!handler) throw new Error(`Unknown desktop command: ${command}`);
    return handler(args);
  });

  ipcMain.handle("mcphub:open-file", async (event, options = {}) => {
    assertTrustedRenderer(event);
    const result = await dialog.showOpenDialog(mainWindow, {
      properties: ["openFile"],
      filters: Array.isArray(options.filters) ? options.filters : [],
    });
    return result.canceled ? null : (result.filePaths[0] ?? null);
  });

  youtube.startSocket();
}

function createWindow() {
  mainWindow = new BrowserWindow({
    title: "MCPHub Tools",
    width: 1280,
    height: 820,
    minWidth: 900,
    minHeight: 600,
    center: true,
    show: false,
    autoHideMenuBar: true,
    webPreferences: {
      preload: path.join(__dirname, "preload.cjs"),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      webSecurity: true,
    },
  });

  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    if (/^https?:\/\//i.test(url)) void shell.openExternal(url);
    return { action: "deny" };
  });

  mainWindow.once("ready-to-show", () => mainWindow.show());
  mainWindow.on("closed", () => {
    mainWindow = null;
  });

  if (app.isPackaged) {
    void mainWindow.loadFile(path.join(__dirname, "..", "dist", "index.html"));
  } else {
    void mainWindow.loadURL("http://127.0.0.1:5174/");
  }
}

if (!app.requestSingleInstanceLock()) {
  app.quit();
} else {
  app.on("second-instance", () => {
    if (mainWindow?.isMinimized()) mainWindow.restore();
    mainWindow?.focus();
  });

  app.whenReady().then(() => {
    loadDotEnv(path.join(process.cwd(), ".env"));
    loadDotEnv(path.join(path.dirname(app.getPath("exe")), ".env"));
    registerDesktopBridge();
    createWindow();
  });

  app.on("activate", () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });

  app.on("window-all-closed", () => {
    youtube?.close();
    if (process.platform !== "darwin") app.quit();
  });
}
