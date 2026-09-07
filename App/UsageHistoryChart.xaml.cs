using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using McServerLauncher.Core.ProcessManagement;

namespace McServerLauncher.App;

/// <summary>Draws CPU usage history as a line chart with selectable time-range tabs.</summary>
public partial class UsageHistoryChart : UserControl
{
    private readonly DispatcherTimer _refreshTimer;
    private Func<TimeSpan, IReadOnlyList<UsageSample>>? _provider;
    private double _hours = 1;
    private Button[] Tabs => new[] { Tab1h, Tab6h, Tab24h, Tab7d, Tab30d };

    public UsageHistoryChart()
    {
        InitializeComponent();
        SetActiveTab(Tab1h);
        SizeChanged += (_, _) => Redraw();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refreshTimer.Tick += (_, _) => Redraw();
        _refreshTimer.Start();
        Unloaded += (_, _) => _refreshTimer.Stop();
    }

    public void SetProvider(Func<TimeSpan, IReadOnlyList<UsageSample>> provider)
    {
        _provider = provider;
        Redraw();
    }

    private void PeriodTab_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        _hours = double.Parse((string)button.Tag);
        SetActiveTab(button);
        Redraw();
    }

    private void SetActiveTab(Button active)
    {
        foreach (var tab in Tabs)
            tab.Background = tab == active
                ? (Brush)FindResource("AccentBrush")
                : (Brush)FindResource("NeutralButtonBrush");
    }

    private void Redraw()
    {
        ChartCanvas.Children.Clear();

        var width = ChartArea.ActualWidth;
        var height = ChartArea.ActualHeight;
        if (_provider is null || width < 10 || height < 10) return;

        var samples = _provider(TimeSpan.FromHours(_hours));
        NoDataText.Visibility = samples.Count < 2 ? Visibility.Visible : Visibility.Collapsed;
        if (samples.Count < 2) return;

        foreach (var pct in new double[] { 0, 50, 100 })
        {
            var y = height - pct / 100.0 * height;
            ChartCanvas.Children.Add(new Line
            {
                X1 = 0, Y1 = y, X2 = width, Y2 = y,
                Stroke = (Brush)FindResource("BorderBrush"), StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 3 }
            });
        }

        var minTime = samples[0].UnixSeconds;
        var maxTime = Math.Max(samples[^1].UnixSeconds, minTime + 1);
        var timeSpan = (double)(maxTime - minTime);

        var linePoints = new PointCollection();
        foreach (var sample in samples)
        {
            var x = (sample.UnixSeconds - minTime) / timeSpan * width;
            var y = height - Math.Clamp(sample.CpuPercent, 0, 100) / 100.0 * height;
            linePoints.Add(new Point(x, y));
        }

        var fillPoints = new PointCollection(linePoints) { new Point(width, height), new Point(0, height) };
        ChartCanvas.Children.Add(new Polygon
        {
            Points = fillPoints,
            Fill = (Brush)FindResource("AccentSoftBrush")
        });

        ChartCanvas.Children.Add(new Polyline
        {
            Points = linePoints,
            Stroke = (Brush)FindResource("AccentBrush"),
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round
        });
    }
}
