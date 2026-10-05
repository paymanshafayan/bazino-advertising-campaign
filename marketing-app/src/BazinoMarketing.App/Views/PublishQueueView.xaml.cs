using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BazinoMarketing.App.ViewModels;

namespace BazinoMarketing.App.Views;

/// <summary>
/// Instagram-like review surface (PLAN-004): the carousel is browsable with the on-screen buttons, the ← → keys,
/// the mouse wheel and a mouse drag; double-click opens the slide full-frame.
/// </summary>
public partial class PublishQueueView : UserControl
{
    private Point? _dragStart;

    public PublishQueueView()
    {
        InitializeComponent();
    }

    private PublishQueuePostViewModel? PostOf(object sender) =>
        (sender as FrameworkElement)?.DataContext as PublishQueuePostViewModel;

    /// <summary>
    /// Phase 5 (2026-10-04): the review area tells the view-model its height so every card can size its frame to fit the
    /// whole post (media + caption + buttons) in one look instead of forcing a scroll.
    /// </summary>
    private void View_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is PublishQueueViewModel queue && e.NewSize.Height > 0) queue.ViewportHeight = e.NewSize.Height;
    }

    private void Card_MouseEnter(object sender, RoutedEventArgs e)
    {
        if (DataContext is PublishQueueViewModel queue && PostOf(sender) is { } post) queue.ActivePost = post;
    }

    private void View_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not PublishQueueViewModel queue || queue.ActivePost is not { } post) return;
        // RTL reading order: «next» lives on the left, «previous» on the right — the same as Instagram's own arrows.
        if (e.Key is Key.Left)
        {
            post.Step(1);
            e.Handled = true;
        }
        else if (e.Key is Key.Right)
        {
            post.Step(-1);
            e.Handled = true;
        }
    }

    private void Slide_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null) return;
        if (PostOf(sender) is not { } post) return;
        if (DataContext is PublishQueueViewModel queue) queue.ActivePost = post;
        if (e.ClickCount == 2)
        {
            OpenFullFrame(post);
            return;
        }
        _dragStart = e.GetPosition(this);
        (sender as UIElement)?.Focus();
        (sender as UIElement)?.CaptureMouse();
    }

    private void Slide_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || PostOf(sender) is not { } post) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(this).X - start.X;
        if (Math.Abs(delta) < 40) return;
        // Dragging to the left walks forward through the slides, dragging to the right walks back.
        post.Step(delta < 0 ? 1 : -1);
        _dragStart = e.GetPosition(this);
    }

    private void Slide_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement element && element.IsMouseCaptured) element.ReleaseMouseCapture();
        _dragStart = null;
    }

    private void Slide_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (PostOf(sender) is not { HasMultipleSlides: true } post) return;
        post.Step(e.Delta < 0 ? 1 : -1);
        e.Handled = true;
    }

    private void Zoom_Click(object sender, RoutedEventArgs e)
    {
        if (PostOf(sender) is { CurrentSlide: { } slide } post) OpenFullFrame(post, slide);
    }

    private void OpenFullFrame(PublishQueuePostViewModel post, PublishQueueSlideViewModel? slide = null)
    {
        slide ??= post.CurrentSlide;
        if (slide is null) return;
        var panel = new DockPanel { Background = (Brush)FindResource("PhoneScreenBrush"), LastChildFill = true };
        var caption = new TextBlock
        {
            Text = post.Title + "\n" + slide.PositionText + " — با کلیدهای ← → اسلاید عوض می‌شود؛ Esc می‌بندد.",
            Foreground = (Brush)FindResource("OnDarkBrush"),
            Margin = new Thickness(18, 12, 18, 8),
            FontSize = 15,
            TextAlignment = TextAlignment.Center
        };
        DockPanel.SetDock(caption, Dock.Top);
        panel.Children.Add(caption);
        if (slide.HasVideo && slide.VideoUri is not null)
        {
            panel.Children.Add(new MediaElement
            {
                Source = slide.VideoUri,
                LoadedBehavior = MediaState.Play,
                UnloadedBehavior = MediaState.Stop,
                Stretch = Stretch.Uniform
            });
        }
        else if (slide.HasImage && slide.ImageUri is not null)
        {
            panel.Children.Add(new Image { Source = new BitmapImage(slide.ImageUri), Stretch = Stretch.Uniform });
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = slide.PlaceholderText,
                Foreground = (Brush)FindResource("MutedBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420,
                TextAlignment = TextAlignment.Center
            });
        }
        // Phase 6 (2026-10-04): the preview is sized from the screen's work area — never taller or wider than the screen,
        // so the top of the window can never sit outside the display (the old fixed 900×1080 did exactly that on 1080p).
        var work = SystemParameters.WorkArea;
        var width = Math.Max(520, Math.Min(1100, work.Width - 80));
        var height = Math.Max(420, Math.Min(1320, work.Height - 80));
        var window = new Window
        {
            Title = $"{post.Title} — {slide.PositionText}",
            Owner = Window.GetWindow(this),
            Width = width,
            Height = height,
            MaxWidth = Math.Max(520, work.Width - 20),
            MaxHeight = Math.Max(420, work.Height - 20),
            // CenterScreen instead of CenterOwner: with the main window spanning two monitors the owner-centred window
            // could still land off the visible screen.
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            WindowState = WindowState.Normal,
            Background = (Brush)FindResource("PhoneScreenBrush"),
            FlowDirection = FlowDirection.RightToLeft,
            Content = panel
        };
        window.PreviewKeyDown += (_, args) =>
        {
            if (args.Key is Key.Left)
            {
                post.Step(1);
                window.Close();
            }
            else if (args.Key is Key.Right)
            {
                post.Step(-1);
                window.Close();
            }
            else if (args.Key is Key.Escape)
            {
                window.Close();
            }
        };
        window.ShowDialog();
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match) return match;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        return null;
    }
}
