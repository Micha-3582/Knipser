using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Drawing = System.Drawing;

namespace Knipser;

/// <summary>
/// Orangefarbener Rahmen um den Aufnahmebereich, damit er während der Scroll-Aufnahme sichtbar bleibt.
/// Liegt außerhalb des Bereichs (landet also nicht im Bild) und lässt alle Mausaktionen durch.
/// </summary>
internal class RegionFrame : Window
{
    const int Pad = 6;          // Pixel Abstand um den Bereich
    const int BannerPx = 26;    // Platz für die Beschriftung oberhalb

    readonly Drawing.Rectangle region;

    public RegionFrame(Drawing.Rectangle region)
    {
        this.region = region;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; ShowActivated = false; Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = region.X; Top = region.Y; Width = 50; Height = 50;

        var orange = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00));
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        var banner = new Border
        {
            Background = orange, CornerRadius = new CornerRadius(4, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 2, 10, 2), Height = 22, VerticalAlignment = VerticalAlignment.Bottom,
            Child = new TextBlock { Text = "Scroll-Aufnahme läuft – bitte nicht bewegen", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Center },
        };
        Grid.SetRow(banner, 0);
        var frame = new Border { BorderBrush = orange, BorderThickness = new Thickness(3), Background = Brushes.Transparent };
        Grid.SetRow(frame, 1);
        grid.Children.Add(banner); grid.Children.Add(frame);
        Content = grid;

        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            Native.SetWindowLong(h, Native.GWL_EXSTYLE, ex | Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
            Place(h);
        };
        Loaded += (_, _) => Place(new WindowInteropHelper(this).Handle);
    }

    void Place(System.IntPtr h) =>
        Native.SetWindowPos(h, Native.HWND_TOPMOST, region.X - Pad, region.Y - Pad - BannerPx,
            region.Width + 2 * Pad, region.Height + 2 * Pad + BannerPx, Native.SWP_SHOWWINDOW | Native.SWP_NOACTIVATE);
}
