using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;

namespace Otm.Kiosk.Shell;

public sealed class AdminButtonWindow : Window
{
    public event EventHandler? AdminRequested;

    public AdminButtonWindow()
    {
        Width = 100;
        Height = 52;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = WpfBrushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;

        var button = new WpfButton
        {
            Width = 92,
            Height = 44,
            Padding = new Thickness(0),
            ToolTip = "Admin",
            Background = new SolidColorBrush(WpfColor.FromRgb(248, 250, 252)),
            BorderBrush = new SolidColorBrush(WpfColor.FromRgb(203, 213, 225)),
            BorderThickness = new Thickness(1),
            Content = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Children =
                {
                    new Viewbox
                    {
                        Width = 16,
                        Height = 16,
                        Margin = new Thickness(0, 0, 7, 0),
                        Child = new System.Windows.Shapes.Path
                        {
                            Fill = new SolidColorBrush(WpfColor.FromRgb(17, 24, 39)),
                            Data = Geometry.Parse("M9.405,1.05 C8.992,-0.35 7.008,-0.35 6.595,1.05 L6.495,1.39 C6.22,2.322 5.149,2.765 4.31,2.28 L4,2.1 C2.734,1.37 1.334,2.77 2.064,4.036 L2.244,4.346 C2.73,5.185 2.286,6.256 1.354,6.531 L1.014,6.631 C-0.386,7.044 -0.386,9.028 1.014,9.441 L1.354,9.541 C2.286,9.816 2.73,10.887 2.244,11.726 L2.064,12.036 C1.334,13.302 2.734,14.702 4,13.972 L4.31,13.792 C5.149,13.306 6.22,13.75 6.495,14.682 L6.595,15.022 C7.008,16.422 8.992,16.422 9.405,15.022 L9.505,14.682 C9.78,13.75 10.851,13.306 11.69,13.792 L12,13.972 C13.266,14.702 14.666,13.302 13.936,12.036 L13.756,11.726 C13.27,10.887 13.714,9.816 14.646,9.541 L14.986,9.441 C16.386,9.028 16.386,7.044 14.986,6.631 L14.646,6.531 C13.714,6.256 13.27,5.185 13.756,4.346 L13.936,4.036 C14.666,2.77 13.266,1.37 12,2.1 L11.69,2.28 C10.851,2.765 9.78,2.322 9.505,1.39 Z M8,10.93 A2.929,2.929 0 1 1 8,5.072 A2.929,2.929 0 0 1 8,10.93 Z")
                        }
                    },
                    new TextBlock
                    {
                        Text = "Admin",
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(WpfColor.FromRgb(17, 24, 39)),
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            }
        };
        button.Click += (_, _) => AdminRequested?.Invoke(this, EventArgs.Empty);

        Content = new Border
        {
            Background = WpfBrushes.Transparent,
            Padding = new Thickness(4),
            Child = button
        };

        Loaded += (_, _) => PlaceTopRight();
    }

    public void PlaceTopRight()
    {
        var area = Forms.Screen.PrimaryScreen?.Bounds;
        if (area is null)
        {
            return;
        }

        Left = area.Value.Right - Width - 20;
        Top = area.Value.Top + 20;
    }
}
