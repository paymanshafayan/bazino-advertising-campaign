using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BazinoMarketing.App.ViewModels;

namespace BazinoMarketing.App.Views;

public partial class ConnectionView : UserControl
{
    public ConnectionView()
    {
        InitializeComponent();
    }

    /// <summary>«تنظیمات» on a tool card jumps to the settings page that holds the same card.</summary>
    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        if (FindMainViewModel() is { } main) main.CurrentPage = "settings";
    }

    private MainViewModel? FindMainViewModel()
    {
        DependencyObject? node = this;
        while (node is not null)
        {
            if (node is FrameworkElement { DataContext: MainViewModel main }) return main;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }
        return Window.GetWindow(this)?.DataContext as MainViewModel;
    }
}
