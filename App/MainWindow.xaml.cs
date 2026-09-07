using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using McServerLauncher.Core.Instances;
using McServerLauncher.Core.ProcessManagement;
using McServerLauncher.Core.Servers;

namespace McServerLauncher.App;

public partial class MainWindow : Window
{
    private readonly ServerInstanceStore _store = new();
    private readonly ObservableCollection<ServerSession> _sessions = new();
    private ServerSession? _selected;
    private bool _isLoadingSession;

    public MainWindow()
    {
        InitializeComponent();
        ServerListBox.ItemsSource = _sessions;

        foreach (var instance in _store.Load())
            AddSession(instance, select: false);

        if (_sessions.Count > 0)
            ServerListBox.SelectedIndex = 0;
        else
            LoadSelectedIntoUi();
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
    }

    private static void OnSessionResourceUsage(ServerSession session, ResourceUsage usage)
    {
        session.CpuPercent = usage.CpuPercent;
        session.MemoryBytes = usage.MemoryBytes;
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
        PersistUiIntoSelected();
        _selected = ServerListBox.SelectedItem as ServerSession;
        LoadSelectedIntoUi();
    }

    private void LoadSelectedIntoUi()
    {
        if (_selected is null)
        {
            ContentScroller.Visibility = Visibility.Collapsed;
            EmptyStatePanel.Visibility = Visibility.Visible;
            return;
        }

        EmptyStatePanel.Visibility = Visibility.Collapsed;
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
        }
        finally
        {
            _isLoadingSession = false;
        }
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

        var instance = new ServerInstance { Name = dialog.ServerName };
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

        try
        {
            OnSessionOutput(session, "サーバーを起動しています...");
            session.ProcessManager.Start(session.Instance.Type, session.Instance.ExecutablePath, memoryMb);
            session.IsRunning = true;
            if (session == _selected)
            {
                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            OnSessionOutput(session, $"エラー: サーバーの起動に失敗しました - {ex.Message}");
        }
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var session = _selected;
        try
        {
            await session.ProcessManager.StopAsync();
        }
        catch (Exception ex)
        {
            OnSessionOutput(session, $"エラー: サーバーの停止に失敗しました - {ex.Message}");
        }
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

    protected override void OnClosing(CancelEventArgs e)
    {
        PersistUiIntoSelected();
        SaveInstances();
        base.OnClosing(e);
    }
}
