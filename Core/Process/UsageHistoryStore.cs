using System.Text.Json;

namespace McServerLauncher.Core.ProcessManagement;

public readonly record struct UsageSample(long UnixSeconds, double CpuPercent, double MemPercent);

/// <summary>Aggregates system usage samples into per-minute averages and persists up to 30 days of history under LocalAppData.</summary>
public sealed class UsageHistoryStore
{
    private const int RetentionDays = 30;

    private readonly string _filePath;
    private readonly List<UsageSample> _samples = new();
    private readonly object _lock = new();

    private long _currentMinute = -1;
    private double _cpuAccum;
    private double _memAccum;
    private int _accumCount;
    private int _commitsSinceFlush;

    public UsageHistoryStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "McServerLauncher", "usage_history.jsonl");
        Load();
    }

    private void Load()
    {
        if (!File.Exists(_filePath)) return;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-RetentionDays).ToUnixTimeSeconds();
        foreach (var line in File.ReadLines(_filePath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var sample = JsonSerializer.Deserialize<UsageSample>(line);
                if (sample.UnixSeconds >= cutoff) _samples.Add(sample);
            }
            catch (JsonException)
            {
                // 壊れた行は無視する
            }
        }
    }

    /// <summary>Feeds one raw (sub-minute) sample; samples are averaged and committed once per minute.</summary>
    public void AddSample(double cpuPercent, double memPercent)
    {
        var now = DateTimeOffset.UtcNow;
        var minute = now.ToUnixTimeSeconds() / 60;

        lock (_lock)
        {
            if (_currentMinute == -1) _currentMinute = minute;
            if (minute != _currentMinute)
            {
                CommitBucket();
                _currentMinute = minute;
            }
            _cpuAccum += cpuPercent;
            _memAccum += memPercent;
            _accumCount++;
        }
    }

    private void CommitBucket()
    {
        if (_accumCount == 0) return;

        var sample = new UsageSample(_currentMinute * 60, _cpuAccum / _accumCount, _memAccum / _accumCount);
        _samples.Add(sample);
        _cpuAccum = 0;
        _memAccum = 0;
        _accumCount = 0;

        var cutoff = DateTimeOffset.UtcNow.AddDays(-RetentionDays).ToUnixTimeSeconds();
        _samples.RemoveAll(s => s.UnixSeconds < cutoff);

        try
        {
            _commitsSinceFlush++;
            if (_commitsSinceFlush >= 60)
            {
                // 古いサンプルを間引いた状態でファイル全体を1時間おきに書き直す
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllLines(_filePath, _samples.Select(s => JsonSerializer.Serialize(s)));
                _commitsSinceFlush = 0;
            }
            else
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(_filePath, JsonSerializer.Serialize(sample) + Environment.NewLine);
            }
        }
        catch (IOException)
        {
            // 書き込み失敗はメモリ上の履歴には影響させない
        }
    }

    /// <summary>Returns committed samples within the given trailing window, plus the current in-progress bucket.</summary>
    public IReadOnlyList<UsageSample> GetSamples(TimeSpan window)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(window).ToUnixTimeSeconds();
        lock (_lock)
        {
            var result = _samples.Where(s => s.UnixSeconds >= cutoff).ToList();
            if (_accumCount > 0)
                result.Add(new UsageSample(DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    _cpuAccum / _accumCount, _memAccum / _accumCount));
            return result;
        }
    }
}
