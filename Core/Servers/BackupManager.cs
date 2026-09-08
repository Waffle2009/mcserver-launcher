using System.IO.Compression;
using McServerLauncher.Core.Instances;

namespace McServerLauncher.Core.Servers;

public sealed record BackupInfo(string FilePath, string FileName, long SizeBytes, DateTime CreatedAt);

/// <summary>Zip-based full backups of a server's install directory, kept outside that directory.</summary>
public static class BackupManager
{
    public static string BackupsDir(ServerInstance instance) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "McServerLauncher", "backups", instance.Id);

    public static List<BackupInfo> List(ServerInstance instance)
    {
        var dir = BackupsDir(instance);
        if (!Directory.Exists(dir)) return new List<BackupInfo>();

        return Directory.GetFiles(dir, "*.zip")
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new BackupInfo(path, info.Name, info.Length, info.LastWriteTime);
            })
            .OrderByDescending(b => b.CreatedAt)
            .ToList();
    }

    public static string Create(ServerInstance instance)
    {
        if (!Directory.Exists(instance.InstallDir))
            throw new InvalidOperationException("サーバーのインストール先が見つかりません。");

        var dir = BackupsDir(instance);
        Directory.CreateDirectory(dir);

        var fileName = $"backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
        var path = Path.Combine(dir, fileName);
        ZipFile.CreateFromDirectory(instance.InstallDir, path, CompressionLevel.Optimal, includeBaseDirectory: false);
        return path;
    }

    public static void Restore(ServerInstance instance, string backupFilePath)
    {
        if (!File.Exists(backupFilePath))
            throw new FileNotFoundException("バックアップファイルが見つかりません。", backupFilePath);

        Directory.CreateDirectory(instance.InstallDir);
        ZipFile.ExtractToDirectory(backupFilePath, instance.InstallDir, overwriteFiles: true);
    }

    public static void Delete(string backupFilePath)
    {
        if (File.Exists(backupFilePath)) File.Delete(backupFilePath);
    }
}
