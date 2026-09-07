using System.Diagnostics;
using McServerLauncher.Core.Servers;

namespace McServerLauncher.Core.ProcessManagement;

public sealed class ServerProcessManager : IDisposable
{
    private System.Diagnostics.Process? _process;
    private System.Threading.Timer? _usageTimer;
    private TimeSpan _lastCpuTime;
    private DateTime _lastSampleTime;

    public event Action<string>? OutputReceived;
    public event Action<int>? Exited;
    public event Action<ResourceUsage>? ResourceUsageUpdated;

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
        _process.Exited += (_, _) =>
        {
            StopUsageMonitor();
            Exited?.Invoke(_process.ExitCode);
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        StartUsageMonitor();
    }

    private void StartUsageMonitor()
    {
        _lastCpuTime = TimeSpan.Zero;
        _lastSampleTime = DateTime.UtcNow;
        _usageTimer = new System.Threading.Timer(_ => SampleUsage(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void SampleUsage()
    {
        if (_process is not { HasExited: false } process) return;
        try
        {
            process.Refresh();
            var now = DateTime.UtcNow;
            var cpuTime = process.TotalProcessorTime;
            var elapsedMs = (now - _lastSampleTime).TotalMilliseconds;
            var cpuPercent = elapsedMs > 0
                ? Math.Clamp((cpuTime - _lastCpuTime).TotalMilliseconds / (Environment.ProcessorCount * elapsedMs) * 100.0, 0, 100)
                : 0;
            _lastCpuTime = cpuTime;
            _lastSampleTime = now;
            ResourceUsageUpdated?.Invoke(new ResourceUsage(cpuPercent, process.WorkingSet64));
        }
        catch (InvalidOperationException)
        {
            // Process exited between the HasExited check above and reading its stats.
        }
    }

    private void StopUsageMonitor()
    {
        _usageTimer?.Dispose();
        _usageTimer = null;
        ResourceUsageUpdated?.Invoke(new ResourceUsage(0, 0));
    }

    public async Task SendCommandAsync(string command)
    {
        if (_process is not { HasExited: false })
            throw new InvalidOperationException("サーバーが起動していません。");

        await _process.StandardInput.WriteLineAsync(command);
        await _process.StandardInput.FlushAsync();
    }

    public Task StopAsync() => SendCommandAsync("stop");

    public void Dispose()
    {
        _usageTimer?.Dispose();
        _process?.Dispose();
    }
}
