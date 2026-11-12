using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Drawing = System.Drawing;

namespace Knipser;

internal enum PixelMode { Picker, Magnifier }

/// <summary>Eingefrorener Bildschirm mit Lupe: Farbpipette (Klick kopiert den Farbwert) oder reine Lupe.</summary>
internal class PixelOverlay : Window
{
    static readonly int[] Zooms = { 2, 3, 4, 6, 8, 10, 12, 16, 20, 24, 32 };

    readonly BitmapSource shot;
    readonly Drawing.Rectangle vs;
    readonly PixelMode mode;
    readonly Canvas cv = new();
    readonly Image img = new() { Stretch = Stretch.Fill };
    readonly Rectangle loupe = new() { Stroke = Brushes.White, StrokeThickness = 2 };
    readonly Rectangle centerBox = new() { Stroke = Brushes.Red, StrokeThickness = 1.5, IsHitTestVisible = false };
    readonly Border info = new() { Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x20, 0x20, 0x20)), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 4, 8, 4), IsHitTestVisible = false };
    readonly Rectangle swatch = new() { Width = 18, Height = 18, Stroke = Brushes.White, StrokeThickness = 1, Margin = new Thickness(0, 0, 8, 0) };
    readonly TextBlock text = new() { Foreground = Brushes.White, FontFamily = new FontFamily("Consolas"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
    readonly double size;
    int zoomIdx;

    /// <summary>Gewählter Farbwert wie #RRGGBB (nur Pipette).</summary>
    public string? Result { get; private set; }

    public PixelOverlay(BitmapSource shot, Drawing.Rectangle virtualScreen, PixelMode mode)
    {
        this.shot = shot; vs = virtualScreen; this.mode = mode;
        size = mode == PixelMode.Picker ? 150 : 260;
        zoomIdx = Array.IndexOf(Zooms, mode == PixelMode.Picker ? 10 : 6);

        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = vs.X; Top = vs.Y; Width = 200; Height = 200;
        Cursor = Cursors.Cross; Background = Brushes.Black;

        img.Source = shot;
        loupe.Width = loupe.Height = size;
        loupe.IsHitTestVisible = false;
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        if (mode == PixelMode.Picker) sp.Children.Add(swatch);
        sp.Children.Add(text);
        info.Child = sp;
        cv.Children.Add(loupe); cv.Children.Add(centerBox); cv.Children.Add(info);
        var grid = new Grid();
        grid.Children.Add(img); grid.Children.Add(cv);
        Content = grid;

        SourceInitialized += (_, _) => Place();
        Loaded += (_, _) => { Place(); Activate(); Focus(); };
        ContentRendered += (_, _) =>
        {
            Place();
            Native.SetForegroundWindow(new WindowInteropHelper(this).Handle);
            if (Native.GetCursorPos(out var cp)) UpdateAt(PointFromScreen(new Point(cp.X, cp.Y)));
        };
        MouseMove += (_, e) => UpdateAt(e.GetPosition(cv));
        MouseLeftButtonDown += (_, e) =>
        {
            if (mode == PixelMode.Picker && PixelAt(e.GetPosition(cv), out var c, out int cx, out int cy)) { Result = Hex(c); Close(); }
        };
        MouseRightButtonDown += (_, _) => Close();
        MouseWheel += (_, e) =>
        {
            zoomIdx = Math.Clamp(zoomIdx + (e.Delta > 0 ? 1 : -1), 0, Zooms.Length - 1);
            UpdateAt(e.GetPosition(cv));
        };
        KeyDown += OnKey;
    }

    static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    void Place() =>
        Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.HWND_TOPMOST, vs.X, vs.Y, vs.Width, vs.Height, Native.SWP_SHOWWINDOW);

    void OnKey(object s, KeyEventArgs e)
    {
        int dx = 0, dy = 0;
        switch (e.Key)
        {
            case Key.Escape: Close(); return;
            case Key.Left: dx = -1; break;
            case Key.Right: dx = 1; break;
            case Key.Up: dy = -1; break;
            case Key.Down: dy = 1; break;
            case Key.Enter when mode == PixelMode.Picker:
                if (Native.GetCursorPos(out var cp) && PixelAt(PointFromScreen(new Point(cp.X, cp.Y)), out var c, out _, out _)) { Result = Hex(c); Close(); }
                return;
            default: return;
        }
        if (Native.GetCursorPos(out var p)) Native.SetCursorPos(p.X + dx, p.Y + dy);
        e.Handled = true;
    }

    bool PixelAt(Point p, out Color c, out int px, out int py)
    {
        double kx = shot.PixelWidth / cv.ActualWidth, ky = shot.PixelHeight / cv.ActualHeight;
        px = Math.Clamp((int)(p.X * kx), 0, shot.PixelWidth - 1);
        py = Math.Clamp((int)(p.Y * ky), 0, shot.PixelHeight - 1);
        var buf = new byte[4];
        try { shot.CopyPixels(new Int32Rect(px, py, 1, 1), buf, 4, 0); }
        catch { c = Colors.Black; return false; }
        c = Color.FromRgb(buf[2], buf[1], buf[0]);
        return true;
    }

    void UpdateAt(Point p)
    {
        if (cv.ActualWidth <= 0 || !PixelAt(p, out var c, out int px, out int py)) return;
        int zoom = Zooms[zoomIdx];
        int n = (int)(size / zoom) | 1;               // ungerade Anzahl Pixel, damit der mittlere genau in der Mitte liegt
        loupe.Fill = new ImageBrush(shot)
        {
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(px + 0.5 - n / 2.0, py + 0.5 - n / 2.0, n, n),
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(loupe, BitmapScalingMode.NearestNeighbor);

        double lx = p.X + 24, ly = p.Y + 24;
        double ih = 34;
        if (lx + size > cv.ActualWidth) lx = p.X - 24 - size;
        if (ly + size + ih + 6 > cv.ActualHeight) ly = p.Y - 24 - size - ih - 6;
        Canvas.SetLeft(loupe, lx); Canvas.SetTop(loupe, ly);

        double cell = size / n;
        centerBox.Width = centerBox.Height = cell;
        Canvas.SetLeft(centerBox, lx + (size - cell) / 2); Canvas.SetTop(centerBox, ly + (size - cell) / 2);

        swatch.Fill = new SolidColorBrush(c);
        text.Text = mode == PixelMode.Picker
            ? $"{Hex(c)}   {c.R},{c.G},{c.B}   x{px} y{py}"
            : $"{zoom}×   x{px} y{py}";
        Canvas.SetLeft(info, lx); Canvas.SetTop(info, ly + size + 6);
    }
}
