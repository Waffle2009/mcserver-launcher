using System.Diagnostics;
using McServerLauncher.Core.Servers;

namespace McServerLauncher.Core.ProcessManagement;

public sealed class ServerProcessManager : IDisposable
{
    private System.Diagnostics.Process? _process;

    public event Action<string>? OutputReceived;
    public event Action<int>? Exited;

    public bool IsRunning => _process is { HasExited: false };

    public void Start(ServerType type, string executablePath, int memoryMb = 2048)
    {
        if (IsRunning)
            throw new InvalidOperationException("サーバーは既に起動しています。");

        var workingDir = Path.GetDirectoryName(executablePath)
            ?? throw new ArgumentException("実行ファイルのディレクトリを特定できません。", nameof(executablePath));

        var psi = new ProcessStartInfo
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (type == ServerType.Bds)
        {
            psi.FileName = executablePath;
        }
        else
        {
            psi.FileName = "java";
            psi.ArgumentList.Add($"-Xmx{memoryMb}M");
            psi.ArgumentList.Add($"-Xms{memoryMb}M");
            psi.ArgumentList.Add("-jar");
            psi.ArgumentList.Add(Path.GetFileName(executablePath));
            psi.ArgumentList.Add("nogui");
        }

        _process = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => { if (e.Data != null) OutputReceived?.Invoke(e.Data); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data != null) OutputReceived?.Invoke(e.Data); };
        _process.Exited += (_, _) => Exited?.Invoke(_process.ExitCode);

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public async Task SendCommandAsync(string command)
    {
        if (_process is not { HasExited: false })
            throw new InvalidOperationException("サーバーが起動していません。");

        await _process.StandardInput.WriteLineAsync(command);
        await _process.StandardInput.FlushAsync();
    }

    public Task StopAsync() => SendCommandAsync("stop");

    public void Dispose() => _process?.Dispose();
}
