# MCPHub Tools — Electron

Windows desktop tools built entirely with Electron, Node.js, Svelte, and TypeScript.
The Tauri/Rust backend and the Windows/WSL MCPHub dashboards have been removed.

## Included tools

- **YouTube Clipper** — yt-dlp metadata/downloads, FFmpeg chapter cutting,
  Google Drive handoff, Google OAuth, playlist management, resumable uploads,
  and the Chapter Clipper WebSocket server on `127.0.0.1:32145`.
- **ADB / Scrcpy** — device discovery, mirroring, screenshots, property exports,
  and APK installation.
- **Clipboard Saver** — live Windows clipboard preview, file-type detection,
  Desktop saves, and guarded execution for Python, JavaScript/TypeScript,
  PowerShell, and batch files.

The Svelte renderer cannot access Node directly. A context-isolated Electron
preload exposes only the allowlisted commands used by these three tools.

## Development

Requirements:

- Node.js 20 or newer
- Corepack/pnpm

```powershell
corepack enable
pnpm install
pnpm electron:dev
```

No Rust compiler, Cargo, WebView2 SDK, or MCPHub backend is required.

## Portable Windows build

Run from any folder:

```powershell
.\build-portable.bat
```

The script builds `dist-electron\MCPHub-Frontend.exe` and copies it to the
Desktop. `build-debug.bat` creates an unpacked development build under
`dist-electron\win-unpacked`.

## YouTube setup

Put these values in a `.env` next to the project during development or next to
the portable executable:

```dotenv
GOOGLE_CLIENT_ID=your-desktop-oauth-client-id
GOOGLE_CLIENT_SECRET=your-desktop-oauth-client-secret
YOUTUBE_DRIVE_DIR=G:\My Drive\video-drives
```

Enable YouTube Data API v3 and use a Google desktop OAuth client. Tokens remain
in `%APPDATA%\MCPHub\youtube-token.json`.

The app checks for:

- `C:\Users\paul\projects\YouTube\backend\yt-dlp.exe`, then `yt-dlp` on PATH
- `ffmpeg` and `ffprobe` on PATH
- optional cookies at `C:\Users\paul\projects\YouTube\backend\cookies.txt`

The unpacked Chrome extension is in `extensions\chapter-clipper`. Load that
folder through `chrome://extensions` with Developer mode enabled. It talks
directly to the Electron-owned WebSocket server; the old Rust native-messaging
host is no longer required.

## Security boundary

The BrowserWindow uses context isolation, renderer sandboxing, disabled Node
integration, a fixed preload bridge, an IPC command allowlist, and external-link
interception. File selection is handled by Electron's native dialog.
