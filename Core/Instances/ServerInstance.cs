using McServerLauncher.Core.Servers;

namespace McServerLauncher.Core.Instances;

public sealed class ServerInstance
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新しいサーバー";
    public ServerType Type { get; set; } = ServerType.Paper;
    public string InstallDir { get; set; } = "";
    public string? ExecutablePath { get; set; }
    public string? Version { get; set; }
    public int MemoryMb { get; set; } = 2048;
}
