using System.Windows;
using System.Windows.Media.Animation;

namespace BazinoMarketing.App.Infrastructure;

/// <summary>
/// Central motion settings. Animations make the report tab feel alive for the owner, but they are
/// switched off in sample/screenshot mode so a rendered PNG always shows the final state.
/// </summary>
public static class Motion
{
    public static bool Enabled { get; set; } = true;

    public static Duration Quick { get; } = new(TimeSpan.FromMilliseconds(180));
    public static Duration Smooth { get; } = new(TimeSpan.FromMilliseconds(380));
    public static Duration Slow { get; } = new(TimeSpan.FromMilliseconds(750));

    public static IEasingFunction Ease { get; } = new CubicEase { EasingMode = EasingMode.EaseOut };

    public static DoubleAnimation Fade(double from, double to, Duration duration, TimeSpan? beginTime = null) =>
        new(from, to, duration) { EasingFunction = Ease, BeginTime = beginTime ?? TimeSpan.Zero };
}
