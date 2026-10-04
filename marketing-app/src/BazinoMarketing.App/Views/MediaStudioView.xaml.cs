using System.Windows;
using System.Windows.Controls;

namespace BazinoMarketing.App.Views;

public partial class MediaStudioView : UserControl
{
    public MediaStudioView() => InitializeComponent();

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        try { Player.Play(); } catch (InvalidOperationException) { }
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        try { Player.Pause(); } catch (InvalidOperationException) { }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        try { Player.Stop(); } catch (InvalidOperationException) { }
    }
}
