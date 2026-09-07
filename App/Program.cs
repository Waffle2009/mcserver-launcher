using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace McServerLauncher.App;

public static class Program
{
    private const string UpdateRepoUrl = "https://github.com/Waffle2009/mcserver-launcher";

    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        _ = CheckForUpdatesAsync();

        var app = new App();
        app.InitializeComponent();
        app.Run();
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
