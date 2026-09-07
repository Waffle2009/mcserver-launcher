using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace McServerLauncher.App;

/// <summary>A dashboard tile showing a circular percentage gauge plus a thin bar underneath.</summary>
public partial class GaugeCard : UserControl
{
    private static readonly Point Center = new(32, 32);
    private const double Radius = 29;

    public GaugeCard()
    {
        InitializeComponent();
    }

    public string Title
    {
        set => TitleText.Text = value;
    }

    public Brush RingBrush
    {
        set
        {
            ArcPath.Stroke = value;
            BarFill.Background = value;
        }
    }

    public void SetValue(double percent, string valueText)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        ValueText.Text = valueText;
        PercentText.Text = $"{clamped:0}%";
        ArcPath.Data = BuildArcGeometry(clamped);
        BarFillColumn.Width = new GridLength(clamped, GridUnitType.Star);
        BarEmptyColumn.Width = new GridLength(100 - clamped, GridUnitType.Star);
    }

    private static Geometry BuildArcGeometry(double percent)
    {
        // 0%/100%ちょうどだと始点と終点が一致してArcSegmentが描画できないため少しだけ内側に寄せる
        var clamped = Math.Clamp(percent, 0.001, 99.999);
        const double startAngle = -90.0;
        var endAngle = startAngle + clamped / 100.0 * 360.0;

        var startPoint = PointOnCircle(startAngle);
        var endPoint = PointOnCircle(endAngle);
        var isLargeArc = clamped > 50.0;

        var figure = new PathFigure { StartPoint = startPoint, IsClosed = false };
        figure.Segments.Add(new ArcSegment(endPoint, new Size(Radius, Radius), 0,
            isLargeArc, SweepDirection.Clockwise, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static Point PointOnCircle(double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new Point(Center.X + Radius * Math.Cos(radians), Center.Y + Radius * Math.Sin(radians));
    }
}
