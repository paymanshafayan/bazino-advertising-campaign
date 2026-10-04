using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using BazinoMarketing.App.Infrastructure;

namespace BazinoMarketing.App.Views.Charts;

/// <summary>
/// Small area/line chart for follower history. The curve is revealed with an animated clip so it draws
/// itself left-to-right; with motion disabled the full curve is shown immediately.
/// </summary>
public sealed class LineChartPanel : Canvas
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IEnumerable), typeof(LineChartPanel), new PropertyMetadata(null, OnDataChanged));

    public static readonly DependencyProperty LabelsProperty = DependencyProperty.Register(
        nameof(Labels), typeof(IEnumerable), typeof(LineChartPanel), new PropertyMetadata(null, OnDataChanged));

    public static readonly DependencyProperty StrokeBrushProperty = DependencyProperty.Register(
        nameof(StrokeBrush), typeof(Brush), typeof(LineChartPanel), new PropertyMetadata(null, OnDataChanged));

    public static readonly DependencyProperty AreaBrushProperty = DependencyProperty.Register(
        nameof(AreaBrush), typeof(Brush), typeof(LineChartPanel), new PropertyMetadata(null, OnDataChanged));

    private const double PadTop = 16;
    private const double PadBottom = 22;
    private const double PadSide = 8;

    public LineChartPanel()
    {
        ClipToBounds = true;
        FlowDirection = FlowDirection.LeftToRight;
        Loaded += (_, _) => Render();
        SizeChanged += (_, _) => Render();
    }

    public IEnumerable? Values
    {
        get => (IEnumerable?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IEnumerable? Labels
    {
        get => (IEnumerable?)GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    public Brush? StrokeBrush
    {
        get => (Brush?)GetValue(StrokeBrushProperty);
        set => SetValue(StrokeBrushProperty, value);
    }

    public Brush? AreaBrush
    {
        get => (Brush?)GetValue(AreaBrushProperty);
        set => SetValue(AreaBrushProperty, value);
    }

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((LineChartPanel)d).Render();

    private void Render()
    {
        Children.Clear();
        var values = ChartData.ToDoubles(Values);
        var width = ActualWidth;
        var height = ActualHeight;
        if (values.Count < 2 || width < 60 || height < 60) return;

        var labels = ChartData.ToStrings(Labels);
        var min = values.Min();
        var max = values.Max();
        var span = Math.Max(1d, max - min);
        var plotWidth = Math.Max(1, width - (PadSide * 2));
        var plotHeight = Math.Max(1, height - PadTop - PadBottom);
        var step = plotWidth / (values.Count - 1);
        var stroke = StrokeBrush ?? new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
        var area = AreaBrush ?? new SolidColorBrush(Color.FromArgb(0x20, 0x25, 0x63, 0xEB));

        var points = new PointCollection();
        for (var i = 0; i < values.Count; i++)
        {
            var x = PadSide + (i * step);
            var y = PadTop + (plotHeight * (1 - ((values[i] - min) / span)));
            points.Add(new Point(x, y));
        }

        var areaPoints = new PointCollection(points) { new(width - PadSide, height - PadBottom), new(PadSide, height - PadBottom) };
        var areaShape = new Polygon { Points = areaPoints, Fill = area, Stroke = null };
        var line = new Polyline { Points = points, Stroke = stroke, StrokeThickness = 2.4, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };

        var layer = new Canvas { Width = width, Height = height, ClipToBounds = true };
        layer.Children.Add(areaShape);
        layer.Children.Add(line);

        var clip = new RectangleGeometry(new Rect(0, 0, width, height));
        layer.Clip = clip;
        Children.Add(layer);

        var last = points[^1];
        var dot = new Ellipse { Width = 9, Height = 9, Fill = stroke, Stroke = Brushes.White, StrokeThickness = 2 };
        Canvas.SetLeft(dot, last.X - 4.5);
        Canvas.SetTop(dot, last.Y - 4.5);
        Children.Add(dot);

        var badge = new Border
        {
            Background = stroke,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 2, 8, 2),
            Child = new TextBlock
            {
                Text = ChartData.Format(values[^1]),
                Foreground = Brushes.White,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold
            }
        };
        Canvas.SetLeft(badge, Math.Max(0, Math.Min(width - 46, last.X - 23)));
        Canvas.SetTop(badge, Math.Max(0, last.Y - 26));
        Children.Add(badge);

        if (labels.Count > 0)
        {
            AddLabel(labels[0], PadSide, height - PadBottom + 4, TextAlignment.Left);
            if (labels.Count > 1) AddLabel(labels[^1], width - PadSide - 90, height - PadBottom + 4, TextAlignment.Right);
        }

        if (Motion.Enabled)
        {
            clip.Rect = new Rect(0, 0, 0, height);
            var reveal = new RectAnimation(new Rect(0, 0, 0, height), new Rect(0, 0, width, height), Motion.Slow)
            {
                EasingFunction = Motion.Ease
            };
            clip.BeginAnimation(RectangleGeometry.RectProperty, reveal);
            dot.Opacity = 0;
            dot.BeginAnimation(OpacityProperty, Motion.Fade(0, 1, Motion.Quick, TimeSpan.FromMilliseconds(520)));
            badge.Opacity = 0;
            badge.BeginAnimation(OpacityProperty, Motion.Fade(0, 1, Motion.Quick, TimeSpan.FromMilliseconds(560)));
        }
    }

    private void AddLabel(string text, double left, double top, TextAlignment alignment)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = 10.5,
            Width = 90,
            TextAlignment = alignment,
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x70, 0x85))
        };
        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, top);
        Children.Add(label);
    }
}
