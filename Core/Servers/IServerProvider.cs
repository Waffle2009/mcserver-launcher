namespace McServerLauncher.Core.Servers;

public interface IServerProvider
{
    ServerType Type { get; }

    /// <summary>Returns available versions ordered newest first (index 0 = latest).</summary>
    Task<IReadOnlyList<string>> GetVersionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Downloads (and, for providers like Spigot that require it, builds) the server
    /// into <paramref name="targetDirectory"/>, returning the path to the runnable jar/exe.
    /// </summary>
    Task<string> InstallAsync(
        string version,
        string targetDirectory,
        IProgress<string>? log = null,
        CancellationToken ct = default);
}
