using System.Diagnostics;

namespace McServerLauncher.Core.Servers;

/// <summary>
/// Spigot has no distributable jar (license restriction) — BuildTools compiles it
/// locally from source, which requires a JDK and can take several minutes.
/// </summary>
public sealed class SpigotProvider : IServerProvider
{
    private const string BuildToolsUrl =
        "https://hub.spigotmc.org/jenkins/job/BuildTools/lastSuccessfulBuild/artifact/target/BuildTools.jar";

    private static readonly HttpClient Http = new();

    public ServerType Type => ServerType.Spigot;

    public Task<IReadOnlyList<string>> GetVersionsAsync(CancellationToken ct = default) =>
        // Spigot's supported Minecraft versions track Paper's closely enough to reuse its list.
        new PaperProvider().GetVersionsAsync(ct);

    public async Task<string> InstallAsync(
        string version,
        string targetDirectory,
        IProgress<string>? log = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(targetDirectory);
        var buildToolsPath = Path.Combine(targetDirectory, "BuildTools.jar");

        log?.Report("BuildTools.jar をダウンロードしています...");
        using (var resp = await Http.GetAsync(BuildToolsUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(buildToolsPath);
            await resp.Content.CopyToAsync(fs, ct);
        }

        log?.Report($"Spigot {version} をビルドしています(数分かかる場合があります)...");
        var psi = new ProcessStartInfo
        {
            FileName = "java",
            WorkingDirectory = targetDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-jar");
        psi.ArgumentList.Add(buildToolsPath);
        psi.ArgumentList.Add("--rev");
        psi.ArgumentList.Add(version);

        using var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) log?.Report(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) log?.Report(e.Data); };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"BuildTools がエラー終了しました (exit code {proc.ExitCode})。");

        var jar = Directory.GetFiles(targetDirectory, $"spigot-{version}.jar").FirstOrDefault()
            ?? Directory.GetFiles(targetDirectory, "spigot-*.jar")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

        if (jar is null)
            throw new InvalidOperationException("ビルド後の Spigot jar が見つかりませんでした。");

        log?.Report("ビルド完了しました。");
        return jar;
    }
}
