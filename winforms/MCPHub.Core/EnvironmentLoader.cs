namespace MCPHub.Core;

public static class EnvironmentLoader
{
    public static void LoadOptional(params string[] roots)
    {
        foreach (var root in roots.Where(Directory.Exists))
        {
            var path = Path.Combine(root, ".env");
            if (!File.Exists(path)) continue;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var split = line.IndexOf('=');
                if (split <= 0) continue;
                var name = line[..split].Trim();
                var value = line[(split + 1)..].Trim().Trim('"', '\'');
                if (Environment.GetEnvironmentVariable(name) is null)
                    Environment.SetEnvironmentVariable(name, value);
            }
            return;
        }
    }
}
