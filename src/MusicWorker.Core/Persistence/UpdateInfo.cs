using System.Text.Json;

namespace MusicWorker.Core.Persistence;

/// <summary>Die neueste veröffentlichte Version laut GitHub („releases/latest“).</summary>
public sealed record UpdateInfo(Version Version, string Tag, string PageUrl, string? SetupUrl)
{
    /// <summary>Liest die Antwort der GitHub-API; null, wenn sie unbrauchbar ist.</summary>
    public static UpdateInfo? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("tag_name", out var tagElement) || tagElement.GetString() is not { } tag ||
                ParseVersion(tag) is not { } version)
            {
                return null;
            }

            if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
            {
                return null;
            }

            string page = root.TryGetProperty("html_url", out var url) && url.GetString() is { } u ? u : string.Empty;
            string? setup = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    string? name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (name is not null && name.StartsWith("MusicWorker-Setup", StringComparison.OrdinalIgnoreCase) &&
                        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                        asset.TryGetProperty("browser_download_url", out var download))
                    {
                        setup = download.GetString();
                    }
                }
            }

            return new UpdateInfo(version, tag, page, setup);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>„v0.8.0“, „0.8“ oder „v1.0.0-beta“ → 0.8.0 bzw. 1.0.0 (null, wenn keine Version erkennbar ist).</summary>
    public static Version? ParseVersion(string text)
    {
        string trimmed = text.Trim().TrimStart('v', 'V');
        int end = 0;
        while (end < trimmed.Length && (char.IsDigit(trimmed[end]) || trimmed[end] == '.'))
        {
            end++;
        }

        var parts = trimmed[..end].Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Length > 4 || !parts.All(p => int.TryParse(p, out _)))
        {
            return null;
        }

        var numbers = parts.Select(int.Parse).Concat(Enumerable.Repeat(0, 3)).Take(Math.Max(3, parts.Length)).ToArray();
        return numbers.Length == 4 ? new Version(numbers[0], numbers[1], numbers[2], numbers[3]) : new Version(numbers[0], numbers[1], numbers[2]);
    }

    /// <summary>True, wenn diese Version neuer ist als <paramref name="current"/> (nur Major.Minor.Build zählen).</summary>
    public bool IsNewerThan(Version current) =>
        Normalize(Version) > Normalize(current);

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
}
