using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using Media = System.Windows.Media;
using Controls = System.Windows.Controls;

namespace Otm.Kiosk.Shell;

public sealed class SecondaryDisplayWindow : Window
{
    private bool _allowClose;

    public SecondaryDisplayWindow(Drawing.Rectangle bounds)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        Background = new Media.SolidColorBrush(Media.Color.FromRgb(9, 13, 18));
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        Content = BuildContent();
    }

    public void CloseFromOwner()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }

    private static UIElement BuildContent()
    {
        var panel = new Controls.StackPanel
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        panel.Children.Add(new Controls.Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/Assets/simplekioskos_bottom.png")),
            Width = 360,
            Height = 230,
            Stretch = Media.Stretch.Uniform
        });
        panel.Children.Add(new Controls.TextBlock
        {
            Text = "Display locked by SimpleKioskOS",
            Foreground = Media.Brushes.White,
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 18, 0, 0)
        });
        panel.Children.Add(new Controls.TextBlock
        {
            Text = "This screen is reserved until an approved multi-monitor app starts.",
            Foreground = new Media.SolidColorBrush(Media.Color.FromRgb(175, 193, 207)),
            FontSize = 15,
            TextAlignment = TextAlignment.Center
        });

        return panel;
    }
}
