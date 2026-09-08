namespace McServerLauncher.Core.Servers;

/// <summary>
/// Reads and writes a Minecraft-style server.properties file (key=value lines),
/// preserving comments and line order so re-saving doesn't reformat the file.
/// </summary>
public static class ServerPropertiesFile
{
    public static string PathFor(string installDir) => Path.Combine(installDir, "server.properties");

    public static List<KeyValuePair<string, string>> Load(string installDir)
    {
        var path = PathFor(installDir);
        var result = new List<KeyValuePair<string, string>>();
        if (!File.Exists(path)) return result;

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

            var idx = trimmed.IndexOf('=');
            if (idx < 0) continue;

            result.Add(new KeyValuePair<string, string>(trimmed[..idx], trimmed[(idx + 1)..]));
        }
        return result;
    }

    public static string? GetValue(string installDir, string key) =>
        Load(installDir).FirstOrDefault(kv => kv.Key == key).Value;

    /// <summary>Rewrites the file with the given values, preserving comments and the original key order.</summary>
    public static void Save(string installDir, IReadOnlyList<KeyValuePair<string, string>> values)
    {
        var path = PathFor(installDir);
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        var byKey = values.ToDictionary(kv => kv.Key, kv => kv.Value);
        var written = new HashSet<string>();

        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

            var idx = trimmed.IndexOf('=');
            if (idx < 0) continue;

            var key = trimmed[..idx];
            if (byKey.TryGetValue(key, out var value))
            {
                lines[i] = $"{key}={value}";
                written.Add(key);
            }
        }

        foreach (var kv in values)
        {
            if (written.Contains(kv.Key)) continue;
            lines.Add($"{kv.Key}={kv.Value}");
        }

        Directory.CreateDirectory(installDir);
        File.WriteAllLines(path, lines);
    }
}
