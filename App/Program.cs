using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using Velopack;
using Velopack.Sources;

namespace McServerLauncher.App;

public static class Program
{
    internal const string UpdateRepoUrl = "https://github.com/Waffle2009/mcserver-launcher";
    private const string RegisterDriverArg = "--register-hardware-driver";

    private static readonly string DriverRegisteredFlagPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "McServerLauncher", "driver-registered.flag");

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == RegisterDriverArg)
        {
            RegisterHardwareDriver();
            return;
        }

        VelopackApp.Build().Run();

        EnsureHardwareDriverRegistered();

        _ = CheckForUpdatesAsync();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    /// <summary>管理者権限で一度だけ起動され、LibreHardwareMonitorのカーネルドライバをサービス登録する。
    /// 登録後は通常権限でもセンサーにアクセスできるようになる。</summary>
    private static void RegisterHardwareDriver()
    {
        try
        {
            var computer = new Computer { IsCpuEnabled = true };
            computer.Open();
            computer.Close();
        }
        catch
        {
            // 登録に失敗しても致命的ではない(温度が「--」表示のままになるだけ)
        }
        finally
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DriverRegisteredFlagPath)!);
                File.WriteAllText(DriverRegisteredFlagPath, DateTime.UtcNow.ToString("o"));
            }
            catch
            {
                // フラグ書き込みに失敗した場合、次回起動時に再度登録を試みる
            }
        }
    }

    /// <summary>初回起動時のみUACプロンプトを出してドライバ登録を行う。以降はフラグにより通常起動する。</summary>
    private static void EnsureHardwareDriverRegistered()
    {
        if (File.Exists(DriverRegisteredFlagPath)) return;

        try
        {
            var exePath = Environment.ProcessPath;
            if (exePath is null) return;

            var psi = new ProcessStartInfo(exePath, RegisterDriverArg)
            {
                UseShellExecute = true,
                Verb = "runas",
            };
            using var process = Process.Start(psi);
            process?.WaitForExit();
        }
        catch
        {
            // UACキャンセル等で失敗した場合は通常権限のまま起動を継続する
        }
    }

    private static async Task CheckForUpdatesAsync()
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource(UpdateRepoUrl, null, false));
            if (!mgr.IsInstalled)
            {
                return;
            }

            var newVersion = await mgr.CheckForUpdatesAsync();
            if (newVersion == null)
            {
                return;
            }

            await mgr.DownloadUpdatesAsync(newVersion);
            mgr.ApplyUpdatesAndRestart(newVersion);
        }
        catch
        {
            // ネットワーク不通・GitHub未公開など更新確認に失敗しても起動は継続する
        }
    }
}
