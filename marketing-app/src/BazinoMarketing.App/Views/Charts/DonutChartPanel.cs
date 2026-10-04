using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using BazinoMarketing.App.Infrastructure;

namespace BazinoMarketing.App.Views.Charts;

/// <summary>
/// Donut chart for the engagement split. Segments are drawn as stroked arcs; the ring scales and fades
/// in when the data arrives (instant when motion is off, so screenshots stay deterministic).
/// </summary>
public sealed class DonutChartPanel : Canvas
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(IEnumerable), typeof(DonutChartPanel), new PropertyMetadata(null, OnDataChanged));

    public static readonly DependencyProperty PaletteProperty = DependencyProperty.Register(
        nameof(Palette), typeof(IEnumerable), typeof(DonutChartPanel), new PropertyMetadata(null, OnDataChanged));

    public static readonly DependencyProperty CentreValueProperty = DependencyProperty.Register(
        nameof(CentreValue), typeof(string), typeof(DonutChartPanel), new PropertyMetadata(string.Empty, OnDataChanged));

    public static readonly DependencyProperty CentreLabelProperty = DependencyProperty.Register(
        nameof(CentreLabel), typeof(string), typeof(DonutChartPanel), new PropertyMetadata(string.Empty, OnDataChanged));

    private const double Thickness = 16;

    public DonutChartPanel()
    {
        ClipToBounds = true;
        FlowDirection = FlowDirection.LeftToRight;
        Loaded += (_, _) => Render();
        SizeChanged += (_, _) => Render();
    }

    public IEnumerable? Segments
    {
        get => (IEnumerable?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public IEnumerable? Palette
    {
        get => (IEnumerable?)GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    public string CentreValue
    {
        get => (string)GetValue(CentreValueProperty);
        set => SetValue(CentreValueProperty, value);
    }

    public string CentreLabel
    {
        get => (string)GetValue(CentreLabelProperty);
        set => SetValue(CentreLabelProperty, value);
    }

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((DonutChartPanel)d).Render();

    private void Render()
    {
        Children.Clear();
        var values = ChartData.ToDoubles(Segments);
        var width = ActualWidth;
        var height = ActualHeight;
        if (values.Count == 0 || width < 60 || height < 60) return;

        var brushes = new List<Brush>();
        if (Palette is not null)
            foreach (var item in Palette)
                if (item is Brush brush) brushes.Add(brush);
        if (brushes.Count == 0)
            brushes.AddRange(new[]
            {
                new SolidColorBrush(Color.FromRgb(0xD9, 0x9A, 0x00)),
                new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
                new SolidColorBrush(Color.FromRgb(0x12, 0x80, 0x5C)),
                new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED)),
                new SolidColorBrush(Color.FromRgb(0xEA, 0x6A, 0x0C)),
                new SolidColorBrush(Color.FromRgb(0x66, 0x70, 0x85))
            });

        var size = Math.Min(width, height);
        var radius = (size - Thickness) / 2 - 2;
        var centre = new Point(width / 2, height / 2);
        var total = Math.Max(1d, values.Sum());

        var canvas = new Canvas { Width = width, Height = height, RenderTransformOrigin = new Point(0.5, 0.5) };
        var angle = -90d;
        for (var i = 0; i < values.Count; i++)
        {
            var sweep = 360d * (values[i] / total);
            if (sweep <= 0.2) { angle += sweep; continue; }
            var brush = brushes[i % brushes.Count];
            canvas.Children.Add(CreateArc(centre, radius, angle, sweep, brush));
            angle += sweep;
        }
        Children.Add(canvas);

        if (!string.IsNullOrEmpty(CentreValue))
        {
            var stack = new StackPanel { Width = size };
            var value = new TextBlock { Text = CentreValue, FontSize = 20, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(0x18, 0x21, 0x35)) };
            var label = new TextBlock { Text = CentreLabel, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x70, 0x85)) };
            stack.Children.Add(value);
            stack.Children.Add(label);
            Canvas.SetLeft(stack, 0);
            Canvas.SetTop(stack, centre.Y - 18);
            Children.Add(stack);
        }

        if (Motion.Enabled)
        {
            canvas.Opacity = 0;
            canvas.BeginAnimation(OpacityProperty, Motion.Fade(0, 1, Motion.Smooth));
            var scale = new ScaleTransform(0.86, 0.86);
            canvas.RenderTransform = scale;
            var grow = new DoubleAnimation(0.86, 1, Motion.Smooth) { EasingFunction = Motion.Ease };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }
    }

    private static Path CreateArc(Point centre, double radius, double startAngle, double sweepAngle, Brush brush)
    {
        var clamped = Math.Min(359.9, sweepAngle);
        var start = PointOnCircle(centre, radius, startAngle);
        var end = PointOnCircle(centre, radius, startAngle + clamped);
        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            IsLargeArc = clamped > 180,
            SweepDirection = SweepDirection.Clockwise
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return new Path
        {
            Data = geometry,
            Stroke = brush,
            StrokeThickness = Thickness,
            StrokeStartLineCap = PenLineCap.Flat,
            StrokeEndLineCap = PenLineCap.Flat
        };
    }

    private static Point PointOnCircle(Point centre, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180d;
        return new Point(centre.X + (radius * Math.Cos(radians)), centre.Y + (radius * Math.Sin(radians)));
    }
}
