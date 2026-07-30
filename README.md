# MCPHub Tauri Frontend

The packaged Tauri interface is a dedicated Svelte app in `shell/`. Its sidebar
contains persistent Windows (`http://localhost:3000`) and WSL
(`http://localhost:3001`) dashboard tabs plus host-specific build, start,
restart, and stop controls. The existing React application remains the MCPHub
dashboard rendered inside each tab.

Windows desktop frontend for MCPHub. Starting the executable automatically runs
`node C:\Users\paul\projects\mcp_UI\mcphub\dist\index.js` on port `3000`, then starts the
`mcp-hub-windows` ngrok tunnel. The login page is bypassed because this setup
uses MCPHub's `skipAuth` mode.

The automatic launcher uses:

- MCPHub working directory: `C:\Users\paul\projects\mcp_UI\mcphub`
- MCPHub entry point: `C:\Users\paul\projects\mcp_UI\mcphub\dist\index.js`
- ngrok config: `C:\Users\paul\AppData\Local\ngrok\ngrok.yml`
- ngrok command: `ngrok start mcp-hub-windows --config <config>`

The Dashboard Hub Controls are split into independent Windows and WSL tabs.
Windows uses port `3000` with `ngrok.yml`; WSL commands run through `wsl.exe`
on port `3001` and use
`C:\Users\paul\AppData\Local\ngrok\ngrok-wsl.yml` with the
`mcp-hub-wsl` endpoint. Both tabs can launch these scripts from their selected
MCPHub source folder:

The Tauri desktop shell also embeds the complete MCPHub dashboards in persistent
host tabs: Windows at `http://localhost:3000` and WSL at
`http://localhost:3001`. Switching tabs changes the visible dashboard webview,
while the Processes button opens the build/start/stop controls.

- `pnpm build`
- `node C:\Users\paul\projects\mcp_UI\mcphub\dist\index.js` (`Start Hub`)
- `pnpm backend:dev`
- `pnpm backend:debug`
- `pnpm dev`
- `pnpm debug`

Only this fixed allowlist can be invoked. The selected folder must contain a
`package.json`. Long-running commands start without an extra console window and
can be stopped or restarted from the dashboard. Starting or restarting the
normal `start` script also starts ngrok. Their latest output is shown in the
expandable Command output area.

## Run in development

```powershell
corepack enable
pnpm install
pnpm tauri:dev
```

## Build the portable Windows executable

Run `build-portable.bat` from any location. The script resolves its own project
directory and writes:

```text
dist-portable\MCPHub-Frontend.exe
```

Requirements:

- Node.js 20 or newer with Corepack/pnpm
- Official Rust stable MSVC toolchain
- Microsoft C++ Build Tools with the Desktop development with C++ workload
- Microsoft Edge WebView2 Runtime (included with current Windows 10/11)

The Tauri bundle step is intentionally disabled. The release binary itself is
the portable single-file app; there is no installer and no embedded backend.

## Backend URL

The frontend is fixed to `http://localhost:3000`, including its desktop content
security policy.

The backend must allow requests from the Tauri webview origin and support
credentials if Better Auth is enabled. Token-based MCPHub login works through
the normal `/api/auth/login` endpoint.

## YouTube clip uploads

Enable the YouTube Data API v3 in a Google Cloud project and create a desktop
OAuth client. Put the client values in the project `.env` as
`GOOGLE_CLIENT_ID` and `GOOGLE_CLIENT_SECRET`. In YouTube Clipper, choose
**Connect with Google** to authorize the account, load or create a playlist,
and upload the generated clips directly into it.
