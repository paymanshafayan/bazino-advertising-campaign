using System.Windows;
using System.Windows.Controls;

namespace BazinoMarketing.App.Infrastructure;

/// <summary>Lets a PasswordBox participate in binding (PasswordBox.Password is deliberately not a dependency property).</summary>
public static class PasswordBoxHelper
{
    public static readonly DependencyProperty AttachProperty = DependencyProperty.RegisterAttached(
        "Attach", typeof(bool), typeof(PasswordBoxHelper), new PropertyMetadata(false, OnAttachChanged));

    public static readonly DependencyProperty PasswordProperty = DependencyProperty.RegisterAttached(
        "Password", typeof(string), typeof(PasswordBoxHelper),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPasswordPropertyChanged));

    private static readonly DependencyProperty IsUpdatingProperty = DependencyProperty.RegisterAttached(
        "IsUpdating", typeof(bool), typeof(PasswordBoxHelper), new PropertyMetadata(false));

    public static bool GetAttach(DependencyObject d) => (bool)d.GetValue(AttachProperty);
    public static void SetAttach(DependencyObject d, bool value) => d.SetValue(AttachProperty, value);
    public static string GetPassword(DependencyObject d) => (string)d.GetValue(PasswordProperty);
    public static void SetPassword(DependencyObject d, string value) => d.SetValue(PasswordProperty, value);

    private static void OnAttachChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;
        if ((bool)e.OldValue) box.PasswordChanged -= OnPasswordChanged;
        if ((bool)e.NewValue) box.PasswordChanged += OnPasswordChanged;
    }

    private static void OnPasswordPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;
        if ((bool)box.GetValue(IsUpdatingProperty)) return;
        box.PasswordChanged -= OnPasswordChanged;
        box.Password = (string?)e.NewValue ?? string.Empty;
        box.PasswordChanged += OnPasswordChanged;
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox box) return;
        box.SetValue(IsUpdatingProperty, true);
        SetPassword(box, box.Password);
        box.SetValue(IsUpdatingProperty, false);
    }
}
