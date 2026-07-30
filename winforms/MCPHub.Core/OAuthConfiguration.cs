using System.Text.Json;

namespace MCPHub.Core;

public static class OAuthConfiguration
{
    public static (string ClientId, string ClientSecret) Load()
    {
        var environment = (Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID"), Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET"));
        if (!string.IsNullOrWhiteSpace(environment.Item1) && !string.IsNullOrWhiteSpace(environment.Item2))
            return (environment.Item1!, environment.Item2!);

        foreach (var path in CandidatePaths())
        {
            if (!File.Exists(path)) continue;
            try
            {
                var parsed = Parse(File.ReadAllText(path));
                if (parsed is not null) return parsed.Value;
            }
            catch (JsonException) { }
        }

        throw new InvalidOperationException("Google OAuth configuration is missing. Set GOOGLE_CLIENT_ID and GOOGLE_CLIENT_SECRET, or place client_secret.json beside MCPHub.exe.");
    }

    public static (string ClientId, string ClientSecret)? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        foreach (var name in new[] { "installed", "web" })
        {
            if (root.TryGetProperty(name, out var nested)) root = nested;
        }
        var id = Property(root, "client_id", "clientId");
        var secret = Property(root, "client_secret", "clientSecret");
        return string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret) ? null : (id, secret);
    }

    private static IEnumerable<string> CandidatePaths()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MCPHub");
        yield return Path.Combine(appData, "client_secret.json");
        yield return Path.Combine(appData, "youtube-oauth.json");
        yield return Path.Combine(AppContext.BaseDirectory, "client_secret.json");
        yield return Path.Combine(AppContext.BaseDirectory, "youtube-oauth.json");
        yield return Path.Combine(Environment.CurrentDirectory, "client_secret.json");
        yield return Path.Combine(Environment.CurrentDirectory, "youtube-oauth.json");
    }

    private static string? Property(JsonElement value, params string[] names)
    {
        foreach (var name in names)
            if (value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
                return property.GetString();
        return null;
    }
}
