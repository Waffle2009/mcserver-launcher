using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace McServerLauncher.App;

public sealed class BoolToStatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isRunning = value is true;
        return Application.Current.FindResource(isRunning ? "StatusRunningBrush" : "StatusIdleBrush");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
