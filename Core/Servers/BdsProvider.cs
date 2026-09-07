using System.Net.Http.Json;
using System.Text.Json;

namespace McServerLauncher.Core.Servers;

/// <summary>
/// Bedrock Dedicated Server. Mojang only distributes the latest build (no version
/// history), so GetVersionsAsync returns a single "latest" entry resolved lazily.
/// </summary>
public sealed class BdsProvider : IServerProvider
{
    private const string LinksApi =
        "https://net-secondary.web.minecraft-services.net/api/v1.0/download/links";

    private static readonly HttpClient Http = new();

    public ServerType Type => ServerType.Bds;

    public async Task<IReadOnlyList<string>> GetVersionsAsync(CancellationToken ct = default)
    {
        var url = await GetWindowsDownloadUrlAsync(ct);
        var version = ExtractVersion(url);
        return new[] { version };
    }

    public async Task<string> InstallAsync(
        string version,
        string targetDirectory,
        IProgress<string>? log = null,
        CancellationToken ct = default)
    {
        log?.Report("BDS の最新ダウンロードリンクを取得しています...");
        var url = await GetWindowsDownloadUrlAsync(ct);
        var fileName = Path.GetFileName(new Uri(url).LocalPath);

        Directory.CreateDirectory(targetDirectory);
        var zipPath = Path.Combine(targetDirectory, fileName);

        log?.Report($"{fileName} をダウンロードしています...");
        using (var req = new HttpRequestMessage(HttpMethod.Get, url))
        {
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0");
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(zipPath);
            await resp.Content.CopyToAsync(fs, ct);
        }

        log?.Report("展開しています...");
        System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, targetDirectory, overwriteFiles: true);
        File.Delete(zipPath);

        log?.Report("ダウンロード完了しました。");
        return Path.Combine(targetDirectory, "bedrock_server.exe");
    }

    private static async Task<string> GetWindowsDownloadUrlAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, LinksApi);
        req.Headers.UserAgent.ParseAdd("Mozilla/5.0");
        using var resp = await Http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

        foreach (var link in doc.GetProperty("result").GetProperty("links").EnumerateArray())
        {
            if (link.GetProperty("downloadType").GetString() == "serverBedrockWindows")
                return link.GetProperty("downloadUrl").GetString()!;
        }

        throw new InvalidOperationException("BDS Windows 版のダウンロードリンクが見つかりませんでした。");
    }

    private static string ExtractVersion(string url)
    {
        var name = Path.GetFileNameWithoutExtension(url); // bedrock-server-1.26.45.1
        var idx = name.LastIndexOf("bedrock-server-", StringComparison.Ordinal);
        return idx >= 0 ? name["bedrock-server-".Length..] : name;
    }
}
