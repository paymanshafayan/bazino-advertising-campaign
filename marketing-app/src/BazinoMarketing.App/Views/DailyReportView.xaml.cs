using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using BazinoMarketing.App.Infrastructure;
using BazinoMarketing.App.ViewModels;

namespace BazinoMarketing.App.Views;

public partial class DailyReportView : UserControl
{
    private bool _shown;

    public DailyReportView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DailyReportViewModel viewModel && !_shown)
        {
            _shown = true;
            _ = viewModel.RefreshAsync();
        }

        if (!Motion.Enabled) return;
        Root.Opacity = 0;
        Root.BeginAnimation(OpacityProperty, Motion.Fade(0, 1, Motion.Smooth));
        var slide = new DoubleAnimation(14, 0, Motion.Smooth) { EasingFunction = Motion.Ease };
        if (Root.RenderTransform is null or System.Windows.Media.TranslateTransform)
        {
            Root.RenderTransform = new System.Windows.Media.TranslateTransform();
            ((System.Windows.Media.TranslateTransform)Root.RenderTransform).BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slide);
        }
    }
}
