using System.Net.Http.Json;
using System.Text.Json;

namespace McServerLauncher.Core.Servers;

public sealed class PaperProvider : IServerProvider
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("https://fill.papermc.io/v3/")
    };

    public ServerType Type => ServerType.Paper;

    public async Task<IReadOnlyList<string>> GetVersionsAsync(CancellationToken ct = default)
    {
        var doc = await Http.GetFromJsonAsync<JsonElement>("projects/paper", ct);
        var versions = new List<string>();
        foreach (var group in doc.GetProperty("versions").EnumerateObject())
            foreach (var v in group.Value.EnumerateArray())
                versions.Add(v.GetString()!);
        return versions;
    }

    public async Task<string> InstallAsync(
        string version,
        string targetDirectory,
        IProgress<string>? log = null,
        CancellationToken ct = default)
    {
        log?.Report($"Paper {version} のビルド情報を取得しています...");
        var build = await Http.GetFromJsonAsync<JsonElement>(
            $"projects/paper/versions/{version}/builds/latest", ct);

        var download = build.GetProperty("downloads").GetProperty("server:default");
        var url = download.GetProperty("url").GetString()!;
        var fileName = download.GetProperty("name").GetString()!;

        Directory.CreateDirectory(targetDirectory);
        var destPath = Path.Combine(targetDirectory, fileName);

        log?.Report($"{fileName} をダウンロードしています...");
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using (var fs = File.Create(destPath))
            await resp.Content.CopyToAsync(fs, ct);

        log?.Report("ダウンロード完了しました。");
        return destPath;
    }
}
