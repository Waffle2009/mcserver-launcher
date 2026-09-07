using System.Text.Json;

namespace McServerLauncher.Core.Instances;

/// <summary>Persists the list of registered server instances as JSON under LocalAppData.</summary>
public sealed class ServerInstanceStore
{
    private readonly string _filePath;

    public ServerInstanceStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "McServerLauncher", "instances.json");
    }

    public List<ServerInstance> Load()
    {
        if (!File.Exists(_filePath)) return new List<ServerInstance>();
        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<List<ServerInstance>>(json) ?? new List<ServerInstance>();
    }

    public void Save(IEnumerable<ServerInstance> instances)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(instances.ToList(), new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}
