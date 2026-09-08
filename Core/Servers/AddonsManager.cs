using McServerLauncher.Core.Instances;

namespace McServerLauncher.Core.Servers;

public sealed record AddonEntry(string Name, string Path, bool IsEnabled, bool IsFolder);

/// <summary>
/// Manages add-ons: plugin jars under "plugins" for Java servers (Paper/Spigot),
/// or behavior/resource pack folders for Bedrock (BDS).
/// </summary>
public static class AddonsManager
{
    public static bool SupportsToggle(ServerType type) => type != ServerType.Bds;

    private static string PluginsDir(ServerInstance instance) => Path.Combine(instance.InstallDir, "plugins");
    private static string BehaviorPacksDir(ServerInstance instance) => Path.Combine(instance.InstallDir, "behavior_packs");
    private static string ResourcePacksDir(ServerInstance instance) => Path.Combine(instance.InstallDir, "resource_packs");

    public static List<AddonEntry> List(ServerInstance instance)
    {
        if (instance.Type == ServerType.Bds)
        {
            var result = new List<AddonEntry>();
            foreach (var dir in new[] { BehaviorPacksDir(instance), ResourcePacksDir(instance) })
            {
                if (!Directory.Exists(dir)) continue;
                result.AddRange(Directory.GetDirectories(dir)
                    .Select(d => new AddonEntry(Path.GetFileName(d), d, IsEnabled: true, IsFolder: true)));
            }
            return result;
        }

        var pluginsDir = PluginsDir(instance);
        if (!Directory.Exists(pluginsDir)) return new List<AddonEntry>();

        return Directory.GetFiles(pluginsDir, "*.jar*")
            .Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
            .Select(f =>
            {
                var enabled = f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
                var name = Path.GetFileName(enabled ? f : f[..^".disabled".Length]);
                return new AddonEntry(name, f, enabled, IsFolder: false);
            })
            .ToList();
    }

    public static void Toggle(AddonEntry entry)
    {
        if (entry.IsFolder) throw new NotSupportedException("BDSのアドオンは有効/無効の切り替えに対応していません。");

        var newPath = entry.IsEnabled ? entry.Path + ".disabled" : entry.Path[..^".disabled".Length];
        File.Move(entry.Path, newPath);
    }

    public static void Delete(AddonEntry entry)
    {
        if (entry.IsFolder) Directory.Delete(entry.Path, recursive: true);
        else File.Delete(entry.Path);
    }

    /// <summary>Copies a plugin jar (Java) into the plugins folder.</summary>
    public static void AddPluginFile(ServerInstance instance, string sourceJarPath)
    {
        var dir = PluginsDir(instance);
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, Path.GetFileName(sourceJarPath));
        File.Copy(sourceJarPath, dest, overwrite: true);
    }

    /// <summary>Copies a pack folder (Bedrock) into behavior_packs or resource_packs.</summary>
    public static void AddPackFolder(ServerInstance instance, string sourceFolderPath, bool isBehaviorPack)
    {
        var dir = isBehaviorPack ? BehaviorPacksDir(instance) : ResourcePacksDir(instance);
        var dest = Path.Combine(dir, Path.GetFileName(sourceFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        CopyDirectory(sourceFolderPath, dest);
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
        foreach (var subDir in Directory.GetDirectories(sourceDir))
            CopyDirectory(subDir, Path.Combine(destDir, Path.GetFileName(subDir)));
    }
}
