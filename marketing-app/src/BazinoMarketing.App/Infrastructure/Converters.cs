using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using BazinoMarketing.Core.Logging;
using BazinoMarketing.Core.Tools;

namespace BazinoMarketing.App.Infrastructure;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value is bool v && v;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible ? !Invert : Invert;
}

public sealed class StringToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var has = value is string s && !string.IsNullOrWhiteSpace(s);
        if (Invert) has = !has;
        return has ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b ? !b : true;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b ? !b : false;
}

/// <summary>Two-way "is this string equal to the parameter" converter for RadioButton groups bound to a string property.</summary>
public sealed class StringEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter?.ToString() : Binding.DoNothing;
}

public sealed class ToolStateToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            ToolState.Connected => "SuccessBrush",
            ToolState.Checking => "AccentBrush",
            ToolState.NeedsLogin => "WarningBrush",
            ToolState.NotConfigured => "MutedBrush",
            ToolState.NetworkError => "DangerBrush",
            ToolState.Error => "DangerBrush",
            _ => "MutedBrush"
        };
        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class ToolStateToTextConverter : IValueConverter
{
    public static string Text(ToolState state) => state switch
    {
        ToolState.Connected => "متصل",
        ToolState.Checking => "در حال بررسی…",
        ToolState.NeedsLogin => "نیاز به ورود",
        ToolState.NotConfigured => "پیکربندی نشده",
        ToolState.NetworkError => "خطای شبکه",
        ToolState.Error => "خطا",
        _ => "بررسی نشده"
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ToolState s ? Text(s) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class LogLevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            LogLevel.Error => "DangerBrush",
            LogLevel.Warning => "WarningBrush",
            LogLevel.Success => "SuccessBrush",
            LogLevel.Debug => "MutedBrush",
            _ => "TextBrush"
        };
        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.White;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Maps the mailbox status kind ("active" / "listening" / "error" / "off") to the shell's dot colour.</summary>
public sealed class MailboxStateToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        "active" => Application.Current?.TryFindResource("SuccessBrush") as Brush ?? Brushes.Green,
        "listening" => Application.Current?.TryFindResource("WarningBrush") as Brush ?? Brushes.Orange,
        "error" => Application.Current?.TryFindResource("DangerBrush") as Brush ?? Brushes.Red,
        _ => Application.Current?.TryFindResource("MutedBrush") as Brush ?? Brushes.Gray
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Soft tinted background that pairs with <see cref="ToolStateToBrushConverter"/> for status pills.</summary>
public sealed class ToolStateToSoftBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            ToolState.Connected => "SuccessSoftBrush",
            ToolState.Checking => "InfoSoftBrush",
            ToolState.NeedsLogin => "AccentSoftBrush",
            ToolState.NetworkError or ToolState.Error => "DangerSoftBrush",
            ToolState.NotConfigured => "HoverBrush",
            _ => "HoverBrush"
        };
        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
