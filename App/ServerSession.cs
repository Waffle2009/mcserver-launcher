using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using McServerLauncher.Core.Instances;
using McServerLauncher.Core.ProcessManagement;
using McServerLauncher.Core.Servers;

namespace McServerLauncher.App;

/// <summary>Runtime state for one registered server: its process manager, live stats and log buffer.</summary>
public sealed class ServerSession : INotifyPropertyChanged
{
    private bool _isRunning;
    private double _cpuPercent;
    private long _memoryBytes;

    public ServerInstance Instance { get; }
    public ServerProcessManager ProcessManager { get; } = new();
    public string LogBuffer { get; set; } = "";
    public ObservableCollection<AccessLogRow> AccessLog { get; } = new();
    public ObservableCollection<string> ConnectedPlayers { get; } = new();

    public ServerSession(ServerInstance instance)
    {
        Instance = instance;
    }

    public string Name => Instance.Name;

    public string TypeText => Instance.Type switch
    {
        ServerType.Paper => "Paper",
        ServerType.Spigot => "Spigot",
        ServerType.Bds => "BDS",
        _ => Instance.Type.ToString()
    };

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (_isRunning == value) return;
            _isRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(CpuText));
            OnPropertyChanged(nameof(MemoryText));
        }
    }

    public string StatusText => IsRunning ? "起動中" : "停止中";

    public double CpuPercent
    {
        get => _cpuPercent;
        set
        {
            _cpuPercent = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CpuText));
        }
    }

    public long MemoryBytes
    {
        get => _memoryBytes;
        set
        {
            _memoryBytes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MemoryText));
        }
    }

    public string CpuText => IsRunning ? $"{CpuPercent:0.0}%" : "--";

    public string MemoryText => IsRunning ? $"{MemoryBytes / 1024.0 / 1024.0:0} MB" : "--";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
