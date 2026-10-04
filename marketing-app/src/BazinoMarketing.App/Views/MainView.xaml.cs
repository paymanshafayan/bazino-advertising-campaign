using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BazinoMarketing.App.Infrastructure;

namespace BazinoMarketing.App.Views;

public partial class MainView : UserControl
{
    private object? _lastContent;

    public MainView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => BindAndAnimate();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldVm) oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        if (e.NewValue is INotifyPropertyChanged newVm) newVm.PropertyChanged += OnViewModelPropertyChanged;
        BindAndAnimate();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "CurrentContent") AnimatePageIn();
    }

    private void BindAndAnimate()
    {
        _lastContent = null;
        AnimatePageIn();
    }

    /// <summary>Subtle fade + slide whenever the owner switches section; instant when motion is off.</summary>
    private void AnimatePageIn()
    {
        var content = (DataContext as ViewModels.MainViewModel)?.CurrentContent;
        if (ReferenceEquals(content, _lastContent)) return;
        _lastContent = content;

        if (!Motion.Enabled || PageShift is null)
        {
            PageHost.Opacity = 1;
            return;
        }

        var fade = new DoubleAnimation(0.0, 1.0, Motion.Quick);
        PageHost.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        var slide = new DoubleAnimation(10, 0, Motion.Smooth);
        PageShift.BeginAnimation(TranslateTransform.YProperty, slide, HandoffBehavior.SnapshotAndReplace);
    }
}
