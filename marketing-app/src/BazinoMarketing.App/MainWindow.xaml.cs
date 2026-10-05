using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
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
        // Phase 7 (owner order 2026-10-04): the app itself is never topmost — and if an older capture tool left the
        // topmost flag on the window handle, the flag is cleared as soon as the window is ready.
        Topmost = false;
        WindowState = WindowState.Maximized;
        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += (_, _) => EnsureNotTopmost();
        PreviewMouseDown += (_, _) => EnsureNotTopmost();
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
            EnsureNotTopmost();
            return;
        }

        _stateBeforeFullscreen = WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        EnsureNotTopmost();
    }

    /// <summary>
    /// Phase 7: the agent's automation asks the app itself to put the window back to a normal, non-topmost state after a
    /// capture run (command <c>window.state</c>). The app never forces itself above other windows.
    /// </summary>
    public object ApplyWindowMode(string mode)
    {
        var normalized = (mode ?? "").Trim().ToLowerInvariant();
        try
        {
            EnsureNotTopmost();
            switch (normalized)
            {
                case "minimize":
                    WindowState = WindowState.Minimized;
                    break;
                case "maximize":
                case "maximized":
                    WindowStyle = WindowStyle.SingleBorderWindow;
                    ResizeMode = ResizeMode.CanResize;
                    WindowState = WindowState.Maximized;
                    break;
                case "normal":
                case "restore":
                case "":
                default:
                    // Back from borderless fullscreen too, so the title bar and the normal frame return.
                    WindowStyle = WindowStyle.SingleBorderWindow;
                    ResizeMode = ResizeMode.CanResize;
                    ShowInTaskbar = true;
                    WindowState = WindowState.Normal;
                    Activate();
                    break;
            }
            EnsureNotTopmost();
            return new
            {
                ok = true,
                mode = normalized.Length == 0 ? "restore" : normalized,
                windowState = WindowState.ToString().ToLowerInvariant(),
                windowStyle = WindowStyle.ToString().ToLowerInvariant(),
                topmost = Topmost,
                left = (int)Left,
                top = (int)Top,
                width = (int)Width,
                height = (int)Height,
                message = "پنجرهٔ ژینوس به حالت عادی برگشت و روی پنجره‌های دیگر نمی‌ماند."
            };
        }
        catch (Exception ex)
        {
            return new { ok = false, mode = normalized, error = ex.GetType().Name, message = ex.Message };
        }
    }

    /// <summary>Clears the Win32 topmost style if a leftover capture tool set it on our handle.</summary>
    public void EnsureNotTopmost()
    {
        Topmost = false;
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;
            var exStyle = GetWindowLongPtr(handle, GWL_EXSTYLE).ToInt64();
            if ((exStyle & WS_EX_TOPMOST) != 0)
                SetWindowLongPtr(handle, GWL_EXSTYLE, new IntPtr(exStyle & ~WS_EX_TOPMOST));
            SetWindowPos(handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        catch (Exception)
        {
            // The window is only decorative here: failing to clear the flag must never crash the app.
        }
    }

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, index) : new IntPtr(GetWindowLong32(hWnd, index));

    private static void SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr64(hWnd, index, value);
        else SetWindowLong32(hWnd, index, value.ToInt32());
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOPMOST = 0x00000008;
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
