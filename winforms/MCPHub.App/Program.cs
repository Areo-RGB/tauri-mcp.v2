using MCPHub.Core;

namespace MCPHub.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\MCPHub.WinForms.SingleInstance", out var first);
        if (!first) { MessageBox.Show("MCPHub is already running.", "MCPHub", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        ApplicationConfiguration.Initialize();
        EnvironmentLoader.LoadOptional(Environment.CurrentDirectory, AppContext.BaseDirectory,
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")));
        Application.Run(new MainForm());
    }
}
