using System.Windows;
using System.Windows.Input;
using BazinoMarketing.App.ViewModels;

namespace BazinoMarketing.App;

public partial class MainWindow : Window
{
    private WindowState _stateBeforeFullscreen = WindowState.Maximized;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Owner decision (2026-10-03): the app opens filling the screen. It keeps the title bar so the window can still
        // be moved, minimised and closed; F11 (or Esc when borderless) switches to true borderless fullscreen.
        WindowState = WindowState.Maximized;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ToggleBorderlessFullscreen();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && WindowStyle == WindowStyle.None)
        {
            ToggleBorderlessFullscreen();
            e.Handled = true;
        }
    }

    private void ToggleBorderlessFullscreen()
    {
        if (WindowStyle == WindowStyle.None)
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = _stateBeforeFullscreen;
            return;
        }

        _stateBeforeFullscreen = WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
    }
}
