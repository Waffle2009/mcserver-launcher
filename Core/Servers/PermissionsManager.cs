using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using McServerLauncher.Core.Instances;

namespace McServerLauncher.Core.Servers;

public sealed record PermissionEntry(string Id, string Name, string Level);

/// <summary>
/// Manages operator/permission entries: ops.json for Java servers (Paper/Spigot),
/// permissions.json for Bedrock (BDS). The two formats differ, so this hides the
/// difference behind one API keyed off the instance's server type.
/// </summary>
public static class PermissionsManager
{
    private static readonly HttpClient Http = new();

    private sealed record JavaOpEntry(
        [property: JsonPropertyName("uuid")] string Uuid,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("level")] int Level,
        [property: JsonPropertyName("bypassesPlayerLimit")] bool BypassesPlayerLimit);

    private sealed record BdsPermissionEntry(
        [property: JsonPropertyName("permission")] string Permission,
        [property: JsonPropertyName("xuid")] string Xuid);

    private sealed record MojangProfile(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name);

    public static List<PermissionEntry> Load(ServerInstance instance)
    {
        if (instance.Type == ServerType.Bds)
        {
            var path = Path.Combine(instance.InstallDir, "permissions.json");
            if (!File.Exists(path)) return new List<PermissionEntry>();
            var entries = JsonSerializer.Deserialize<List<BdsPermissionEntry>>(File.ReadAllText(path)) ?? new();
            return entries.Select(e => new PermissionEntry(e.Xuid, e.Xuid, e.Permission)).ToList();
        }
        else
        {
            var path = Path.Combine(instance.InstallDir, "ops.json");
            if (!File.Exists(path)) return new List<PermissionEntry>();
            var entries = JsonSerializer.Deserialize<List<JavaOpEntry>>(File.ReadAllText(path)) ?? new();
            return entries.Select(e => new PermissionEntry(e.Uuid, e.Name, $"レベル{e.Level}")).ToList();
        }
    }

    /// <summary>
    /// Adds an operator by player name (Java, resolved to a UUID via the Mojang API)
    /// or by XUID (Bedrock, entered directly since there's no public name lookup).
    /// </summary>
    public static async Task AddAsync(ServerInstance instance, string nameOrXuid, CancellationToken ct = default)
    {
        if (instance.Type == ServerType.Bds)
        {
            var path = Path.Combine(instance.InstallDir, "permissions.json");
            var entries = File.Exists(path)
                ? JsonSerializer.Deserialize<List<BdsPermissionEntry>>(File.ReadAllText(path)) ?? new()
                : new List<BdsPermissionEntry>();

            if (entries.Any(e => e.Xuid == nameOrXuid))
                throw new InvalidOperationException("既に登録されています。");

            entries.Add(new BdsPermissionEntry("operator", nameOrXuid));
            Directory.CreateDirectory(instance.InstallDir);
            File.WriteAllText(path, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            var resp = await Http.GetAsync($"https://api.mojang.com/users/profiles/minecraft/{Uri.EscapeDataString(nameOrXuid)}", ct);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"プレイヤー名 '{nameOrXuid}' が見つかりませんでした。");

            var profile = await resp.Content.ReadFromJsonAsync<MojangProfile>(cancellationToken: ct)
                ?? throw new InvalidOperationException("Mojang APIの応答を解析できませんでした。");
            var uuid = InsertUuidDashes(profile.Id);

            var path = Path.Combine(instance.InstallDir, "ops.json");
            var entries = File.Exists(path)
                ? JsonSerializer.Deserialize<List<JavaOpEntry>>(File.ReadAllText(path)) ?? new()
                : new List<JavaOpEntry>();

            if (entries.Any(e => e.Uuid == uuid))
                throw new InvalidOperationException("既に登録されています。");

            entries.Add(new JavaOpEntry(uuid, profile.Name, 4, false));
            Directory.CreateDirectory(instance.InstallDir);
            File.WriteAllText(path, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    public static void Remove(ServerInstance instance, string id)
    {
        if (instance.Type == ServerType.Bds)
        {
            var path = Path.Combine(instance.InstallDir, "permissions.json");
            if (!File.Exists(path)) return;
            var entries = JsonSerializer.Deserialize<List<BdsPermissionEntry>>(File.ReadAllText(path)) ?? new();
            entries.RemoveAll(e => e.Xuid == id);
            File.WriteAllText(path, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            var path = Path.Combine(instance.InstallDir, "ops.json");
            if (!File.Exists(path)) return;
            var entries = JsonSerializer.Deserialize<List<JavaOpEntry>>(File.ReadAllText(path)) ?? new();
            entries.RemoveAll(e => e.Uuid == id);
            File.WriteAllText(path, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static string InsertUuidDashes(string raw) =>
        raw.Length == 32
            ? $"{raw[..8]}-{raw[8..12]}-{raw[12..16]}-{raw[16..20]}-{raw[20..]}"
            : raw;
}
