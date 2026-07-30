namespace MCPHub.Core;

public static class AppConstants
{
    public const string WindowsProjectDir = @"C:\Users\paul\projects\mcp_UI\mcphub";
    public const string WslProjectDir = "/mnt/c/Users/paul/Documents/Codex/2026-07-29/https-deepwiki-com-samanhappy-mcphub/mcphub";
    public const string WindowsNgrokConfig = @"C:\Users\paul\AppData\Local\ngrok\ngrok.yml";
    public const string WslNgrokConfig = @"C:\Users\paul\AppData\Local\ngrok\ngrok-wsl.yml";
    public const string YouTubeYtDlp = @"C:\Users\paul\projects\YouTube\backend\yt-dlp.exe";
    public const string YouTubeCookies = @"C:\Users\paul\projects\YouTube\backend\cookies.txt";
    public const string YouTubeDriveDir = @"G:\My Drive\video-drives";
    public const string ChromeExecutable = @"C:\Users\paul\AppData\Local\Google\Chrome\Application\chrome.exe";
    public const string SshWslUrl = "https://width-cucumber-wavy.ngrok-free.dev/mcp/ssh-wsl/";
    public const string PipeName = "MCPHub.ChapterClipper.v1";
    public const string NativeHostName = "com.mcphub.chapter_clipper";
    public const int LogTailBytes = 16_384;
    public const int ClipboardRunTimeoutSeconds = 60;
    public static readonly HashSet<string> AllowedScripts = ["build", "start", "backend:dev", "backend:debug", "dev", "debug"];
}

public enum HubTarget { Windows, Wsl }
