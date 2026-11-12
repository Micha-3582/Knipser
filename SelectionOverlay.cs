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

    public SelectionOverlay(BitmapSource shot, Drawing.Rectangle virtualScreen)
    {
        this.shot = shot;
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
        var grid = new Grid();
        grid.Children.Add(img);
        grid.Children.Add(cv);
        Content = grid;

        SourceInitialized += (_, _) => Place();
        Loaded += (_, _) => { Place(); Activate(); Focus(); UpdateDim(); };
        ContentRendered += (_, _) => { Place(); Native.SetForegroundWindow(new WindowInteropHelper(this).Handle); };
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
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
        double w = cv.ActualWidth, h = cv.ActualHeight;
        Geometry full = new RectangleGeometry(new Rect(0, 0, w, h));
        dim.Data = start.HasValue && sel.Width > 0 && sel.Height > 0
            ? new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(sel))
            : full;
    }

    void OnDown(object s, MouseButtonEventArgs e)
    {
        start = e.GetPosition(cv);
        sel = new Rect(start.Value, new Size(0, 0));
        CaptureMouse();
    }

    void OnMove(object s, MouseEventArgs e)
    {
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
