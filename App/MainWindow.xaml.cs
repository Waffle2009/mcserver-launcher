using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using McServerLauncher.Core.Instances;
using McServerLauncher.Core.ProcessManagement;
using McServerLauncher.Core.Servers;
using Velopack;
using Velopack.Sources;

namespace McServerLauncher.App;

public partial class MainWindow : Window
{
    private readonly ServerInstanceStore _store = new();
    private readonly ObservableCollection<ServerSession> _sessions = new();
    private readonly SystemUsageMonitor _systemUsageMonitor = new();
    private readonly UsageHistoryStore _usageHistory = new();
    private readonly DispatcherTimer _headerTimer;
    private ServerSession? _selected;
    private bool _isLoadingSession;
    private long _lastTotalMemoryBytes;
    private string? _currentFileDir;

    public MainWindow()
    {
        InitializeComponent();

        _headerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _headerTimer.Tick += (_, _) => RefreshHeaderStats();
        _headerTimer.Start();

        var groupedView = CollectionViewSource.GetDefaultView(_sessions);
        groupedView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ServerSession.TypeText)));
        ServerListBox.ItemsSource = groupedView;

        AppVersionText.Text = GetAppVersionText();

        SystemCpuGauge.Title = "全体CPU使用率";
        SystemMemGauge.Title = "全体メモリ使用率";
        RunningCountGauge.Title = "BDS合計CPU使用率";
        TotalServerCpuGauge.Title = "BDS合計メモリ使用率";
        CpuHistoryChart.SetProvider(_usageHistory.GetSamples);

        _systemUsageMonitor.UsageUpdated += usage => Dispatcher.Invoke(() => OnSystemUsageUpdated(usage));

        foreach (var instance in _store.Load())
            AddSession(instance, select: false);

        ShowOverviewPage();
        UpdateAggregateGauges();
    }

    private static string GetAppVersionText()
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource(Program.UpdateRepoUrl, null, false));
            if (mgr.IsInstalled)
                return $"v{mgr.CurrentVersion}";
        }
        catch
        {
            // インストール情報が読めない場合は開発版扱いにする
        }
        return "開発版";
    }

    private void OnSystemUsageUpdated(SystemUsage usage)
    {
        var usedGb = usage.UsedMemoryBytes / 1024.0 / 1024.0 / 1024.0;
        var totalGb = usage.TotalMemoryBytes / 1024.0 / 1024.0 / 1024.0;

        var memPercent = usage.TotalMemoryBytes > 0 ? usage.UsedMemoryBytes * 100.0 / usage.TotalMemoryBytes : 0;
        SystemCpuGauge.SetValue(usage.CpuPercent, $"{usage.CpuPercent:0.0}%");
        SystemMemGauge.SetValue(memPercent, $"{usedGb:0.0}/{totalGb:0.0}GB");
        _usageHistory.AddSample(usage.CpuPercent, memPercent);

        _lastTotalMemoryBytes = usage.TotalMemoryBytes;
        UpdateAggregateGauges();
    }

    private void UpdateAggregateGauges()
    {
        var bdsCpu = _sessions.Where(s => s.IsRunning && s.Instance.Type == ServerType.Bds).Sum(s => s.CpuPercent);
        RunningCountGauge.SetValue(Math.Min(bdsCpu, 100), $"{bdsCpu:0}%");

        var bdsMemory = _sessions.Where(s => s.IsRunning && s.Instance.Type == ServerType.Bds).Sum(s => s.MemoryBytes);
        var bdsMemGb = bdsMemory / 1024.0 / 1024.0 / 1024.0;
        var bdsMemPercent = _lastTotalMemoryBytes > 0 ? bdsMemory * 100.0 / _lastTotalMemoryBytes : 0;
        TotalServerCpuGauge.SetValue(bdsMemPercent, $"{bdsMemGb:0.0}GB");
    }

    private void ShowOverviewPage()
    {
        OverviewNavItem.Background = (Brush)FindResource("CardHoverBrush");
        OverviewNavItem.BorderBrush = (Brush)FindResource("AccentBrush");
        ContentScroller.Visibility = Visibility.Collapsed;
        OverviewScroller.Visibility = Visibility.Visible;
    }

    private void OverviewNavItem_Click(object sender, MouseButtonEventArgs e)
    {
        PersistUiIntoSelected();
        SaveInstances();
        ServerListBox.SelectedItem = null;
        ShowOverviewPage();
    }

    private ServerType SelectedServerType =>
        Enum.Parse<ServerType>((string)((ComboBoxItem)ServerTypeCombo.SelectedItem).Tag);

    private static IServerProvider CreateProvider(ServerType type) => type switch
    {
        ServerType.Paper => new PaperProvider(),
        ServerType.Spigot => new SpigotProvider(),
        ServerType.Bds => new BdsProvider(),
        _ => throw new NotSupportedException($"未対応のサーバー種別です: {type}")
    };

    private ServerSession AddSession(ServerInstance instance, bool select = true)
    {
        var session = new ServerSession(instance);
        session.ProcessManager.OutputReceived += line => Dispatcher.Invoke(() => OnSessionOutput(session, line));
        session.ProcessManager.Exited += code => Dispatcher.Invoke(() => OnSessionExited(session, code));
        session.ProcessManager.ResourceUsageUpdated += usage => Dispatcher.Invoke(() => OnSessionResourceUsage(session, usage));

        _sessions.Add(session);
        if (select) ServerListBox.SelectedItem = session;
        UpdateAggregateGauges();
        return session;
    }

    private void OnSessionOutput(ServerSession session, string line)
    {
        session.LogBuffer += line + Environment.NewLine;
        if (session == _selected)
        {
            LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToEnd();
        }

        var entry = AccessLogParser.TryParse(line);
        if (entry is not null)
        {
            session.AccessLog.Add(new AccessLogRow
            {
                TimeText = entry.Time.ToString("HH:mm:ss"),
                PlayerName = entry.PlayerName,
                EventText = entry.Joined ? "参加" : "退出"
            });
        }
    }

    private void OnSessionExited(ServerSession session, int code)
    {
        session.IsRunning = false;
        OnSessionOutput(session, $"--- サーバープロセスが終了しました (code={code}) ---");
        if (session == _selected)
        {
            StartButton.IsEnabled = session.Instance.ExecutablePath is not null;
            StopButton.IsEnabled = false;
        }
        UpdateAggregateGauges();
    }

    private void OnSessionResourceUsage(ServerSession session, ResourceUsage usage)
    {
        session.CpuPercent = usage.CpuPercent;
        session.MemoryBytes = usage.MemoryBytes;
        UpdateAggregateGauges();
    }

    private void SaveInstances() => _store.Save(_sessions.Select(s => s.Instance));

    private void PersistUiIntoSelected()
    {
        if (_selected is null) return;
        _selected.Instance.InstallDir = InstallDirBox.Text;
        if (int.TryParse(MemoryBox.Text, out var mb) && mb > 0)
            _selected.Instance.MemoryMb = mb;
    }

    private void ServerListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Overviewページへ切り替える際にSelectedItemをnullにするので、その場合は何もしない
        if (ServerListBox.SelectedItem is not ServerSession session) return;

        PersistUiIntoSelected();
        _selected = session;
        LoadSelectedIntoUi();
    }

    private void LoadSelectedIntoUi()
    {
        if (_selected is null) return;

        OverviewNavItem.Background = Brushes.Transparent;
        OverviewNavItem.BorderBrush = Brushes.Transparent;
        OverviewScroller.Visibility = Visibility.Collapsed;
        ContentScroller.Visibility = Visibility.Visible;
        ContentPanel.DataContext = _selected;

        _isLoadingSession = true;
        try
        {
            var instance = _selected.Instance;

            SelectServerTypeCombo(instance.Type);
            InstallDirBox.Text = instance.InstallDir;
            MemoryBox.Text = instance.MemoryMb.ToString();
            UpdateEulaVisibility();

            VersionCombo.Items.Clear();
            if (!string.IsNullOrEmpty(instance.Version))
            {
                VersionCombo.Items.Add(instance.Version);
                VersionCombo.SelectedIndex = 0;
            }

            LogBox.Text = _selected.LogBuffer;
            LogBox.ScrollToEnd();

            _selected.IsRunning = _selected.ProcessManager.IsRunning;
            var hasExecutable = instance.ExecutablePath is not null;
            StartButton.IsEnabled = hasExecutable && !_selected.IsRunning;
            StopButton.IsEnabled = _selected.IsRunning;

            SetupCard.Visibility = hasExecutable ? Visibility.Collapsed : Visibility.Visible;
            ServerTabs.Visibility = hasExecutable ? Visibility.Visible : Visibility.Collapsed;

            _currentFileDir = instance.InstallDir;
            AccessLogListView.ItemsSource = _selected.AccessLog;
            LoadFileList();
            LoadPropertiesList();
            LoadPermissionsList();
            LoadBackupsList();
            LoadAddonsList();
            RefreshHeaderStats();
        }
        finally
        {
            _isLoadingSession = false;
        }
    }

    private void RefreshHeaderStats()
    {
        if (_selected is null) return;
        var instance = _selected.Instance;
        var pm = _selected.ProcessManager;

        HeaderPidText.Text = pm.ProcessId?.ToString() ?? "--";
        HeaderUptimeText.Text = pm.StartedAtUtc is { } startedAt
            ? (DateTime.UtcNow - startedAt).ToString(@"hh\:mm\:ss")
            : "--";
        HeaderVersionText.Text = string.IsNullOrEmpty(instance.Version) ? "--" : instance.Version;

        var port = Directory.Exists(instance.InstallDir)
            ? ServerPropertiesFile.GetValue(instance.InstallDir, "server-port")
            : null;
        HeaderPortText.Text = port ?? "--";
    }

    private void SelectServerTypeCombo(ServerType type)
    {
        foreach (ComboBoxItem item in ServerTypeCombo.Items)
        {
            if ((string)item.Tag == type.ToString())
            {
                ServerTypeCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void UpdateDefaultInstallDir()
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "McServerLauncher", "servers", SelectedServerType.ToString());
        InstallDirBox.Text = baseDir;
    }

    private void UpdateEulaVisibility()
    {
        var isJava = SelectedServerType != ServerType.Bds;
        EulaCheckBox.Visibility = isJava ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddServerButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddServerDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var instance = new ServerInstance
        {
            Name = dialog.ServerName,
            Type = dialog.SelectedType,
            LevelType = dialog.SelectedLevelType
        };
        AddSession(instance);
        SaveInstances();
    }

    private void ServerTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _isLoadingSession || _selected is null) return;

        _selected.Instance.Type = SelectedServerType;
        _selected.Instance.ExecutablePath = null;
        VersionCombo.Items.Clear();
        UpdateDefaultInstallDir();
        UpdateEulaVisibility();
        StartButton.IsEnabled = false;
        SaveInstances();
    }

    private async void RefreshVersionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var session = _selected;

        RefreshVersionsButton.IsEnabled = false;
        VersionCombo.Items.Clear();
        try
        {
            var provider = CreateProvider(SelectedServerType);
            OnSessionOutput(session, $"{SelectedServerType} のバージョン一覧を取得しています...");
            var versions = await provider.GetVersionsAsync();
            foreach (var v in versions)
                VersionCombo.Items.Add(v);
            if (VersionCombo.Items.Count > 0)
                VersionCombo.SelectedIndex = 0;
            OnSessionOutput(session, $"{versions.Count} 件のバージョンを取得しました。");
        }
        catch (Exception ex)
        {
            OnSessionOutput(session, $"エラー: バージョン一覧の取得に失敗しました - {ex.Message}");
        }
        finally
        {
            RefreshVersionsButton.IsEnabled = true;
        }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            InitialDirectory = Directory.Exists(InstallDirBox.Text) ? InstallDirBox.Text : null
        };
        if (dialog.ShowDialog() == true)
            InstallDirBox.Text = dialog.FolderName;
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var session = _selected;

        if (VersionCombo.SelectedItem is not string version)
        {
            MessageBox.Show("先にバージョン一覧を取得し、バージョンを選択してください。", "確認",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var type = SelectedServerType;
        if (type != ServerType.Bds && EulaCheckBox.IsChecked != true)
        {
            MessageBox.Show("Minecraft使用許諾契約(EULA)への同意が必要です。", "確認",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        InstallButton.IsEnabled = false;
        try
        {
            var provider = CreateProvider(type);
            var progress = new Progress<string>(line => OnSessionOutput(session, line));
            var targetDir = InstallDirBox.Text;

            var path = await provider.InstallAsync(version, targetDir, progress);

            if (type != ServerType.Bds)
                EulaHelper.Accept(targetDir);

            if (!string.IsNullOrEmpty(session.Instance.LevelType))
                ServerPropertiesFile.Save(targetDir, new[]
                {
                    new KeyValuePair<string, string>("level-type", session.Instance.LevelType)
                });

            session.Instance.Type = type;
            session.Instance.InstallDir = targetDir;
            session.Instance.Version = version;
            session.Instance.ExecutablePath = path;
            SaveInstances();

            OnSessionOutput(session, $"インストール完了: {path}");
            if (session == _selected)
                StartButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            OnSessionOutput(session, $"エラー: インストールに失敗しました - {ex.Message}");
        }
        finally
        {
            InstallButton.IsEnabled = true;
        }
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var session = _selected;

        if (session.Instance.ExecutablePath is null)
        {
            MessageBox.Show("先にサーバーを取得(ダウンロード)してください。", "確認",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(MemoryBox.Text, out var memoryMb) || memoryMb <= 0)
        {
            MessageBox.Show("メモリ(MB)には正の整数を入力してください。", "確認",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        session.Instance.MemoryMb = memoryMb;
        SaveInstances();
        StartSession(session);
    }

    private void StartSession(ServerSession session)
    {
        if (session.Instance.ExecutablePath is null) return;

        try
        {
            OnSessionOutput(session, "サーバーを起動しています...");
            session.ProcessManager.Start(session.Instance.Type, session.Instance.ExecutablePath, session.Instance.MemoryMb);
            session.IsRunning = true;
            if (session == _selected)
            {
                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
            }
            UpdateAggregateGauges();
        }
        catch (Exception ex)
        {
            OnSessionOutput(session, $"エラー: サーバーの起動に失敗しました - {ex.Message}");
        }
    }

    private async Task StopSessionAsync(ServerSession session)
    {
        try
        {
            await session.ProcessManager.StopAsync();
        }
        catch (Exception ex)
        {
            OnSessionOutput(session, $"エラー: サーバーの停止に失敗しました - {ex.Message}");
        }
    }

    private void StartAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var session in _sessions.Where(s => !s.IsRunning && s.Instance.ExecutablePath is not null).ToList())
            StartSession(session);
    }

    private async void StopAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var session in _sessions.Where(s => s.IsRunning).ToList())
            await StopSessionAsync(session);
    }

    private async void RestartAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var session in _sessions.Where(s => s.IsRunning).ToList())
        {
            await StopSessionAsync(session);
            StartSession(session);
        }
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var session = _selected;
        await StopSessionAsync(session);
    }

    private async void SendCommandButton_Click(object sender, RoutedEventArgs e) => await SendCommandAsync();

    private async void CommandBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await SendCommandAsync();
    }

    private async Task SendCommandAsync()
    {
        if (_selected is null) return;
        var session = _selected;

        var command = CommandBox.Text;
        if (string.IsNullOrWhiteSpace(command) || !session.ProcessManager.IsRunning) return;

        OnSessionOutput(session, $"> {command}");
        CommandBox.Clear();
        try
        {
            await session.ProcessManager.SendCommandAsync(command);
        }
        catch (Exception ex)
        {
            OnSessionOutput(session, $"エラー: コマンド送信に失敗しました - {ex.Message}");
        }
    }

    private async void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var session = _selected;
        if (session.IsRunning)
            await StopSessionAsync(session);
        StartSession(session);
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var session = _selected;

        if (session.Instance.ExecutablePath is null)
        {
            MessageBox.Show("先にサーバーを取得(ダウンロード)してください。", "確認",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show("最新バージョンを再取得してサーバーを更新しますか?", "確認",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var wasRunning = session.IsRunning;
        if (wasRunning) await StopSessionAsync(session);

        UpdateButton.IsEnabled = false;
        try
        {
            var provider = CreateProvider(session.Instance.Type);
            var progress = new Progress<string>(line => OnSessionOutput(session, line));
            OnSessionOutput(session, "最新バージョンを確認しています...");
            var versions = await provider.GetVersionsAsync();
            var latest = versions.FirstOrDefault();
            if (latest is null)
                throw new InvalidOperationException("バージョン一覧を取得できませんでした。");

            var path = await provider.InstallAsync(latest, session.Instance.InstallDir, progress);
            session.Instance.Version = latest;
            session.Instance.ExecutablePath = path;
            SaveInstances();
            OnSessionOutput(session, $"更新完了: {latest}");

            if (session == _selected)
                RefreshHeaderStats();
        }
        catch (Exception ex)
        {
            OnSessionOutput(session, $"エラー: 更新に失敗しました - {ex.Message}");
        }
        finally
        {
            UpdateButton.IsEnabled = true;
            if (wasRunning) StartSession(session);
        }
    }

    private async void DeleteServerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var session = _selected;

        if (MessageBox.Show($"「{session.Name}」を削除しますか?\nこの操作は取り消せません。", "確認",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (session.IsRunning)
            await StopSessionAsync(session);

        var deleteFiles = MessageBox.Show("サーバーのファイルも削除しますか?", "確認",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        if (deleteFiles && Directory.Exists(session.Instance.InstallDir))
        {
            try { Directory.Delete(session.Instance.InstallDir, recursive: true); }
            catch (Exception ex)
            {
                MessageBox.Show($"ファイルの削除に失敗しました: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        _sessions.Remove(session);
        session.ProcessManager.Dispose();
        SaveInstances();

        _selected = null;
        ServerListBox.SelectedItem = null;
        ShowOverviewPage();
        UpdateAggregateGauges();
    }

    private void ServerTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selected is null || !ReferenceEquals(e.OriginalSource, ServerTabs)) return;

        switch (ServerTabs.SelectedIndex)
        {
            case 1: LoadFileList(); break;
            case 2: LoadPropertiesList(); break;
            case 3: LoadPermissionsList(); break;
            case 4: LoadBackupsList(); break;
            case 5: LoadAddonsList(); break;
        }
    }

    // ----- ファイル タブ -----

    private void LoadFileList()
    {
        if (_selected is null || _currentFileDir is null) return;

        FilePathText.Text = _currentFileDir;
        if (!Directory.Exists(_currentFileDir))
        {
            FileListView.ItemsSource = Array.Empty<FileRow>();
            return;
        }

        try
        {
            var rows = new List<FileRow>();
            foreach (var dir in Directory.GetDirectories(_currentFileDir).OrderBy(Path.GetFileName))
                rows.Add(new FileRow { Name = Path.GetFileName(dir), FullPath = dir, IsDirectory = true });
            foreach (var file in Directory.GetFiles(_currentFileDir).OrderBy(Path.GetFileName))
                rows.Add(new FileRow
                {
                    Name = Path.GetFileName(file),
                    FullPath = file,
                    IsDirectory = false,
                    SizeText = FormatSize(new FileInfo(file).Length)
                });

            FileListView.ItemsSource = rows;
        }
        catch (Exception ex)
        {
            FileListView.ItemsSource = Array.Empty<FileRow>();
            OnSessionOutput(_selected, $"エラー: ファイル一覧の読み込みに失敗しました - {ex.Message}");
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:0.0} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:0.0} GB";
    }

    private void FileUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _currentFileDir is null) return;
        var installRoot = Path.GetFullPath(_selected.Instance.InstallDir);
        var current = Path.GetFullPath(_currentFileDir);
        if (string.Equals(current, installRoot, StringComparison.OrdinalIgnoreCase)) return;

        var parent = Path.GetDirectoryName(current);
        if (parent is null) return;
        _currentFileDir = parent;
        LoadFileList();
    }

    private void FileListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileListView.SelectedItem is not FileRow row || !row.IsDirectory) return;
        _currentFileDir = row.FullPath;
        LoadFileList();
    }

    private void OpenInExplorerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFileDir is null || !Directory.Exists(_currentFileDir)) return;
        Process.Start(new ProcessStartInfo { FileName = _currentFileDir, UseShellExecute = true });
    }

    private void FileDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is not FileRow row) return;
        if (MessageBox.Show($"「{row.Name}」を削除しますか?", "確認",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            if (row.IsDirectory) Directory.Delete(row.FullPath, recursive: true);
            else File.Delete(row.FullPath);
            LoadFileList();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"削除に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ----- 設定 タブ -----

    private void LoadPropertiesList()
    {
        if (_selected is null) return;
        try
        {
            var rows = ServerPropertiesFile.Load(_selected.Instance.InstallDir)
                .Select(kv => new PropertyRow(kv.Key, kv.Value))
                .ToList();
            PropertiesListView.ItemsSource = rows;
        }
        catch (Exception ex)
        {
            PropertiesListView.ItemsSource = Array.Empty<PropertyRow>();
            OnSessionOutput(_selected, $"エラー: 設定の読み込みに失敗しました - {ex.Message}");
        }
    }

    private void ReloadPropertiesButton_Click(object sender, RoutedEventArgs e) => LoadPropertiesList();

    private void SavePropertiesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var rows = PropertiesListView.ItemsSource?.Cast<PropertyRow>().ToList() ?? new List<PropertyRow>();
        try
        {
            ServerPropertiesFile.Save(_selected.Instance.InstallDir,
                rows.Select(r => new KeyValuePair<string, string>(r.Key, r.Value)).ToList());
            RefreshHeaderStats();
            MessageBox.Show("保存しました。反映にはサーバーの再起動が必要です。", "設定",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ----- 権限 タブ -----

    private void LoadPermissionsList()
    {
        if (_selected is null) return;
        try
        {
            var rows = PermissionsManager.Load(_selected.Instance)
                .Select(p => new PermissionRow { Id = p.Id, Name = p.Name, Level = p.Level })
                .ToList();
            PermissionsListView.ItemsSource = rows;
        }
        catch (Exception ex)
        {
            OnSessionOutput(_selected, $"エラー: 権限一覧の読み込みに失敗しました - {ex.Message}");
        }
    }

    private async void AddPermissionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var name = PermissionNameBox.Text.Trim();
        if (name.Length == 0) return;

        AddPermissionButton.IsEnabled = false;
        try
        {
            await PermissionsManager.AddAsync(_selected.Instance, name);
            PermissionNameBox.Clear();
            LoadPermissionsList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            AddPermissionButton.IsEnabled = true;
        }
    }

    private void RemovePermissionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || PermissionsListView.SelectedItem is not PermissionRow row) return;
        try
        {
            PermissionsManager.Remove(_selected.Instance, row.Id);
            LoadPermissionsList();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"削除に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ----- バックアップ タブ -----

    private void LoadBackupsList()
    {
        if (_selected is null) return;
        try
        {
            var rows = BackupManager.List(_selected.Instance)
                .Select(b => new BackupRow
                {
                    FilePath = b.FilePath,
                    FileName = b.FileName,
                    CreatedAtText = b.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                    SizeText = FormatSize(b.SizeBytes)
                })
                .ToList();
            BackupsListView.ItemsSource = rows;
        }
        catch (Exception ex)
        {
            BackupsListView.ItemsSource = Array.Empty<BackupRow>();
            OnSessionOutput(_selected, $"エラー: バックアップ一覧の読み込みに失敗しました - {ex.Message}");
        }
    }

    private void CreateBackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        try
        {
            BackupManager.Create(_selected.Instance);
            LoadBackupsList();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"バックアップの作成に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RestoreBackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || BackupsListView.SelectedItem is not BackupRow row) return;
        var session = _selected;

        if (MessageBox.Show($"「{row.FileName}」を復元しますか?\n現在のサーバーファイルは上書きされます。", "確認",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (session.IsRunning)
            await StopSessionAsync(session);

        try
        {
            BackupManager.Restore(session.Instance, row.FilePath);
            MessageBox.Show("復元しました。", "バックアップ", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"復元に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteBackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (BackupsListView.SelectedItem is not BackupRow row) return;
        if (MessageBox.Show($"「{row.FileName}」を削除しますか?", "確認",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            BackupManager.Delete(row.FilePath);
            LoadBackupsList();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"削除に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ----- アドオン タブ -----

    private void LoadAddonsList()
    {
        if (_selected is null) return;

        var isBds = _selected.Instance.Type == ServerType.Bds;
        AddAddonButton.Content = isBds ? "パックフォルダを追加..." : "プラグインを追加...";
        ToggleAddonButton.Visibility = isBds ? Visibility.Collapsed : Visibility.Visible;

        try
        {
            var rows = AddonsManager.List(_selected.Instance)
                .Select(a => new AddonRow { Entry = a })
                .ToList();
            AddonsListView.ItemsSource = rows;
        }
        catch (Exception ex)
        {
            AddonsListView.ItemsSource = Array.Empty<AddonRow>();
            OnSessionOutput(_selected, $"エラー: アドオン一覧の読み込みに失敗しました - {ex.Message}");
        }
    }

    private void AddAddonButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var instance = _selected.Instance;

        try
        {
            if (instance.Type == ServerType.Bds)
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "追加するビヘイビア/リソースパックのフォルダを選択" };
                if (dialog.ShowDialog() != true) return;

                var isBehavior = MessageBox.Show("ビヘイビアパックとして追加しますか?\n「いいえ」でリソースパックとして追加します。",
                    "パックの種類", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
                AddonsManager.AddPackFolder(instance, dialog.FolderName, isBehavior);
            }
            else
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Title = "追加するプラグインjarを選択", Filter = "Plugin jar (*.jar)|*.jar" };
                if (dialog.ShowDialog() != true) return;
                AddonsManager.AddPluginFile(instance, dialog.FileName);
            }
            LoadAddonsList();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"追加に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ToggleAddonButton_Click(object sender, RoutedEventArgs e)
    {
        if (AddonsListView.SelectedItem is not AddonRow row) return;
        try
        {
            AddonsManager.Toggle(row.Entry);
            LoadAddonsList();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"切り替えに失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteAddonButton_Click(object sender, RoutedEventArgs e)
    {
        if (AddonsListView.SelectedItem is not AddonRow row) return;
        if (MessageBox.Show($"「{row.Name}」を削除しますか?", "確認",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            AddonsManager.Delete(row.Entry);
            LoadAddonsList();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"削除に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ----- アクセスログ タブ -----

    private void ClearAccessLogButton_Click(object sender, RoutedEventArgs e) => _selected?.AccessLog.Clear();

    protected override void OnClosing(CancelEventArgs e)
    {
        PersistUiIntoSelected();
        SaveInstances();
        _headerTimer.Stop();
        _systemUsageMonitor.Dispose();
        base.OnClosing(e);
    }
}
