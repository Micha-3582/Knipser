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

internal enum PixelMode { Picker, Magnifier, Ruler }

/// <summary>Eingefrorener Bildschirm mit Lupe: Farbpipette (Klick kopiert den Farbwert), reine Lupe oder Messen (Gummiband-Strecke in Pixeln).</summary>
internal class PixelOverlay : Window
{
    static readonly int[] Zooms = { 2, 3, 4, 6, 8, 10, 12, 16, 20, 24, 32 };

    readonly BitmapSource shot;
    readonly Drawing.Rectangle vs;
    readonly PixelMode mode;
    readonly Canvas cv = new();
    readonly Image img = new() { Stretch = Stretch.Fill };
    readonly Ellipse loupe = new() { Stroke = Brushes.White, StrokeThickness = 2 };
    readonly Rectangle centerBox = new() { Stroke = Brushes.Red, StrokeThickness = 1.5, IsHitTestVisible = false };
    readonly Border info = new() { Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x20, 0x20, 0x20)), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 4, 8, 4), IsHitTestVisible = false };
    readonly Rectangle swatch = new() { Width = 18, Height = 18, Stroke = Brushes.White, StrokeThickness = 1, Margin = new Thickness(0, 0, 8, 0) };
    readonly TextBlock text = new() { Foreground = Brushes.White, FontFamily = new FontFamily("Consolas"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };

    // Messen
    readonly Line lineShadow = new() { Stroke = Brushes.Black, StrokeThickness = 4, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly Line lineMain = new() { Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F)), StrokeThickness = 2, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly Ellipse dotA = new() { Width = 9, Height = 9, Fill = Brushes.Red, Stroke = Brushes.White, StrokeThickness = 1.5, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly Ellipse dotB = new() { Width = 9, Height = 9, Fill = Brushes.Red, Stroke = Brushes.White, StrokeThickness = 1.5, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly Border result = new() { Background = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xD5, 0x4F)), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 3, 8, 3), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly TextBlock resultText = new() { Foreground = new SolidColorBrush(Color.FromRgb(0x21, 0x21, 0x21)), FontFamily = new FontFamily("Consolas"), FontSize = 14, FontWeight = FontWeights.SemiBold };
    int sx, sy, ex, ey;
    int stage;            // 0 = nichts/fertig, 1 = Gummiband läuft
    bool hasLine;
    string measureText = "";

    readonly double size;
    int zoomIdx;

    /// <summary>Gewählter Farbwert wie #RRGGBB (nur Pipette).</summary>
    public string? Result { get; private set; }

    public PixelOverlay(BitmapSource shot, Drawing.Rectangle virtualScreen, PixelMode mode)
    {
        this.shot = shot; vs = virtualScreen; this.mode = mode;
        size = mode switch { PixelMode.Picker => 150, PixelMode.Magnifier => 260, _ => 200 };
        zoomIdx = Array.IndexOf(Zooms, mode switch { PixelMode.Picker => 10, PixelMode.Magnifier => 6, _ => 8 });

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
        result.Child = resultText;
        cv.Children.Add(lineShadow); cv.Children.Add(lineMain); cv.Children.Add(dotA); cv.Children.Add(dotB);
        cv.Children.Add(result);
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
        MouseLeftButtonDown += OnLeftDown;
        MouseLeftButtonUp += OnLeftUp;
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

    // ---------- Maus ----------
    void OnLeftDown(object s, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(cv);
        if (!PixelAt(p, out var c, out int px, out int py)) return;
        switch (mode)
        {
            case PixelMode.Picker:
                Result = Hex(c); Close();
                break;
            case PixelMode.Ruler:
                if (stage == 1)
                {
                    // zweiter Klick im Klick-Klick-Modus: Endpunkt setzen
                    (ex, ey) = Snap(px, py);
                    stage = 0; DrawMeasure();
                }
                else
                {
                    sx = ex = px; sy = ey = py;
                    stage = 1; hasLine = true;
                    moved = false;
                    CaptureMouse();
                    DrawMeasure();
                }
                break;
        }
    }

    bool moved;

    void OnLeftUp(object s, MouseButtonEventArgs e)
    {
        if (mode != PixelMode.Ruler) return;
        ReleaseMouseCapture();
        if (stage == 1 && moved)
        {
            // Ziehen beendet: Endpunkt festlegen
            if (PixelAt(e.GetPosition(cv), out _, out int px, out int py)) (ex, ey) = Snap(px, py);
            stage = 0; DrawMeasure();
        }
        // ohne Bewegung: Gummiband bleibt aktiv, nächster Klick setzt den Endpunkt
    }

    (int, int) Snap(int px, int py)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (Math.Abs(px - sx) >= Math.Abs(py - sy)) py = sy; else px = sx;
        }
        return (px, py);
    }

    // ---------- Tastatur ----------
    void OnKey(object s, KeyEventArgs e)
    {
        int dx = 0, dy = 0;
        switch (e.Key)
        {
            case Key.Escape:
                if (mode == PixelMode.Ruler && hasLine) { hasLine = false; stage = 0; DrawMeasure(); e.Handled = true; return; }
                Close(); return;
            case Key.Left: dx = -1; break;
            case Key.Right: dx = 1; break;
            case Key.Up: dy = -1; break;
            case Key.Down: dy = 1; break;
            case Key.Enter when mode == PixelMode.Picker:
                if (Native.GetCursorPos(out var cp) && PixelAt(PointFromScreen(new Point(cp.X, cp.Y)), out var c, out _, out _)) { Result = Hex(c); Close(); }
                return;
            case Key.Enter when mode == PixelMode.Ruler:
                // Enter setzt wie ein Klick (praktisch bei Feinjustierung mit den Pfeiltasten)
                if (Native.GetCursorPos(out var cp2) && PixelAt(PointFromScreen(new Point(cp2.X, cp2.Y)), out _, out int qx, out int qy))
                {
                    if (stage == 1) { (ex, ey) = Snap(qx, qy); stage = 0; }
                    else { sx = ex = qx; sy = ey = qy; stage = 1; hasLine = true; moved = true; }
                    DrawMeasure();
                }
                e.Handled = true; return;
            case Key.C when mode == PixelMode.Ruler && Keyboard.Modifiers == ModifierKeys.Control && hasLine:
                try { Clipboard.SetText(measureText); } catch { }
                e.Handled = true; return;
            default: return;
        }
        if (Native.GetCursorPos(out var p)) Native.SetCursorPos(p.X + dx, p.Y + dy);
        e.Handled = true;
    }

    // ---------- Pixel & Anzeige ----------
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

    void DrawMeasure()
    {
        var vis = hasLine ? Visibility.Visible : Visibility.Collapsed;
        lineShadow.Visibility = lineMain.Visibility = dotA.Visibility = dotB.Visibility = result.Visibility = vis;
        if (!hasLine) return;

        double kx = shot.PixelWidth / cv.ActualWidth, ky = shot.PixelHeight / cv.ActualHeight;
        double x1 = (sx + 0.5) / kx, y1 = (sy + 0.5) / ky, x2 = (ex + 0.5) / kx, y2 = (ey + 0.5) / ky;
        foreach (var l in new[] { lineShadow, lineMain }) { l.X1 = x1; l.Y1 = y1; l.X2 = x2; l.Y2 = y2; }
        Canvas.SetLeft(dotA, x1 - 4.5); Canvas.SetTop(dotA, y1 - 4.5);
        Canvas.SetLeft(dotB, x2 - 4.5); Canvas.SetTop(dotB, y2 - 4.5);

        int dx = Math.Abs(ex - sx), dy = Math.Abs(ey - sy);
        double len = Math.Sqrt((double)dx * dx + (double)dy * dy);
        measureText = $"Δx {dx} px   Δy {dy} px   Länge {len:0.0} px".Replace('.', ',');
        resultText.Text = measureText;
        result.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double rw = result.DesiredSize.Width, rh = result.DesiredSize.Height;
        double lx = x2 + 14, ly = y2 - rh - 14;
        if (lx + rw > cv.ActualWidth) lx = x2 - rw - 14;
        if (ly < 0) ly = y2 + 14;
        Canvas.SetLeft(result, lx); Canvas.SetTop(result, ly);
    }

    void UpdateAt(Point p)
    {
        if (cv.ActualWidth <= 0 || !PixelAt(p, out var c, out int px, out int py)) return;

        if (mode == PixelMode.Ruler && stage == 1)
        {
            if (px != sx || py != sy) moved = true;
            (ex, ey) = Snap(px, py);
            DrawMeasure();
        }

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
        text.Text = mode switch
        {
            PixelMode.Picker => $"{Hex(c)}   {c.R},{c.G},{c.B}   x{px} y{py}",
            PixelMode.Ruler => $"x {px}  y {py}   ·   Klick = Start, Ziehen/Klick = Ende",
            _ => $"{zoom}×   x{px} y{py}",
        };
        Canvas.SetLeft(info, lx); Canvas.SetTop(info, ly + size + 6);
    }
}
