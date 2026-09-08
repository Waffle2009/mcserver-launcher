using System.Windows;
using System.Windows.Threading;

namespace McServerLauncher.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ShowCrashDialog(args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ShowCrashDialog(args.Exception);
            args.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowCrashDialog(e.Exception);
        e.Handled = true;
    }

    private static void ShowCrashDialog(Exception? ex)
    {
        MessageBox.Show(
            $"予期しないエラーが発生しました。\n\n{ex}",
            "エラー",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
