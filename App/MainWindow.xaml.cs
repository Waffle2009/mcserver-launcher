using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using McServerLauncher.Core.ProcessManagement;
using McServerLauncher.Core.Servers;

namespace McServerLauncher.App;

public partial class MainWindow : Window
{
    private readonly ServerProcessManager _processManager = new();
    private string? _installedExecutablePath;

    public MainWindow()
    {
        InitializeComponent();

        _processManager.OutputReceived += line => Dispatcher.Invoke(() => AppendLog(line));
        _processManager.Exited += code => Dispatcher.Invoke(() =>
        {
            AppendLog($"--- サーバープロセスが終了しました (code={code}) ---");
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            SetRunningStatus(false);
        });

        UpdateDefaultInstallDir();
        UpdateEulaVisibility();
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

    private void AppendLog(string line)
    {
        LogBox.AppendText(line + Environment.NewLine);
        LogBox.ScrollToEnd();
    }

    private void SetRunningStatus(bool isRunning)
    {
        StatusText.Text = isRunning ? "起動中" : "停止中";
        StatusDot.Fill = (Brush)FindResource(isRunning ? "StatusRunningBrush" : "StatusIdleBrush");

        StatusDot.BeginAnimation(UIElement.OpacityProperty, null);
        if (isRunning)
        {
            var pulse = new DoubleAnimation(1.0, 0.35, TimeSpan.FromSeconds(0.9))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            StatusDot.BeginAnimation(UIElement.OpacityProperty, pulse);
        }
        else
        {
            StatusDot.Opacity = 1.0;
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

    private void ServerTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;

        VersionCombo.Items.Clear();
        UpdateDefaultInstallDir();
        UpdateEulaVisibility();
    }

    private async void RefreshVersionsButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshVersionsButton.IsEnabled = false;
        VersionCombo.Items.Clear();
        try
        {
            var provider = CreateProvider(SelectedServerType);
            AppendLog($"{SelectedServerType} のバージョン一覧を取得しています...");
            var versions = await provider.GetVersionsAsync();
            foreach (var v in versions)
                VersionCombo.Items.Add(v);
            if (VersionCombo.Items.Count > 0)
                VersionCombo.SelectedIndex = 0;
            AppendLog($"{versions.Count} 件のバージョンを取得しました。");
        }
        catch (Exception ex)
        {
            AppendLog($"エラー: バージョン一覧の取得に失敗しました - {ex.Message}");
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
            var progress = new Progress<string>(AppendLog);
            var targetDir = InstallDirBox.Text;

            var path = await provider.InstallAsync(version, targetDir, progress);

            if (type != ServerType.Bds)
                EulaHelper.Accept(targetDir);

            _installedExecutablePath = path;
            AppendLog($"インストール完了: {path}");
            StartButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            AppendLog($"エラー: インストールに失敗しました - {ex.Message}");
        }
        finally
        {
            InstallButton.IsEnabled = true;
        }
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_installedExecutablePath is null)
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

        try
        {
            AppendLog("サーバーを起動しています...");
            _processManager.Start(SelectedServerType, _installedExecutablePath, memoryMb);
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            SetRunningStatus(true);
        }
        catch (Exception ex)
        {
            AppendLog($"エラー: サーバーの起動に失敗しました - {ex.Message}");
        }
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _processManager.StopAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"エラー: サーバーの停止に失敗しました - {ex.Message}");
        }
    }

    private async void SendCommandButton_Click(object sender, RoutedEventArgs e) => await SendCommandAsync();

    private async void CommandBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await SendCommandAsync();
    }

    private async Task SendCommandAsync()
    {
        var command = CommandBox.Text;
        if (string.IsNullOrWhiteSpace(command) || !_processManager.IsRunning) return;

        AppendLog($"> {command}");
        CommandBox.Clear();
        try
        {
            await _processManager.SendCommandAsync(command);
        }
        catch (Exception ex)
        {
            AppendLog($"エラー: コマンド送信に失敗しました - {ex.Message}");
        }
    }
}
