using System.Globalization;
using System.Text.RegularExpressions;

namespace MCPHub.Core;

public static partial class TimestampParser
{
    [GeneratedRegex(@"^\s*(.+?)\s*:\s*((?:\d+:)?\d+:\d+)\s*-\s*((?:\d+:)?\d+:\d+)\s*$")]
    private static partial Regex LinePattern();

    public static IReadOnlyList<YouTubeChapter> Parse(string text)
    {
        var result = new List<YouTubeChapter>();
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = LinePattern().Match(line); if (!match.Success) continue;
            var start = ParseTime(match.Groups[2].Value); var end = ParseTime(match.Groups[3].Value);
            if (start is null || end is null || end <= start) continue;
            result.Add(new(result.Count + 1, match.Groups[1].Value.Trim(), start.Value, end.Value, end.Value - start.Value));
        }
        return result;
    }

    public static double? ParseTime(string value)
    {
        var parts = value.Split(':'); if (parts.Length is < 2 or > 3) return null;
        if (!parts.All(p => double.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _))) return null;
        var numbers = parts.Select(p => double.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        return parts.Length == 2 ? numbers[0] * 60 + numbers[1] : numbers[0] * 3600 + numbers[1] * 60 + numbers[2];
    }
}
