using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Drawing = System.Drawing;

namespace Knipser;

/// <summary>Vollbild-Fenster über allen Monitoren: eingefrorener Screenshot, Bereich aufziehen.</summary>
internal class SelectionOverlay : Window
{
    readonly BitmapSource shot;
    readonly Drawing.Rectangle vs;
    readonly Canvas cv = new();
    readonly Image img = new() { Stretch = Stretch.Fill };
    readonly Path dim = new() { Fill = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), IsHitTestVisible = false };
    readonly Rectangle border = new() { Stroke = Brushes.White, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly Border label = new() { Background = new SolidColorBrush(Color.FromArgb(0xDD, 0x20, 0x20, 0x20)), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 2, 6, 2), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly TextBlock labelText = new() { Foreground = Brushes.White, FontSize = 12 };
    Point? start;
    Rect sel;

    public Int32Rect? Result { get; private set; }
    /// <summary>Freihand-Auswahl: Umriss in Bildschirm-Pixeln (relativ zum Screenshot).</summary>
    public List<Point>? FreePoints { get; private set; }
    readonly Size? fixedPx;
    readonly bool freehand;
    readonly bool dimOn;
    readonly Polyline freeLine = new() { Stroke = Brushes.White, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false };
    readonly List<Point> free = new();

    public SelectionOverlay(BitmapSource shot, Drawing.Rectangle virtualScreen, Size? fixedPixelSize = null, bool freehand = false, bool dimOutside = true)
    {
        this.shot = shot;
        fixedPx = fixedPixelSize; this.freehand = freehand; dimOn = dimOutside;
        vs = virtualScreen;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = vs.X; Top = vs.Y; Width = 200; Height = 200;
        Cursor = Cursors.Cross;
        Background = Brushes.Black;

        img.Source = shot;
        label.Child = labelText;
        cv.Children.Add(dim);
        cv.Children.Add(border);
        cv.Children.Add(label);
        cv.Children.Add(freeLine);
        var grid = new Grid();
        grid.Children.Add(img);
        grid.Children.Add(cv);
        Content = grid;

        SourceInitialized += (_, _) => Place();
        Loaded += (_, _) => { Place(); Activate(); Focus(); UpdateDim(); };
        ContentRendered += (_, _) => { Place(); Native.SetForegroundWindow(new WindowInteropHelper(this).Handle); };
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseWheel += OnWheel;
        MouseLeftButtonUp += OnUp;
        MouseRightButtonDown += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    void Place()
    {
        var h = new WindowInteropHelper(this).Handle;
        Native.SetWindowPos(h, Native.HWND_TOPMOST, vs.X, vs.Y, vs.Width, vs.Height, Native.SWP_SHOWWINDOW);
    }

    void UpdateDim()
    {
        if (!dimOn) { dim.Data = null; return; }
        double w = cv.ActualWidth, h = cv.ActualHeight;
        Geometry full = new RectangleGeometry(new Rect(0, 0, w, h));
        dim.Data = (start.HasValue || fixedPx.HasValue) && !freehand && sel.Width > 0 && sel.Height > 0
            ? new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(sel))
            : full;
    }

    void OnDown(object s, MouseButtonEventArgs e)
    {
        if (fixedPx.HasValue) { MoveFixed(e.GetPosition(cv)); FinishSel(); return; }
        if (freehand)
        {
            start = e.GetPosition(cv);
            free.Clear(); free.Add(start.Value);
            freeLine.Points = new PointCollection(free);
            CaptureMouse();
            return;
        }
        start = e.GetPosition(cv);
        sel = new Rect(start.Value, new Size(0, 0));
        CaptureMouse();
    }

    // Lupe am Mauszeiger (pixelgenaues Ansetzen)
    static readonly int[] Zooms = { 4, 6, 8, 10, 12, 16, 20 };
    int zoomIdx = 2;
    const double LoupeSize = 150;
    readonly Ellipse loupe = new() { Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false, Visibility = Visibility.Collapsed, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 0, Opacity = 0.8 } };
    readonly Rectangle centerBox = new() { Stroke = Brushes.Red, StrokeThickness = 1.5, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly Border loupeInfo = new() { Background = new SolidColorBrush(Color.FromArgb(0xDD, 0x20, 0x20, 0x20)), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 2, 6, 2), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly TextBlock loupeText = new() { Foreground = Brushes.White, FontSize = 12 };
    // Fadenkreuz-Hilfslinien über den ganzen Bildschirm (schwarz unterlegt + weiß gestrichelt, auf jedem Hintergrund sichtbar)
    readonly Line[] guideDark = { new(), new() }, guideLight = { new(), new() };
    bool loupeInit;
    Point lastPos;

    void UpdateLoupe(Point p)
    {
        if (!loupeInit)
        {
            loupeInfo.Child = loupeText;
            foreach (var l in guideDark) { l.Stroke = new SolidColorBrush(Color.FromArgb(0xAA, 0, 0, 0)); l.StrokeThickness = 3; l.IsHitTestVisible = false; cv.Children.Add(l); }
            foreach (var l in guideLight) { l.Stroke = Brushes.White; l.StrokeThickness = 1; l.StrokeDashArray = new DoubleCollection { 4, 4 }; l.IsHitTestVisible = false; cv.Children.Add(l); }
            cv.Children.Add(loupe); cv.Children.Add(centerBox); cv.Children.Add(loupeInfo);
            loupe.Width = loupe.Height = LoupeSize;
            loupeInit = true;
        }
        lastPos = p;
        double kx = shot.PixelWidth / cv.ActualWidth, ky = shot.PixelHeight / cv.ActualHeight;
        int px = Math.Clamp((int)Math.Floor(p.X * kx), 0, shot.PixelWidth - 1);
        int py = Math.Clamp((int)Math.Floor(p.Y * ky), 0, shot.PixelHeight - 1);
        double gx = (px + 0.5) / kx, gy = (py + 0.5) / ky;
        foreach (var l in new[] { guideDark[0], guideLight[0] }) { l.X1 = 0; l.X2 = cv.ActualWidth; l.Y1 = l.Y2 = gy; }
        foreach (var l in new[] { guideDark[1], guideLight[1] }) { l.Y1 = 0; l.Y2 = cv.ActualHeight; l.X1 = l.X2 = gx; }
        int zoom = Zooms[zoomIdx];
        int n = (int)(LoupeSize / zoom) | 1;
        loupe.Fill = new ImageBrush(shot)
        {
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(px + 0.5 - n / 2.0, py + 0.5 - n / 2.0, n, n),
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(loupe, BitmapScalingMode.NearestNeighbor);

        double ih = 24;
        double lx = p.X + 24, ly = p.Y + 24;
        if (lx + LoupeSize > cv.ActualWidth) lx = p.X - 24 - LoupeSize;
        if (ly + LoupeSize + ih + 6 > cv.ActualHeight) ly = p.Y - 24 - LoupeSize - ih - 6;
        Canvas.SetLeft(loupe, lx); Canvas.SetTop(loupe, ly);
        double cell = LoupeSize / n;
        centerBox.Width = centerBox.Height = cell;
        Canvas.SetLeft(centerBox, lx + (LoupeSize - cell) / 2); Canvas.SetTop(centerBox, ly + (LoupeSize - cell) / 2);
        loupeText.Text = $"x {px}  y {py}   ·   {zoom}×";
        Canvas.SetLeft(loupeInfo, lx); Canvas.SetTop(loupeInfo, ly + LoupeSize + 6);
        loupe.Visibility = centerBox.Visibility = loupeInfo.Visibility = Visibility.Visible;
    }

    void OnWheel(object s, MouseWheelEventArgs e)
    {
        zoomIdx = Math.Clamp(zoomIdx + (e.Delta > 0 ? 1 : -1), 0, Zooms.Length - 1);
        UpdateLoupe(lastPos);
    }

    void MoveFixed(Point p)
    {
        double kx = shot.PixelWidth / cv.ActualWidth, ky = shot.PixelHeight / cv.ActualHeight;
        double w = Math.Min(fixedPx!.Value.Width / kx, cv.ActualWidth), h = Math.Min(fixedPx.Value.Height / ky, cv.ActualHeight);
        double x = Math.Clamp(p.X - w / 2, 0, cv.ActualWidth - w), y = Math.Clamp(p.Y - h / 2, 0, cv.ActualHeight - h);
        sel = new Rect(x, y, w, h);
        Canvas.SetLeft(border, sel.X); Canvas.SetTop(border, sel.Y);
        border.Width = sel.Width; border.Height = sel.Height; border.Visibility = Visibility.Visible;
        labelText.Text = $"{Math.Round(sel.Width * kx)} × {Math.Round(sel.Height * ky)}";
        label.Visibility = Visibility.Visible;
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double ly = sel.Y - label.DesiredSize.Height - 4;
        if (ly < 0) ly = sel.Y + 4;
        Canvas.SetLeft(label, sel.X); Canvas.SetTop(label, ly);
        UpdateDim();
    }

    void FinishSel()
    {
        double kx = shot.PixelWidth / cv.ActualWidth, ky = shot.PixelHeight / cv.ActualHeight;
        int x = (int)Math.Round(sel.X * kx), y = (int)Math.Round(sel.Y * ky);
        int w = (int)Math.Round(sel.Width * kx), h = (int)Math.Round(sel.Height * ky);
        x = Math.Max(0, x); y = Math.Max(0, y);
        w = Math.Min(w, shot.PixelWidth - x); h = Math.Min(h, shot.PixelHeight - y);
        if (w >= 3 && h >= 3) Result = new Int32Rect(x, y, w, h);
        Close();
    }

    void OnMove(object s, MouseEventArgs e)
    {
        UpdateLoupe(e.GetPosition(cv));
        if (fixedPx.HasValue) { MoveFixed(e.GetPosition(cv)); return; }
        if (freehand)
        {
            if (start.HasValue) { free.Add(e.GetPosition(cv)); freeLine.Points = new PointCollection(free); }
            return;
        }
        if (!start.HasValue) return;
        var p = e.GetPosition(cv);
        sel = new Rect(start.Value, p);
        Canvas.SetLeft(border, sel.X); Canvas.SetTop(border, sel.Y);
        border.Width = sel.Width; border.Height = sel.Height;
        border.Visibility = Visibility.Visible;
        double k = shot.PixelWidth / cv.ActualWidth;
        labelText.Text = $"{Math.Round(sel.Width * k)} × {Math.Round(sel.Height * k)}";
        label.Visibility = Visibility.Visible;
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double ly = sel.Y - label.DesiredSize.Height - 4;
        if (ly < 0) ly = sel.Y + 4;
        Canvas.SetLeft(label, sel.X); Canvas.SetTop(label, ly);
        UpdateDim();
    }

    void OnUp(object s, MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        if (fixedPx.HasValue) return;
        if (freehand)
        {
            if (start.HasValue && free.Count >= 3)
            {
                double fx = shot.PixelWidth / cv.ActualWidth, fy = shot.PixelHeight / cv.ActualHeight;
                var px = new List<Point>();
                foreach (var q in free) px.Add(new Point(Math.Clamp(q.X * fx, 0, shot.PixelWidth), Math.Clamp(q.Y * fy, 0, shot.PixelHeight)));
                double minX = double.MaxValue, minY = double.MaxValue, maxX = 0, maxY = 0;
                foreach (var q in px) { minX = Math.Min(minX, q.X); minY = Math.Min(minY, q.Y); maxX = Math.Max(maxX, q.X); maxY = Math.Max(maxY, q.Y); }
                int rx = (int)Math.Floor(minX), ry = (int)Math.Floor(minY);
                int rw = Math.Min((int)Math.Ceiling(maxX) - rx, shot.PixelWidth - rx), rh = Math.Min((int)Math.Ceiling(maxY) - ry, shot.PixelHeight - ry);
                if (rw >= 3 && rh >= 3) { Result = new Int32Rect(rx, ry, rw, rh); FreePoints = px; }
            }
            Close();
            return;
        }
        if (!start.HasValue) return;
        double kx = shot.PixelWidth / cv.ActualWidth, ky = shot.PixelHeight / cv.ActualHeight;
        int x = (int)Math.Round(sel.X * kx), y = (int)Math.Round(sel.Y * ky);
        int w = (int)Math.Round(sel.Width * kx), h = (int)Math.Round(sel.Height * ky);
        x = Math.Max(0, x); y = Math.Max(0, y);
        w = Math.Min(w, shot.PixelWidth - x); h = Math.Min(h, shot.PixelHeight - y);
        if (w >= 3 && h >= 3) Result = new Int32Rect(x, y, w, h);
        Close();
    }
}
