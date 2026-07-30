# MCPHub WinForms

Native Windows desktop control center for ADB/Scrcpy, YouTube chapter processing/upload, clipboard tools, and Chrome extension integration. The application uses .NET 10 WinForms with horizontal tabs and a C# backend.

## Features

- ADB device selection, Scrcpy mirrors, screenshots, specs export, and APK installation.
- yt-dlp/ffmpeg chapter clipping, mounted Drive relocation, playlists, and private YouTube uploads.
- Live clipboard editor, format detection, Desktop saving, session history, and supported script execution.
- Chapter Clipper Chrome extension connected through a C# native-messaging host.


## Development

Requirements: Windows x64 and .NET 10 SDK. External workflows additionally use their existing tools on `PATH` and the machine-specific paths preserved in `MCPHub.Core/AppConstants.cs`.

```powershell
dotnet restore .\winforms\MCPHub.slnx
dotnet test .\winforms\MCPHub.slnx
dotnet run --project .\winforms\MCPHub.App\MCPHub.App.csproj
```

## Portable build

Run `build-portable.bat` or `build-portable.ps1`. The self-contained output is written to:

```text
dist-portable\MCPHub-WinForms
```

The folder contains `MCPHub.exe`, the C# native host, the unpacked Chrome extension, and the native-host installer.

## Chrome extension

1. Open `chrome://extensions`, enable developer mode, and load `chrome-extension` from the portable folder.
2. Copy the displayed extension ID.
3. Run `install-chrome-native-host.ps1 -ExtensionId <id>` from the portable folder.
4. Start `MCPHub.exe`, then use the extension on YouTube.

The native host forwards `ping`, chapter-fetch, playlist, processing, and upload requests to the running desktop app through the local `MCPHub.ChapterClipper.v1` named pipe.
