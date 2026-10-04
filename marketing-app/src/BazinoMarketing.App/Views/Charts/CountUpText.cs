using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using BazinoMarketing.App.Infrastructure;

namespace BazinoMarketing.App.Views.Charts;

/// <summary>
/// A TextBlock that counts up to <see cref="Value"/> when the number changes — the small "alive"
/// touch used on the daily-report tiles. With motion disabled it jumps straight to the final text,
/// which keeps offscreen screenshots deterministic.
/// </summary>
public sealed class CountUpText : TextBlock
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(CountUpText), new PropertyMetadata(0d, OnValueChanged));

    public static readonly DependencyProperty SuffixProperty = DependencyProperty.Register(
        nameof(Suffix), typeof(string), typeof(CountUpText), new PropertyMetadata(string.Empty, OnTextInputChanged));

    public static readonly DependencyProperty DecimalsProperty = DependencyProperty.Register(
        nameof(Decimals), typeof(int), typeof(CountUpText), new PropertyMetadata(0, OnTextInputChanged));

    private static readonly DependencyProperty DisplayValueProperty = DependencyProperty.Register(
        nameof(DisplayValue), typeof(double), typeof(CountUpText), new PropertyMetadata(0d, OnDisplayValueChanged));

    private static readonly DependencyProperty IsAnimatingProperty = DependencyProperty.Register(
        nameof(IsAnimating), typeof(bool), typeof(CountUpText), new PropertyMetadata(false));

    public CountUpText()
    {
        Text = Format(0);
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Suffix
    {
        get => (string)GetValue(SuffixProperty);
        set => SetValue(SuffixProperty, value);
    }

    public int Decimals
    {
        get => (int)GetValue(DecimalsProperty);
        set => SetValue(DecimalsProperty, value);
    }

    private double DisplayValue
    {
        get => (double)GetValue(DisplayValueProperty);
        set => SetValue(DisplayValueProperty, value);
    }

    private bool IsAnimating
    {
        get => (bool)GetValue(IsAnimatingProperty);
        set => SetValue(IsAnimatingProperty, value);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (CountUpText)d;
        var target = (double)e.NewValue;
        if (!Motion.Enabled)
        {
            control.BeginAnimation(DisplayValueProperty, null);
            control.DisplayValue = target;
            control.IsAnimating = false;
            return;
        }
        control.IsAnimating = true;
        var animation = new DoubleAnimation(target, Motion.Slow) { EasingFunction = Motion.Ease };
        control.BeginAnimation(DisplayValueProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void OnTextInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (CountUpText)d;
        if (!control.IsAnimating) control.Text = control.Format(control.DisplayValue);
    }

    private static void OnDisplayValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CountUpText)d).Text = ((CountUpText)d).Format((double)e.NewValue);

    private string Format(double value)
    {
        var format = Decimals > 0 ? "N" + Decimals : "N0";
        return value.ToString(format, CultureInfo.InvariantCulture) + Suffix;
    }
}
