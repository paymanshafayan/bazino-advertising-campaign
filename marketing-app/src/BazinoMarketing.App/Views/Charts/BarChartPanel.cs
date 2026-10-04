using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using BazinoMarketing.App.Infrastructure;

namespace BazinoMarketing.App.Views.Charts;

/// <summary>
/// Simple, dependency-free vertical bar chart drawn on a Canvas. Bars grow from the baseline with a
/// small stagger when the data arrives, so the daily-report page feels animated without a chart library.
/// </summary>
public sealed class BarChartPanel : Canvas
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IEnumerable), typeof(BarChartPanel), new PropertyMetadata(null, OnDataChanged));

    public static readonly DependencyProperty LabelsProperty = DependencyProperty.Register(
        nameof(Labels), typeof(IEnumerable), typeof(BarChartPanel), new PropertyMetadata(null, OnDataChanged));

    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(
        nameof(BarBrush), typeof(Brush), typeof(BarChartPanel), new PropertyMetadata(null, OnDataChanged));

    public static readonly DependencyProperty HighlightBrushProperty = DependencyProperty.Register(
        nameof(HighlightBrush), typeof(Brush), typeof(BarChartPanel), new PropertyMetadata(null, OnDataChanged));

    public static readonly DependencyProperty ShowValuesProperty = DependencyProperty.Register(
        nameof(ShowValues), typeof(bool), typeof(BarChartPanel), new PropertyMetadata(true, OnDataChanged));

    private const double LabelHeight = 20;
    private const double ValueHeight = 16;
    private const double Gap = 8;
    private bool _rendered;

    public BarChartPanel()
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

    public Brush? BarBrush
    {
        get => (Brush?)GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    public Brush? HighlightBrush
    {
        get => (Brush?)GetValue(HighlightBrushProperty);
        set => SetValue(HighlightBrushProperty, value);
    }

    public bool ShowValues
    {
        get => (bool)GetValue(ShowValuesProperty);
        set => SetValue(ShowValuesProperty, value);
    }

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((BarChartPanel)d).Render();

    private void Render()
    {
        Children.Clear();
        _rendered = false;

        var values = ChartData.ToDoubles(Values);
        if (values.Count == 0) return;
        var labels = ChartData.ToStrings(Labels);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 40 || height < 60) return;

        var baseline = height - LabelHeight;
        var chartTop = ValueHeight / 2;
        var usable = Math.Max(10, baseline - chartTop);
        var max = Math.Max(1d, values.Max());
        var barBrush = BarBrush ?? new SolidColorBrush(Color.FromRgb(0xD9, 0x9A, 0x00));
        var highlight = HighlightBrush ?? barBrush;
        var slot = width / values.Count;
        var barWidth = Math.Max(3, Math.Min(38, slot - Gap));
        var lastIndex = values.Count - 1;

        for (var i = 0; i < values.Count; i++)
        {
            var ratio = values[i] / max;
            var barHeight = Math.Max(2, usable * ratio);
            var left = (i * slot) + ((slot - barWidth) / 2);

            var bar = new Rectangle
            {
                Width = barWidth,
                Height = barHeight,
                RadiusX = 4,
                RadiusY = 4,
                Fill = i == lastIndex ? highlight : barBrush,
                Opacity = i == lastIndex ? 1 : 0.9
            };
            bar.RenderTransformOrigin = new Point(0.5, 1);
            Canvas.SetLeft(bar, left);
            Canvas.SetTop(bar, baseline - barHeight);
            Children.Add(bar);

            if (ShowValues && values.Count <= 16)
            {
                var caption = new TextBlock
                {
                    Text = ChartData.Format(values[i]),
                    FontSize = 10.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x70, 0x85)),
                    Width = slot,
                    TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(caption, i * slot);
                Canvas.SetTop(caption, Math.Max(0, baseline - barHeight - ValueHeight));
                Children.Add(caption);
            }

            if (labels.Count > i)
            {
                var label = new TextBlock
                {
                    Text = labels[i],
                    FontSize = 10.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x70, 0x85)),
                    Width = slot,
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Canvas.SetLeft(label, i * slot);
                Canvas.SetTop(label, baseline + 4);
                Children.Add(label);
            }

            if (Motion.Enabled)
            {
                var scale = new ScaleTransform(1, 0);
                bar.RenderTransform = scale;
                var grow = new DoubleAnimation(0, 1, Motion.Smooth)
                {
                    BeginTime = TimeSpan.FromMilliseconds(i * 35),
                    EasingFunction = Motion.Ease
                };
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                var fade = Motion.Fade(0, 1, Motion.Quick, TimeSpan.FromMilliseconds(i * 35));
                bar.BeginAnimation(OpacityProperty, fade);
            }
        }

        _rendered = true;
    }

    /// <summary>True after the first successful draw (used by the report view to trigger its own touches).</summary>
    public bool HasRendered => _rendered;
}

/// <summary>Shared helpers for the small chart panels.</summary>
internal static class ChartData
{
    public static List<double> ToDoubles(IEnumerable? source)
    {
        var list = new List<double>();
        if (source is null) return list;
        foreach (var item in source)
        {
            var value = ToDouble(item);
            if (value is { } number) list.Add(number);
        }
        return list;
    }

    public static double? ToDouble(object? item) => item switch
    {
        null => null,
        double d => d,
        float f => f,
        decimal m => (double)m,
        int i => i,
        long l => l,
        short s => s,
        byte b => b,
        string text when double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null
    };

    public static List<string> ToStrings(IEnumerable? source)
    {
        var list = new List<string>();
        if (source is null) return list;
        foreach (var item in source) list.Add(item?.ToString() ?? string.Empty);
        return list;
    }

    public static string Format(double value) => value >= 1000
        ? value.ToString("N0", CultureInfo.InvariantCulture)
        : value.ToString("0.#", CultureInfo.InvariantCulture);

    public static string FormatShort(double value) => value switch
    {
        >= 1_000_000 => (value / 1_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + "M",
        >= 1_000 => (value / 1_000d).ToString("0.#", CultureInfo.InvariantCulture) + "K",
        _ => value.ToString("0.#", CultureInfo.InvariantCulture)
    };
}
