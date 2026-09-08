using System.ComponentModel;
using System.Runtime.CompilerServices;
using McServerLauncher.Core.Servers;

namespace McServerLauncher.App;

/// <summary>Editable row for the 設定 tab's server.properties grid.</summary>
public sealed class PropertyRow : INotifyPropertyChanged
{
    private string _value;

    public string Key { get; }

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            OnPropertyChanged();
        }
    }

    public PropertyRow(string key, string value)
    {
        Key = key;
        _value = value;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Row for the ファイル tab's directory listing.</summary>
public sealed class FileRow
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    public string TypeText => IsDirectory ? "フォルダ" : "ファイル";
    public string SizeText { get; init; } = "";
}

/// <summary>Row for the 権限 tab, wrapping a <see cref="PermissionEntry"/>.</summary>
public sealed class PermissionRow
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Level { get; init; }
}

/// <summary>Row for the バックアップ tab, wrapping a <see cref="BackupInfo"/>.</summary>
public sealed class BackupRow
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required string CreatedAtText { get; init; }
    public required string SizeText { get; init; }
}

/// <summary>Row for the アドオン tab, wrapping an <see cref="AddonEntry"/>.</summary>
public sealed class AddonRow
{
    public required AddonEntry Entry { get; init; }
    public string Name => Entry.Name;
    public string StatusText => Entry.IsFolder ? "有効" : (Entry.IsEnabled ? "有効" : "無効");
}

/// <summary>Row for the アクセスログ tab, wrapping an <see cref="AccessLogEntry"/>.</summary>
public sealed class AccessLogRow
{
    public required string TimeText { get; init; }
    public required string PlayerName { get; init; }
    public required string EventText { get; init; }
}
