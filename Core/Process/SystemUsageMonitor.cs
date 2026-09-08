using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LibreHardwareMonitor.Hardware;

[assembly: SupportedOSPlatform("windows")]

namespace McServerLauncher.Core.ProcessManagement;

public readonly record struct SystemUsage(double CpuPercent, long UsedMemoryBytes, long TotalMemoryBytes, double? CpuTemperatureCelsius);

/// <summary>Samples machine-wide CPU and memory usage on a timer, independent of any running server process.</summary>
public sealed class SystemUsageMonitor : IDisposable
{
    private readonly System.Threading.Timer _timer;
    private readonly Computer? _hardwareMonitor;
    private long _lastIdleTicks;
    private long _lastTotalTicks;
    private bool _hasSample;
    private int _tickCount;
    private double? _lastTemperature;

    public event Action<SystemUsage>? UsageUpdated;

    public SystemUsageMonitor()
    {
        try
        {
            _hardwareMonitor = new Computer { IsCpuEnabled = true };
            _hardwareMonitor.Open();
        }
        catch
        {
            // 管理者権限がない等でハードウェアセンサーにアクセスできない場合は温度取得を諦める
            _hardwareMonitor = null;
        }

        _timer = new System.Threading.Timer(_ => Sample(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    private void Sample()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return;

        var idleTicks = ToTicks(idle);
        var totalTicks = ToTicks(kernel) + ToTicks(user);

        double cpuPercent = 0;
        if (_hasSample)
        {
            var idleDelta = idleTicks - _lastIdleTicks;
            var totalDelta = totalTicks - _lastTotalTicks;
            cpuPercent = totalDelta > 0 ? Math.Clamp((totalDelta - idleDelta) / (double)totalDelta * 100.0, 0, 100) : 0;
        }
        _lastIdleTicks = idleTicks;
        _lastTotalTicks = totalTicks;
        _hasSample = true;

        var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        long usedMemory = 0, totalMemory = 0;
        if (GlobalMemoryStatusEx(ref memStatus))
        {
            totalMemory = (long)memStatus.ullTotalPhys;
            usedMemory = totalMemory - (long)memStatus.ullAvailPhys;
        }

        // WMIの温度取得は数十msかかることがあるため、毎秒ではなく3秒おきに行い間は前回値を使い回す
        if (_tickCount++ % 3 == 0)
            _lastTemperature = ReadCpuTemperatureCelsius();

        UsageUpdated?.Invoke(new SystemUsage(cpuPercent, usedMemory, totalMemory, _lastTemperature));
    }

    private double? ReadCpuTemperatureCelsius()
    {
        if (_hardwareMonitor is null) return null;

        try
        {
            double? package = null;
            double? coreAverage = null;
            var coreSum = 0.0;
            var coreCount = 0;

            foreach (var hardware in _hardwareMonitor.Hardware)
            {
                if (hardware.HardwareType != HardwareType.Cpu) continue;

                hardware.Update();
                foreach (var sensor in hardware.Sensors)
                {
                    if (sensor.SensorType != SensorType.Temperature || sensor.Value is not { } value) continue;

                    if (sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
                        package = value;
                    else if (sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                    {
                        coreSum += value;
                        coreCount++;
                    }
                }
            }

            if (coreCount > 0) coreAverage = coreSum / coreCount;
            return package ?? coreAverage;
        }
        catch
        {
            // センサーへのアクセスに失敗した場合は未取得扱いにする
            return null;
        }
    }

    private static long ToTicks(System.Runtime.InteropServices.ComTypes.FILETIME ft) =>
        ((long)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;

    public void Dispose()
    {
        _timer.Dispose();
        _hardwareMonitor?.Close();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(
        out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}
