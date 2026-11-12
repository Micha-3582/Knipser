using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using Path = System.Windows.Shapes.Path;

namespace Knipser;

enum Tool { Pointer, Rect, Ellipse, Line, Arrow, Pen, Marker, Text, Number, Blur, Soften, Crop, Fill }

public partial class EditorWindow : Window
{
    readonly Settings settings;
    readonly Image baseImg = new() { Stretch = Stretch.Fill };
    BitmapSource bmp;

    Tool tool = Tool.Arrow;
    Color color = Color.FromRgb(0xE5, 0x39, 0x35);
    double thick = 4;
    int counter = 1;

    readonly Stack<(Action undo, Action redo)> undoStack = new();
    readonly Stack<(Action undo, Action redo)> redoStack = new();

    Point p0;
    UIElement? cur;
    bool drawing;
    UIElement? dragEl, selected;
    Point dragStart, dragOrig;
    bool shift;
    bool saved;   // seit dem letzten Speichern/Öffnen nichts mehr geändert (und gespeichert)

    readonly List<Border> swatches = new();

    public EditorWindow(BitmapSource image, Settings settings)
    {
        InitializeComponent();
        Icon = App.WindowIcon;
        this.settings = settings;
        bmp = image;
        Art.Children.Add(baseImg);
        SetBitmap(bmp);
        BuildToolbar();
        Loaded += (_, _) => { FitMinWidth(); FitZoom(); };
        PreviewKeyDown += OnKey;
        Closing += (_, _) =>
        {
            if (settings.SaveOnClose && !saved) QuickSave();
        };
    }

    // ---------- Aufbau Menüband (Ribbon) ----------
    static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));
    static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    static UIElement Glyph(string g, double size = 22) => new TextBlock { Text = g, FontFamily = IconFont, FontSize = size, Foreground = Ink, HorizontalAlignment = HorizontalAlignment.Center, Height = 26 };

    static UIElement Geo(string data, bool fill = false) => new Path
    {
        Data = Geometry.Parse(data), Stroke = Ink, StrokeThickness = 1.8, Fill = fill ? Ink : null, Width = 24, Height = 24,
        StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 1, 0, 1),
    };

    static UIElement NumberIcon()
    {
        var g = new Grid { Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 1, 0, 1) };
        g.Children.Add(new Ellipse { Stroke = Ink, StrokeThickness = 1.8, Margin = new Thickness(1) });
        g.Children.Add(new TextBlock { Text = "1", Foreground = Ink, FontWeight = FontWeights.Bold, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        return g;
    }

    static UIElement WithLabel(UIElement icon, string label)
    {
        var sp = new StackPanel { MinWidth = 44 };
        sp.Children.Add(icon);
        sp.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Foreground = Ink, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) });
        return sp;
    }

    Button BigBtn(UIElement icon, string label, RoutedEventHandler click, string? tip = null)
    {
        var b = new Button { Content = WithLabel(icon, label), Style = (Style)FindResource("RibbonBtn"), ToolTip = tip ?? label };
        b.Click += click;
        return b;
    }

    Button SmallBtn(string glyph, RoutedEventHandler click, string tip)
    {
        var b = new Button { Content = new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 15, Foreground = Ink }, Style = (Style)FindResource("RibbonBtn"), ToolTip = tip, Padding = new Thickness(4) };
        b.Click += click;
        return b;
    }

    static UIElement Group(string title, params UIElement[] items)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var i in items) row.Children.Add(i);
        var col = new StackPanel { Margin = new Thickness(6, 4, 6, 2) };
        col.Children.Add(new Border { Height = 62, Child = row });
        col.Children.Add(new TextBlock { Text = title, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) });
        var d = new DockPanel();
        var sep = new Border { Width = 1, Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)), Margin = new Thickness(0, 6, 0, 6) };
        DockPanel.SetDock(sep, Dock.Right);
        d.Children.Add(sep); d.Children.Add(col);
        return d;
    }

    StackPanel? startPanel;

    /// <summary>Fenster nie schmaler als das Menüband (Reiter Start) breit ist, damit alle Gruppen sichtbar bleiben.</summary>
    void FitMinWidth()
    {
        if (startPanel == null) return;
        startPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double chrome = Math.Max(0, ActualWidth - Ribbon.ActualWidth);
        MinWidth = Math.Ceiling(startPanel.DesiredSize.Width + chrome + 6);
        if (Width < MinWidth) Width = MinWidth;
    }

    // ---------- Formen-Galerie (Rahmen-Werkzeug) ----------
    static readonly string[] FormNames = { "Rechteck", "Abgerundetes Rechteck", "Dreieck", "Raute", "Sechseck", "Stern", "Sprechblase" };
    static readonly string[] FormIcons =
    {
        "M5,6 H19 V18 H5 Z",
        "M8,6 H16 A3,3 0 0 1 19,9 V15 A3,3 0 0 1 16,18 H8 A3,3 0 0 1 5,15 V9 A3,3 0 0 1 8,6 Z",
        "M12,5 L20,19 H4 Z",
        "M12,4 L20,12 L12,20 L4,12 Z",
        "M8,5 H16 L21,12 L16,19 H8 L3,12 Z",
        "M12,3 L14.6,9.2 L21.3,9.7 L16.2,14 L17.8,20.5 L12,17 L6.2,20.5 L7.8,14 L2.7,9.7 L9.4,9.2 Z",
        "M6,5 H18 Q20,5 20,7 V13 Q20,15 18,15 H11 L7,19 V15 H6 Q4,15 4,13 V7 Q4,5 6,5 Z",
    };
    // Umrisse im Einheitsquadrat 0..100 (werden beim Aufziehen auf die gewünschte Größe gestreckt)
    static readonly string[] FormGeos =
    {
        "", "",
        "M50,0 L100,100 L0,100 Z",
        "M50,0 L100,50 L50,100 L0,50 Z",
        "M25,0 L75,0 L100,50 L75,100 L25,100 L0,50 Z",
        "M50,0 L61.2,34.5 L97.6,34.5 L68.2,55.9 L79.4,90.5 L50,69.1 L20.6,90.5 L31.8,55.9 L2.4,34.5 L38.8,34.5 Z",
        "M10,0 H90 Q100,0 100,10 V70 Q100,80 90,80 H40 L22,100 L28,80 H10 Q0,80 0,70 V10 Q0,0 10,0 Z",
    };

    UIElement MakeFormShape()
    {
        int id = Math.Clamp(settings.RectShape, 0, FormNames.Length - 1);
        if (id == 0) return new Rectangle { Stroke = Brush(), StrokeThickness = thick, Fill = Brushes.Transparent, RadiusX = 2, RadiusY = 2 };
        if (id == 1) return new Rectangle { Stroke = Brush(), StrokeThickness = thick, Fill = Brushes.Transparent, RadiusX = 16, RadiusY = 16 };
        return new Path
        {
            Data = Geometry.Parse(FormGeos[id]), Stretch = Stretch.Fill, Stroke = Brush(), StrokeThickness = thick, Fill = Brushes.Transparent,
            StrokeLineJoin = PenLineJoin.Round, Tag = new ShapeKind { Id = id },
        };
    }

    void HookGalleryMenu(RadioButton rb)
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => { timer.Stop(); ShowGalleryMenu(rb); };
        rb.PreviewMouseLeftButtonDown += (_, e) =>
        {
            var pos = e.GetPosition(rb);
            if (pos.X > rb.ActualWidth - 22 && pos.Y < 38) { ShowGalleryMenu(rb); e.Handled = true; return; }
            timer.Start();
        };
        rb.PreviewMouseLeftButtonUp += (_, _) => timer.Stop();
        rb.MouseLeave += (_, _) => timer.Stop();
        rb.MouseRightButtonUp += (_, e) => { ShowGalleryMenu(rb); e.Handled = true; };
    }

    void ShowGalleryMenu(RadioButton target)
    {
        var pop = new Popup { PlacementTarget = target, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        var panel = new StackPanel();
        for (int i = 0; i < FormNames.Length; i++)
        {
            int id = i;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = id == settings.RectShape ? "\u2713" : "", Foreground = Ink, Width = 16, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(Geo(FormIcons[id]));
            row.Children.Add(new TextBlock { Text = FormNames[id], Foreground = Ink, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 10, 0), MinWidth = 150 });
            var b = new Button { Content = row, Style = (Style)FindResource("RibbonBtn"), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 3, 6, 3) };
            b.Click += (_, _) => { settings.RectShape = id; settings.Save(); target.IsChecked = true; pop.IsOpen = false; };
            panel.Children.Add(b);
        }
        pop.Child = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(3), Child = panel,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.5 },
        };
        pop.IsOpen = true;
    }

    // ---------- Bild als Element einfügen ----------
    void PasteImageElement(BitmapSource? src)
    {
        if (src == null) return;
        CommitText();
        double w = src.PixelWidth, h = src.PixelHeight;
        double k = Math.Min(1, Math.Min(bmp.PixelWidth * 0.7 / w, bmp.PixelHeight * 0.7 / h));
        int iw = (int)Math.Max(8, Math.Round(w * k)), ih = (int)Math.Max(8, Math.Round(h * k));
        var img = new Image { Source = src, Width = iw, Height = ih, Stretch = Stretch.Fill };
        Canvas.SetLeft(img, (int)((bmp.PixelWidth - iw) / 2.0)); Canvas.SetTop(img, (int)((bmp.PixelHeight - ih) / 2.0));
        Art.Children.Add(img);
        Push(() => Art.Children.Remove(img), () => Art.Children.Add(img));
        Select(img);
        toolButtons[Tool.Pointer].IsChecked = true;
    }

    void PasteFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsImage()) PasteImageElement(Clipboard.GetImage());
            else Status.Text = "In der Zwischenablage liegt kein Bild.";
        }
        catch (Exception ex) { Status.Text = "Einfügen fehlgeschlagen: " + ex.Message; }
    }

    void InsertImageFile()
    {
        var dlg = new OpenFileDialog { Filter = "Bilder (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Alle Dateien|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(dlg.FileName); bi.EndInit(); bi.Freeze();
            PasteImageElement(bi);
        }
        catch (Exception ex) { Status.Text = "Bild konnte nicht geladen werden: " + ex.Message; }
    }

    // ---------- Form-Untermenü für Verpixeln / Weichzeichnen ----------
    static readonly string[] ShapeNames = { "Rechteck", "Ellipse", "Freihand (Lasso)" };

    static UIElement ShapeIcon(int i) => i == 0 ? Geo("M5,6 H19 V18 H5 Z")
        : i == 1 ? Geo("M3,12 A9,6 0 1 0 21,12 A9,6 0 1 0 3,12")
        : Geo("M5,16 C3,8 9,4 14,6 C19,8 21,13 16,17 C13,20 8,18 9,13");

    static UIElement WithCorner(UIElement icon)
    {
        var g = new Grid { Width = 32, Height = 26, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 1, 0, 1) };
        g.Children.Add(icon);
        g.Children.Add(new Path { Data = Geometry.Parse("M0,6 L6,6 L6,0 Z"), Fill = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1) });
        return g;
    }

    int ShapeOf(Tool t) => t == Tool.Soften ? settings.SoftenShape : settings.BlurShape;

    void HookShapeMenu(RadioButton rb, Func<int> get, Action<int> set, Func<int> getEdge, Action<int> setEdge)
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => { timer.Stop(); ShowShapeMenu(rb, get, set, getEdge, setEdge); };
        rb.PreviewMouseLeftButtonDown += (_, e) =>
        {
            var pos = e.GetPosition(rb);
            if (pos.X > rb.ActualWidth - 22 && pos.Y < 38) { ShowShapeMenu(rb, get, set, getEdge, setEdge); e.Handled = true; return; }
            timer.Start();
        };
        rb.PreviewMouseLeftButtonUp += (_, _) => timer.Stop();
        rb.MouseLeave += (_, _) => timer.Stop();
        rb.MouseRightButtonUp += (_, e) => { ShowShapeMenu(rb, get, set, getEdge, setEdge); e.Handled = true; };
    }

    static readonly string[] EdgeNames = { "Harte Kante", "Weiche Kante" };

    void ShowShapeMenu(RadioButton target, Func<int> get, Action<int> set, Func<int> getEdge, Action<int> setEdge)
    {
        var pop = new Popup { PlacementTarget = target, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        var panel = new StackPanel();
        for (int i = 0; i < ShapeNames.Length; i++)
        {
            int st = i;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = st == get() ? "\u2713" : "", Foreground = Ink, Width = 16, VerticalAlignment = VerticalAlignment.Center });
            var ic = ShapeIcon(st);
            row.Children.Add(ic);
            row.Children.Add(new TextBlock { Text = ShapeNames[i], Foreground = Ink, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 10, 0), MinWidth = 100 });
            var b = new Button { Content = row, Style = (Style)FindResource("RibbonBtn"), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 3, 6, 3) };
            b.Click += (_, _) =>
            {
                set(st); settings.Save();
                target.IsChecked = true;
                pop.IsOpen = false;
            };
            panel.Children.Add(b);
        }
        panel.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), Margin = new Thickness(4, 4, 4, 4) });
        for (int i = 0; i < EdgeNames.Length; i++)
        {
            int ed = i;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = ed == Math.Min(getEdge(), 1) ? "\u2713" : "", Foreground = Ink, Width = 16, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = EdgeNames[ed], Foreground = Ink, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), MinWidth = 130 });
            var b = new Button { Content = row, Style = (Style)FindResource("RibbonBtn"), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 3, 6, 3) };
            b.Click += (_, _) =>
            {
                setEdge(ed); settings.Save();
                target.IsChecked = true;
                pop.IsOpen = false;
            };
            panel.Children.Add(b);
        }
        pop.Child = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(3), Child = panel,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.5 },
        };
        pop.IsOpen = true;
    }

    /// <summary>Weichheit der Kante in Pixeln (0 = hart), abhängig von der Größe der Form.</summary>
    static double FeatherPx(Rect shapeRect, int edge) =>
        edge <= 0 ? 0 : Math.Clamp(Math.Min(shapeRect.Width, shapeRect.Height) * 0.05, 3, 10);

    /// <summary>Form (Ellipse oder Freihand) als Deckkraftmaske auf das Bild-Element legen; skaliert beim Größe-Ändern mit.</summary>
    static void ApplyShapeMask(Image img, int shape, List<Point>? poly, Rect shapeRect, double feather)
    {
        if (shape == 0 && feather <= 0) return;
        int w = (int)img.Width, h = (int)img.Height;
        double ox = Canvas.GetLeft(img), oy = Canvas.GetTop(img);
        Geometry g;
        if (shape == 2)
        {
            if (poly == null || poly.Count < 3) return;
            var sg = new StreamGeometry();
            using (var c = sg.Open())
            {
                c.BeginFigure(new Point(poly[0].X - ox, poly[0].Y - oy), true, true);
                for (int i = 1; i < poly.Count; i++) c.LineTo(new Point(poly[i].X - ox, poly[i].Y - oy), true, false);
            }
            sg.Freeze();
            g = sg;
        }
        else
        {
            var lr = new Rect(shapeRect.X - ox, shapeRect.Y - oy, shapeRect.Width, shapeRect.Height);
            g = shape == 1 ? new EllipseGeometry(lr) : new RectangleGeometry(lr);
        }

        if (feather <= 0)
        {
            img.OpacityMask = new DrawingBrush(new GeometryDrawing(Brushes.Black, null, g))
            {
                Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, w, h),
            };
            return;
        }

        // weiche Kante: Form als Bild zeichnen, weichzeichnen, als Deckkraftmaske benutzen
        var dv = new DrawingVisual();
        // Form um die Weichheit nach außen verbreitern, dann weichzeichnen: innen bleibt der Effekt voll, nur außen läuft er aus
        var grow = new Pen(Brushes.Black, feather * 2) { LineJoin = PenLineJoin.Round };
        using (var dc = dv.RenderOpen()) dc.DrawGeometry(Brushes.Black, grow, g);
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var px = new byte[w * h * 4];
        rtb.CopyPixels(px, w * 4, 0);
        BlurBgra(px, w, h, Math.Max(1, (int)Math.Round(feather * 0.6)));
        var mask = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, w * 4);
        mask.Freeze();
        img.OpacityMask = new ImageBrush(mask) { Stretch = Stretch.Fill };
    }

    // ---------- Pipette ----------
    /// <summary>Pipetten-Symbol (Tropfer mit Gummikappe, Glasröhrchen und Tropfen), schräg gestellt.</summary>
    static UIElement PipetteIcon(double size = 24)
    {
        var rot = new RotateTransform(40, 20, 20);
        Path Make(string data, bool fill)
        {
            var g = Geometry.Parse(data).Clone();
            g.Transform = rot;
            return new Path
            {
                Data = g, Fill = fill ? Ink : null, Stroke = Ink, StrokeThickness = fill ? 0.6 : 2.4,
                StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            };
        }
        var grid = new Grid { Width = 40, Height = 40 };
        grid.Children.Add(Make("M15.5,8 V6 A3.5,3.5 0 0 1 19,2.5 H21 A3.5,3.5 0 0 1 24.5,6 V8 Z M13.5,8 H26.5 V11 H13.5 Z M16,20 H24 V27 L20,32.5 L16,27 Z M20,34.5 C18.3,36.8 18.3,38.5 20,38.5 C21.7,38.5 21.7,36.8 20,34.5 Z", true));
        grid.Children.Add(Make("M16,11 V27 L20,32.5 L24,27 V11", false));
        return new Viewbox { Width = size, Height = size, Child = grid, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, (26 - size) / 2, 0, (26 - size) / 2) };
    }

    // ---------- Lupe für die Pipette ----------
    const double LoupeSize = 150;
    readonly Ellipse loupeCircle = new() { Width = LoupeSize, Height = LoupeSize, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false, Visibility = Visibility.Collapsed, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 0, Opacity = 0.8 } };
    readonly Rectangle loupeBox = new() { Stroke = Brushes.Red, StrokeThickness = 1.5, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly Border loupeInfo = new() { Background = new SolidColorBrush(Color.FromArgb(0xDD, 0x20, 0x20, 0x20)), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 2, 6, 2), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly TextBlock loupeText = new() { Foreground = Brushes.White, FontSize = 12 };
    bool loupeInit;
    Point lastLoupeImg, lastLoupeLayer;
    static readonly int[] LoupeSteps = { 7, 9, 11, 15, 21, 31, 45, 61 };

    void ShowLoupe(Point pImg, Point pLayer, Color? c)
    {
        if (!loupeInit)
        {
            loupeInfo.Child = loupeText;
            LoupeLayer.Children.Add(loupeCircle); LoupeLayer.Children.Add(loupeBox); LoupeLayer.Children.Add(loupeInfo);
            loupeInit = true;
        }
        lastLoupeImg = pImg; lastLoupeLayer = pLayer;
        int px = (int)Math.Floor(pImg.X), py = (int)Math.Floor(pImg.Y);
        if (px < 0 || py < 0 || px >= bmp.PixelWidth || py >= bmp.PixelHeight) { HideLoupe(); return; }
        int sample = Math.Max(1, settings.PickSample);
        int n = Math.Max(settings.LoupePixels, sample + 4) | 1;
        loupeCircle.Fill = new ImageBrush(bmp)
        {
            ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(px + 0.5 - n / 2.0, py + 0.5 - n / 2.0, n, n), Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(loupeCircle, BitmapScalingMode.NearestNeighbor);
        double lx = pLayer.X + 26, ly = pLayer.Y + 26;
        double lw = LoupeLayer.ActualWidth, lh = LoupeLayer.ActualHeight;
        if (lx + LoupeSize > lw || (cpWin != null && pLayer.X - 26 - LoupeSize >= 0)) lx = pLayer.X - 26 - LoupeSize;   // bei offenem Farbwähler links vom Zeiger
        if (ly + LoupeSize + 32 > lh) ly = pLayer.Y - 26 - LoupeSize - 32;
        Canvas.SetLeft(loupeCircle, lx); Canvas.SetTop(loupeCircle, ly);
        double cell = LoupeSize / n, box = Math.Min(LoupeSize - 4, cell * sample);
        loupeBox.Width = loupeBox.Height = box;
        Canvas.SetLeft(loupeBox, lx + (LoupeSize - box) / 2); Canvas.SetTop(loupeBox, ly + (LoupeSize - box) / 2);
        loupeText.Text = (c is Color cc ? $"#{cc.R:X2}{cc.G:X2}{cc.B:X2}   x {px}  y {py}" : $"x {px}  y {py}") + $"   {LoupeSize / n:0.#}×";
        Canvas.SetLeft(loupeInfo, lx); Canvas.SetTop(loupeInfo, ly + LoupeSize + 6);
        loupeCircle.Visibility = loupeBox.Visibility = loupeInfo.Visibility = Visibility.Visible;
    }

    void HideLoupe()
    {
        loupeCircle.Visibility = loupeBox.Visibility = loupeInfo.Visibility = Visibility.Collapsed;
    }

    bool picking;
    Border? colorBox;
    ColorPickerWindow? cpWin;
    Cursor? pipetteCursor;

    /// <summary>Farbe als aktuelle Farbe setzen (färbt auch das ausgewählte Element bzw. den gerade geschriebenen Text).</summary>
    void SetColor(Color c)
    {
        color = c;
        if (editBox != null) { editBox.Foreground = Brush(); editBox.CaretBrush = Brush(); }
        else if (selected != null) ApplyColor(selected, c);
        foreach (var sw in swatches) sw.BorderBrush = sw.Background is SolidColorBrush b && b.Color == c ? Brushes.White : Brushes.Transparent;
        if (colorBox != null) colorBox.Background = new SolidColorBrush(c);
    }

    /// <summary>Mauszeiger in Pipettenform (Spitze unten links als Klickpunkt).</summary>
    Cursor PipetteCursor()
    {
        if (pipetteCursor != null) return pipetteCursor;
        try
        {
            var rot = new RotateTransform(40, 20, 20);
            Path Make(string data, bool fill, Brush b, double thick)
            {
                var g = Geometry.Parse(data).Clone();
                g.Transform = rot;
                return new Path { Data = g, Fill = fill ? b : null, Stroke = b, StrokeThickness = thick, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            }
            const string solid = "M15.5,8 V6 A3.5,3.5 0 0 1 19,2.5 H21 A3.5,3.5 0 0 1 24.5,6 V8 Z M13.5,8 H26.5 V11 H13.5 Z M16,20 H24 V27 L20,32.5 L16,27 Z M20,34.5 C18.3,36.8 18.3,38.5 20,38.5 C21.7,38.5 21.7,36.8 20,34.5 Z";
            const string tube = "M16,11 V27 L20,32.5 L24,27 V11";
            var grid = new Grid { Width = 40, Height = 40 };
            grid.Children.Add(Make(solid, true, Brushes.Black, 4));
            grid.Children.Add(Make(tube, false, Brushes.Black, 5));
            grid.Children.Add(Make(solid, true, Brushes.White, 0.6));
            grid.Children.Add(Make(tube, false, Brushes.White, 2.2));
            grid.Measure(new Size(40, 40)); grid.Arrange(new Rect(0, 0, 40, 40));
            var rtb = new RenderTargetBitmap(32, 32, 96 * 0.8, 96 * 0.8, PixelFormats.Pbgra32);
            rtb.Render(grid);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var png = new MemoryStream();
            enc.Save(png);
            var data = png.ToArray();
            using var cur = new MemoryStream();
            using (var w = new BinaryWriter(cur, System.Text.Encoding.UTF8, true))
            {
                w.Write((short)0); w.Write((short)2); w.Write((short)1);       // ICONDIR: Typ Cursor, 1 Bild
                w.Write((byte)32); w.Write((byte)32); w.Write((byte)0); w.Write((byte)0);
                w.Write((short)9); w.Write((short)24);                          // Klickpunkt (Spitze)
                w.Write(data.Length); w.Write(22);
                w.Write(data);
            }
            cur.Position = 0;
            pipetteCursor = new Cursor(cur);
        }
        catch { pipetteCursor = Cursors.Cross; }
        return pipetteCursor!;
    }

    void ShowColorPicker()
    {
        CommitText();
        if (cpWin != null) { cpWin.Activate(); return; }
        cpWin = new ColorPickerWindow(color, settings) { Owner = this };
        cpWin.Applied += c => SetColor(c);
        cpWin.Closed += (_, _) =>
        {
            cpWin = null;
            HideLoupe();
            Art.Cursor = tool == Tool.Pointer ? Cursors.Arrow : Cursors.Cross;
            Status.Text = Hint;
        };
        Art.Cursor = PipetteCursor();
        Status.Text = "Farbwähler: ins Bild klicken, um eine Farbe aufzunehmen.";
        cpWin.Show();
    }

    static readonly int[] PickSizes = { 1, 3, 5, 11, 31 };

    Button MakePipetteButton()
    {
        var btn = BigBtn(WithCorner(PipetteIcon(32)), "Pipette", (_, _) => StartPick(),
            "Pipette: Farbe direkt aus dem Bild aufnehmen (Esc = abbrechen). Ecke, langer Druck oder Rechtsklick: Aufnahmebereich");
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => { timer.Stop(); ShowPickMenu(btn); };
        btn.PreviewMouseLeftButtonDown += (_, e) =>
        {
            var pos = e.GetPosition(btn);
            if (pos.X > btn.ActualWidth - 22 && pos.Y < 38) { ShowPickMenu(btn); e.Handled = true; return; }
            timer.Start();
        };
        btn.PreviewMouseLeftButtonUp += (_, _) => timer.Stop();
        btn.MouseLeave += (_, _) => timer.Stop();
        btn.MouseRightButtonUp += (_, e) => { ShowPickMenu(btn); e.Handled = true; };
        return btn;
    }

    void ShowPickMenu(FrameworkElement target)
    {
        var pop = new Popup { PlacementTarget = target, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Pipette nimmt", Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)), FontSize = 11, Margin = new Thickness(8, 4, 8, 4) });
        foreach (int n in PickSizes)
        {
            int size = n;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = size == Math.Max(1, settings.PickSample) ? "\u2713" : "", Foreground = Ink, Width = 16, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = size == 1 ? "1 Pixel" : $"Durchschnitt {size} × {size} Pixel", Foreground = Ink, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), MinWidth = 160 });
            var b = new Button { Content = row, Style = (Style)FindResource("RibbonBtn"), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 3, 6, 3) };
            b.Click += (_, _) => { settings.PickSample = size; settings.Save(); cpWin?.RefreshSample(); pop.IsOpen = false; };
            panel.Children.Add(b);
        }
        pop.Child = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(3), Child = panel,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.5 },
        };
        pop.IsOpen = true;
    }

    void StartPick()
    {
        CommitText();
        picking = true;
        Art.Cursor = PipetteCursor();
        Status.Text = "Pipette: in das Bild klicken, um die Farbe zu übernehmen (Esc = abbrechen).";
    }

    void EndPick()
    {
        picking = false;
        HideLoupe();
        if (colorBox != null) colorBox.Background = new SolidColorBrush(color);
        Art.Cursor = cpWin != null ? PipetteCursor() : tool == Tool.Pointer ? Cursors.Arrow : Cursors.Cross;
        Status.Text = Hint;
    }

    Color? SampleAt(Point p)
    {
        int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y);
        if (x < 0 || y < 0 || x >= bmp.PixelWidth || y >= bmp.PixelHeight) return null;
        int n = Math.Max(1, settings.PickSample), half = n / 2;
        int x0 = Math.Max(0, x - half), y0 = Math.Max(0, y - half);
        int x1 = Math.Min(bmp.PixelWidth - 1, x + half), y1 = Math.Min(bmp.PixelHeight - 1, y + half);
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        try
        {
            var area = new FormatConvertedBitmap(new CroppedBitmap(bmp, new Int32Rect(x0, y0, w, h)), PixelFormats.Bgra32, null, 0);
            var px = new byte[w * h * 4];
            area.CopyPixels(px, w * 4, 0);
            long rs = 0, gs = 0, bs = 0;
            for (int i = 0; i < w * h; i++) { bs += px[i * 4]; gs += px[i * 4 + 1]; rs += px[i * 4 + 2]; }
            int cnt = w * h;
            return Color.FromRgb((byte)Math.Round(rs / (double)cnt), (byte)Math.Round(gs / (double)cnt), (byte)Math.Round(bs / (double)cnt));
        }
        catch { return null; }
    }

    // ---------- Pfeil-Untermenü (Ecke am Symbol, langer Druck oder Rechtsklick) ----------
    Path? arrowPrev;
    static readonly string[] ArrowNames = { "Klassisch", "Spitz", "Offen", "Modern", "Punkt" };
    static readonly int[] ArrowOrder = { 3, 0, 1, 2, 4 };   // Reihenfolge im Menü: Modern, Klassisch, Spitz, Offen, Punkt

    UIElement BuildArrowIcon()
    {
        var g = new Grid { Width = 32, Height = 26, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 1, 0, 1) };
        arrowPrev = new Path
        {
            Stroke = Ink, StrokeThickness = 1.8, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        };
        g.Children.Add(arrowPrev);
        g.Children.Add(new Path { Data = Geometry.Parse("M0,6 L6,6 L6,0 Z"), Fill = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1) });
        UpdateArrowIcon();
        return g;
    }

    void UpdateArrowIcon()
    {
        if (arrowPrev == null) return;
        int st = Math.Clamp(settings.ArrowStyle, 0, 4);
        arrowPrev.Data = ArrowGeo(new Point(3, 21), new Point(21, 3), true, st == 4 ? 0 : -1.5, st);
        arrowPrev.Fill = st == 2 ? null : Ink;
    }

    void HookArrowMenu(RadioButton rb)
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => { timer.Stop(); ShowArrowMenu(rb); };
        rb.PreviewMouseLeftButtonDown += (_, e) =>
        {
            var pos = e.GetPosition(rb);
            if (pos.X > rb.ActualWidth - 22 && pos.Y < 38) { ShowArrowMenu(rb); e.Handled = true; return; }   // kleine Ecke am Symbol
            timer.Start();
        };
        rb.PreviewMouseLeftButtonUp += (_, _) => timer.Stop();
        rb.MouseLeave += (_, _) => timer.Stop();
        rb.MouseRightButtonUp += (_, e) => { ShowArrowMenu(rb); e.Handled = true; };
    }

    void ShowArrowMenu(FrameworkElement target)
    {
        var pop = new Popup { PlacementTarget = target, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        var panel = new StackPanel();
        foreach (int st in ArrowOrder)
        {
            int i = st;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = st == settings.ArrowStyle ? "✓" : "", Foreground = Ink, Width = 16, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new Path
            {
                Data = ArrowGeo(new Point(4, 12), new Point(st == 4 ? 50 : 58, 12), true, 2.5, st), Stroke = Ink, StrokeThickness = 2.5, Fill = st == 2 ? null : Ink,
                StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Width = 64, Height = 24,
            });
            row.Children.Add(new TextBlock { Text = ArrowNames[i], Foreground = Ink, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0), MinWidth = 60 });
            var b = new Button { Content = row, Style = (Style)FindResource("RibbonBtn"), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 3, 6, 3) };
            if (st == settings.ArrowStyle) b.Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
            b.Click += (_, _) =>
            {
                settings.ArrowStyle = st; settings.Save();
                UpdateArrowIcon();
                toolButtons[Tool.Arrow].IsChecked = true;
                pop.IsOpen = false;
            };
            panel.Children.Add(b);
        }
        pop.Child = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(3), Child = panel,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.5 },
        };
        pop.IsOpen = true;
    }

    void BuildToolbar()
    {
        Art.MouseLeave += (_, _) =>
        {
            cpWin?.ClearPreview();
            PosText.Text = "";
            HideLoupe();
            if (picking && colorBox != null) colorBox.Background = new SolidColorBrush(color);
        };
        ZoomSlider.ValueChanged += (_, e) => { if (!zoomSync) SetZoom(e.NewValue / 100); };
        var panels = new Dictionary<string, StackPanel>();

        // --- Kopfzeile: Schnellzugriff + Reiter ---
        var head = new DockPanel { Margin = new Thickness(6, 3, 6, 0) };
        var quick = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 2), VerticalAlignment = VerticalAlignment.Center };
        quick.Children.Add(SmallBtn("\uE74E", (_, _) => QuickSave(), "Speichern"));
        quick.Children.Add(SmallBtn("\uE7A7", (_, _) => Undo(), "Rückgängig (Strg+Z)"));
        quick.Children.Add(SmallBtn("\uE7A6", (_, _) => Redo(), "Wiederholen (Strg+Y)"));
        DockPanel.SetDock(quick, Dock.Left);
        head.Children.Add(quick);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(tabs);
        Ribbon.Children.Add(head);

        var body = new Border { Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)), BorderThickness = new Thickness(0, 1, 0, 1), Height = 100 };
        var host = new Grid();
        body.Child = host;
        Ribbon.Children.Add(body);

        foreach (var name in new[] { "Datei", "Start", "Bild", "Ansicht" })
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
            panels[name] = panel;
            host.Children.Add(panel);
            var tb = new RadioButton { Content = name, GroupName = "tabs", Style = (Style)FindResource("RibbonTab"), IsChecked = name == "Start" };
            var n = name;
            tb.Checked += (_, _) => { foreach (var kv in panels) kv.Value.Visibility = kv.Key == n ? Visibility.Visible : Visibility.Collapsed; };
            tabs.Children.Add(tb);
        }
        panels["Start"].Visibility = Visibility.Visible;
        startPanel = panels["Start"];

        // --- Reiter Datei ---
        var fmtBox = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
        foreach (var f in new[] { "jpg", "png" })
        {
            var rb = new RadioButton { Content = f.ToUpperInvariant(), GroupName = "fmt", IsChecked = Ext == f, ToolTip = "Format beim Speichern", Margin = new Thickness(2, 1, 2, 1) };
            var ff = f;
            rb.Checked += (_, _) => { settings.ImageFormat = ff; settings.Save(); };
            fmtBox.Children.Add(rb);
        }
        panels["Datei"].Children.Add(Group("Speichern",
            BigBtn(Glyph("\uE74E"), "Speichern", (_, _) => QuickSave()),
            BigBtn(Glyph("\uE792"), "Speichern unter", (_, _) => SaveAs(), "Speichern unter…"),
            fmtBox));
        panels["Datei"].Children.Add(Group("Zwischenablage",
            BigBtn(Glyph("\uE8C8"), "Kopieren", (_, _) => CopyToClipboard()),
            BigBtn(Glyph("\uE77F"), "Einfügen", (_, _) => PasteFromClipboard(), "Bild aus der Zwischenablage als verschiebbares Element einfügen (Strg+V)"),
            BigBtn(Glyph("\uE8B9"), "Bilddatei", (_, _) => InsertImageFile(), "Bilddatei als verschiebbares Element einfügen"),
            BigBtn(Glyph("\uE749"), "Drucken", (_, _) => PrintImage())));
        panels["Datei"].Children.Add(Group("Programm",
            BigBtn(Glyph("\uE8A7"), "Öffnen mit", (_, _) => OpenExternal(), "Bild speichern und im externen Programm öffnen (Einstellungen → Speichern)"),
            BigBtn(Glyph("\uE713"), "Einstellungen", (_, _) => ((App)Application.Current).ShowSettings())));

        // --- Reiter Start ---
        cropApplyBtn = new Button { Content = "Zuschnitt anwenden", Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)), Visibility = Visibility.Collapsed, Margin = new Thickness(2, 1, 2, 1), Padding = new Thickness(8, 3, 8, 3) };
        cropApplyBtn.Click += (_, _) => ApplyCropNow();
        cropCancelBtn = new Button { Content = "Abbrechen", Visibility = Visibility.Collapsed, Margin = new Thickness(2, 1, 2, 1), Padding = new Thickness(8, 3, 8, 3) };
        cropCancelBtn.Click += (_, _) => CancelCrop();
        CropBar.Children.Add(cropApplyBtn); CropBar.Children.Add(cropCancelBtn);   // schweben über dem Bild: das Menüband bleibt ruhig

        (string label, Tool t, UIElement icon)[] tools =
        {
            ("Auswahl", Tool.Pointer, Geo("M6,3 L6,19 L10,15 L13,21 L15.5,20 L12.5,14 L18,14 Z", true)),
            ("Pfeil", Tool.Arrow, BuildArrowIcon()),
            ("Linie", Tool.Line, Geo("M4,20 L20,4")),
            ("Rahmen", Tool.Rect, WithCorner(Geo("M4,6 H20 V18 H4 Z"))),
            ("Ellipse", Tool.Ellipse, Geo("M3,12 A9,6 0 1 0 21,12 A9,6 0 1 0 3,12")),
            ("Stift", Tool.Pen, Geo("M3,17 C6,5 9,5 12,12 S18,19 21,7")),
            ("Marker", Tool.Marker, Glyph("\uE7E6")),
            ("Text", Tool.Text, Glyph("\uE8D2")),
            ("Nummer", Tool.Number, NumberIcon()),
            ("Verpixeln", Tool.Blur, WithCorner(Geo("M4,4 h6 v6 h-6 z M14,4 h6 v6 h-6 z M9,9 h6 v6 h-6 z M4,14 h6 v6 h-6 z M14,14 h6 v6 h-6 z"))),
            ("Weichzeichnen", Tool.Soften, WithCorner(Geo("M12,3 C16,8 19,11 19,14.5 A7,7 0 0 1 5,14.5 C5,11 8,8 12,3 Z"))),
            ("Zuschneiden", Tool.Crop, Glyph("\uE7A8")),
            ("Füllen", Tool.Fill, WithCorner(Geo("M5,12 L12,5 L18,11 L11,18 Z M18,13 C19.5,15 20,16 19,17 C18,18 17,17 17,16 C17,15 17.5,14 18,13 Z"))),
        };
        var toolGroup = new List<UIElement>();
        var bildGroup = new List<UIElement>();
        foreach (var (label, t, icon) in tools)
        {
            var rb = new RadioButton { Content = WithLabel(icon, label), GroupName = "tools", Style = (Style)FindResource("RibbonTool"), IsChecked = t == tool, ToolTip = label };
            var tt = t;
            rb.Checked += (_, _) =>
            {
                CommitText();
                if (cropMode && tt != Tool.Crop) EndCrop();
                tool = tt;
                Art.Cursor = tt == Tool.Pointer ? Cursors.Arrow : Cursors.Cross;
                if (tt == Tool.Crop) StartCrop();
            };
            toolButtons[t] = rb;
            if (t == Tool.Arrow) HookArrowMenu(rb);
            if (t == Tool.Rect) HookGalleryMenu(rb);
            if (t == Tool.Fill) HookListMenu(rb, FillNames, () => settings.FillMode, v => settings.FillMode = v);
            if (t == Tool.Blur) HookShapeMenu(rb, () => settings.BlurShape, v => settings.BlurShape = v, () => settings.BlurEdge, v => settings.BlurEdge = v);
            if (t == Tool.Soften) HookShapeMenu(rb, () => settings.SoftenShape, v => settings.SoftenShape = v, () => settings.SoftenEdge, v => settings.SoftenEdge = v);
            (t == Tool.Crop || t == Tool.Fill ? bildGroup : toolGroup).Add(rb);
        }

        panels["Start"].Children.Add(Group("Bild", bildGroup.ToArray()));
        panels["Bild"].Children.Add(Group("Größe",
            BigBtn(Geo("M5,19 L19,5 M19,5 H12 M19,5 V12 M5,19 H12 M5,19 V12"), "Größe", (_, _) => ResizeImage(), "Bildgröße ändern"),
            BigBtn(Geo("M6,12 A7,7 0 1 1 12,19 M6,12 L3,9 M6,12 L9,9"), "Links", (_, _) => RotateImage(270), "90° nach links drehen"),
            BigBtn(Geo("M18,12 A7,7 0 1 0 12,19 M18,12 L21,9 M18,12 L15,9"), "Rechts", (_, _) => RotateImage(90), "90° nach rechts drehen"),
            BigBtn(Geo("M12,3 V21 M8,7 L3,12 L8,17 Z M16,7 L21,12 L16,17 Z"), "Spiegeln ↔", (_, _) => FlipImage(true), "Horizontal kippen"),
            BigBtn(Geo("M3,12 H21 M7,8 L12,3 L17,8 Z M7,16 L12,21 L17,16 Z"), "Spiegeln ↕", (_, _) => FlipImage(false), "Vertikal kippen"),
            BigBtn(Geo("M8,8 H16 V16 H8 Z M3,3 H21 V21 H3 Z"), "Arbeitsbereich", (_, _) => ChangeCanvas(), "Arbeitsbereich (Leinwand) um einen Rand erweitern")));
        panels["Bild"].Children.Add(Group("Anpassen",
            BigBtn(Geo("M12,4 A8,8 0 1 0 12,20 A8,8 0 1 0 12,4 Z M12,4 V20 M12,4 A8,8 0 0 1 12,20 Z", true), "Hell/Kontrast", (_, _) => BrightnessContrast(), "Helligkeit und Kontrast"),
            BigBtn(Geo("M4,4 H20 V20 H4 Z M12,4 V20 M4,12 H12"), "Invertieren", (_, _) => ApplyPixelEffect(InvertFx), "Farben umkehren"),
            BigBtn(Geo("M12,4 L20,20 H4 Z M12,10 V15"), "Schärfen", (_, _) => ApplyPixelEffect(SharpenFx), "Bild schärfen"),
            BigBtn(Geo("M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 Z M12,3 V12 L19.8,16.5 M12,12 L4.2,16.5"), "Farbton", (_, _) => HueSaturation(), "Farbton, Sättigung und Helligkeit"),
            BigBtn(Geo("M4,7 H20 M4,12 H20 M4,17 H20 M8,5 V9 M15,10 V14 M10,15 V19"), "Farbbalance", (_, _) => ColorBalance(), "Rot-, Grün- und Blauanteil verschieben")));
        panels["Bild"].Children.Add(Group("Effekte",
            BigBtn(Geo("M5,5 H19 V19 H5 Z M9,9 H15 V15 H9 Z"), "Rahmen", (_, _) => ApplyImageFrame(), "Rahmen um das ganze Bild (Breite und Farbe wählbar)"),
            BigBtn(Geo("M4,4 H15 V15 H4 Z M18,7 V18 H7"), "Schatten", (_, _) => ApplyImageShadow(), "Schlagschatten um das ganze Bild (Abstand, Weichheit und Deckkraft wählbar)"),
            BigBtn(Geo("M12,4 A8,8 0 1 0 12,20 A8,8 0 1 0 12,4 Z M12,4 V20"), "Graustufen", (_, _) => ApplyGrayscale(), "Bild in Graustufen umwandeln"),
            BigBtn(Geo("M4,4 h4 v4 h-4 z M12,4 h4 v4 h-4 z M8,8 h4 v4 h-4 z M16,8 h4 v4 h-4 z M4,12 h4 v4 h-4 z M12,12 h4 v4 h-4 z M8,16 h4 v4 h-4 z M16,16 h4 v4 h-4 z"), "Auflösen", (_, _) => PixelateWhole(), "Ganzes Bild in Pixel auflösen"),
            BigBtn(Geo("M4,5 H20 V19 H4 Z M8,15 L12,8 L16,15 M9.5,12.5 H14.5"), "Wasserzeichen", (_, _) => Watermark(), "Text als Wasserzeichen in das Bild einrechnen")));
        panels["Bild"].Children.Add(Group("Element",
            BigBtn(Geo("M5,5 H15 V15 H5 Z M18,8 V18 H8"), "Schatten ein/aus", (_, _) => ToggleElementShadow(), "Schlagschatten für das ausgewählte Element ein- oder ausschalten")));
        panels["Start"].Children.Add(Group("Werkzeuge", toolGroup.ToArray()));

        panels["Start"].Children.Add(Group("Anordnen",
            BigBtn(Glyph(""), "Ebene hoch", (_, _) => Reorder(2), "Ausgewähltes Element eine Ebene nach vorne (Strg+#)"),
            BigBtn(Glyph(""), "Ebene runter", (_, _) => Reorder(3), "Ausgewähltes Element eine Ebene nach hinten (Strg+ß)")));

        var slider = new Slider { Minimum = 1, Maximum = 20, Value = thick, Width = 100, VerticalAlignment = VerticalAlignment.Center, IsSnapToTickEnabled = true, TickFrequency = 1, ToolTip = "Strichstärke bzw. Schriftgröße" };
        thickSlider = slider;
        slider.ValueChanged += (_, e) =>
        {
            if (syncing) return;
            if (editBox != null) { thick = e.NewValue; editBox.FontSize = 14 + thick * 2; return; }
            if (selected != null && ApplyThickness(selected, e.NewValue)) { UpdateSel(); return; }
            thick = e.NewValue;
        };
        panels["Start"].Children.Add(Group("Stärke", new Border { Padding = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Child = slider }));

        colorBox = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(color), BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 0),
        };
        panels["Start"].Children.Add(Group("Farben",
            MakePipetteButton(),
            BigBtn(colorBox, "Farbe", (_, _) => ShowColorPicker(), "Farbwähler öffnen: Vorgaben, Spektrum und eigene Farben")));

        // --- Reiter Ansicht ---
        panels["Ansicht"].Children.Add(Group("Zoom",
            BigBtn(Glyph("\uE71F"), "Verkleinern", (_, _) => SetZoom(Zoom.ScaleX / 1.25)),
            BigBtn(Glyph("\uE8A3"), "Vergrößern", (_, _) => SetZoom(Zoom.ScaleX * 1.25)),
            BigBtn(Glyph("\uE9A6"), "Anpassen", (_, _) => FitZoom(), "An Fenster anpassen"),
            BigBtn(new TextBlock { Text = "1:1", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Ink, HorizontalAlignment = HorizontalAlignment.Center, Height = 26, Padding = new Thickness(0, 3, 0, 0) }, "100 %", (_, _) => SetZoom(1), "Originalgröße")));
    }

    // ---------- Bild & Zoom ----------
    void SetBitmap(BitmapSource b)
    {
        bmp = b;
        baseImg.Source = b;
        baseImg.Width = b.PixelWidth; baseImg.Height = b.PixelHeight;
        Art.Width = b.PixelWidth; Art.Height = b.PixelHeight;
        Sel.Width = b.PixelWidth; Sel.Height = b.PixelHeight;
        Status.Text = Hint;
        SizeText.Text = $"{b.PixelWidth} × {b.PixelHeight} px";
    }

    const string Hint = "Elemente verschieben: Auswahl-Werkzeug oder Strg+Ziehen, Anfasser zum Ändern der Größe";
    bool zoomSync;

    void SetZoom(double z)
    {
        z = Math.Clamp(z, 0.1, 8);
        Zoom.ScaleX = Zoom.ScaleY = z;
        zoomSync = true; ZoomSlider.Value = Math.Clamp(z * 100, 10, 400); zoomSync = false;
        ZoomText.Text = $"{Math.Round(z * 100)} %";
        PositionSel();
    }

    void FitZoom()
    {
        double aw = Scroller.ActualWidth - 60, ah = Scroller.ActualHeight - 60;
        if (aw <= 0 || ah <= 0) return;
        SetZoom(Math.Min(1, Math.Min(aw / bmp.PixelWidth, ah / bmp.PixelHeight)));
    }

    void Scroller_Wheel(object sender, MouseWheelEventArgs e)
    {
        if ((picking || cpWin != null) && loupeCircle.Visibility == Visibility.Visible)
        {
            // Mausrad zoomt die Lupe der Pipette
            int idx = Array.IndexOf(LoupeSteps, settings.LoupePixels);
            if (idx < 0) idx = 3;
            idx = Math.Clamp(idx + (e.Delta > 0 ? -1 : 1), 0, LoupeSteps.Length - 1);
            settings.LoupePixels = LoupeSteps[idx]; settings.Save();
            ShowLoupe(lastLoupeImg, lastLoupeLayer, SampleAt(lastLoupeImg));
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        SetZoom(Zoom.ScaleX * (e.Delta > 0 ? 1.15 : 1 / 1.15));
        e.Handled = true;
    }

    // ---------- Hilfen ----------
    static TranslateTransform GetT(UIElement el)
    {
        if (el.RenderTransform is TranslateTransform t) return t;
        t = new TranslateTransform();
        el.RenderTransform = t;
        return t;
    }

    SolidColorBrush Brush(byte alpha = 255) => new(Color.FromArgb(alpha, color.R, color.G, color.B));

    void Push(Action undo, Action redo)
    {
        undoStack.Push((undo, redo));
        redoStack.Clear();
        saved = false;
    }

    void Undo()
    {
        CommitText();
        if (undoStack.Count == 0) return;
        var a = undoStack.Pop();
        a.undo();
        redoStack.Push(a);
        saved = false;
        RefreshSel();
    }

    void Redo()
    {
        if (redoStack.Count == 0) return;
        var a = redoStack.Pop();
        a.redo();
        undoStack.Push(a);
        saved = false;
        RefreshSel();
    }

    UIElement? FindEl(object src)
    {
        var d = src as DependencyObject;
        while (d != null)
        {
            if (d is UIElement ui && ReferenceEquals(VisualTreeHelper.GetParent(ui), Art) && !ReferenceEquals(ui, baseImg))
                return ui;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    // ---------- Zeichnen ----------
    void Art_Down(object sender, MouseButtonEventArgs e)
    {
        if (cpWin != null && !picking)
        {
            if (SampleAt(e.GetPosition(Art)) is Color wc) cpWin.SetNewColor(wc);
            e.Handled = true;
            return;
        }
        if (picking)
        {
            if (SampleAt(e.GetPosition(Art)) is Color pc) SetColor(pc);
            EndPick();
            e.Handled = true;
            return;
        }
        bool wasEditing = editBox != null;
        CommitText();
        if (wasEditing && tool == Tool.Text) toolButtons[Tool.Pointer].IsChecked = true;   // Klick neben den Text: Text fertig, Auswahl aktiv
        Art.Focus();
        p0 = e.GetPosition(Art);
        shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (cropMode) { CropDown(p0); return; }

        var hit = FindEl(e.OriginalSource);
        if (tool == Tool.Pointer && e.ClickCount == 2 && hit is TextBlock tbHit) { StartEdit(tbHit); return; }
        if (tool == Tool.Pointer || Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            dragEl = hit;
            Select(hit);
            if (hit != null)
            {
                var t = GetT(hit);
                dragStart = p0; dragOrig = new Point(t.X, t.Y);
                Art.CaptureMouse();
            }
            return;
        }
        Select(null);
        if (tool == Tool.Fill) { DoFill(p0); return; }
        if (tool == Tool.Text) { AddText(p0); return; }
        if (tool == Tool.Number) { AddNumber(p0); return; }

        switch (tool)
        {
            case Tool.Rect:
                cur = MakeFormShape();
                break;
            case Tool.Ellipse:
                cur = new Ellipse { Stroke = Brush(), StrokeThickness = thick, Fill = Brushes.Transparent };
                break;
            case Tool.Marker:
                cur = new Rectangle { Fill = Brush(0x70) };
                break;
            case Tool.Blur:
            case Tool.Soften:
            {
                int shp = ShapeOf(tool);
                var dash = new DoubleCollection { 3, 3 };
                var tint = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255));
                if (shp == 2)
                {
                    var lasso = new Polyline { Stroke = Brushes.White, StrokeThickness = 1.5, StrokeDashArray = dash, Fill = tint, StrokeLineJoin = PenLineJoin.Round };
                    lasso.Points.Add(p0);
                    cur = lasso;
                }
                else if (shp == 1) cur = new Ellipse { Stroke = Brushes.White, StrokeThickness = 1, StrokeDashArray = dash, Fill = tint };
                else cur = new Rectangle { Stroke = Brushes.White, StrokeThickness = 1, StrokeDashArray = dash, Fill = tint };
                break;
            }
            case Tool.Crop:
                cur = new Rectangle { Stroke = Brushes.White, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 }, Fill = new SolidColorBrush(Color.FromArgb(0x33, 30, 136, 229)) };
                break;
            case Tool.Line:
            case Tool.Arrow:
                cur = new Path { Stroke = Brush(), StrokeThickness = thick, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Fill = tool == Tool.Arrow && settings.ArrowStyle != 2 ? Brush() : null };
                break;
            case Tool.Pen:
                var pl = new Polyline { Stroke = Brush(), StrokeThickness = thick, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                pl.Points.Add(p0);
                cur = pl;
                break;
        }
        if (cur == null) return;
        Art.Children.Add(cur);
        drawing = true;
        Art.CaptureMouse();
    }

    void Art_Move(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(Art);
        PosText.Text = $"{(int)Math.Floor(p.X)}, {(int)Math.Floor(p.Y)}";
        if (picking || cpWin != null)
        {
            ShowLoupe(p, e.GetPosition(LoupeLayer), SampleAt(p));
            if (SampleAt(p) is Color hc) Status.Text = cpWin != null ? $"Farbe unter dem Zeiger: #{hc.R:X2}{hc.G:X2}{hc.B:X2}   (Klick = in den Farbwähler übernehmen)" : $"Pipette: #{hc.R:X2}{hc.G:X2}{hc.B:X2}   (Klick = übernehmen, Esc = abbrechen)";
            return;
        }
        if (cropDrag >= 8) { CropMove(p); return; }
        if (dragEl != null)
        {
            var t = GetT(dragEl);
            t.X = dragOrig.X + p.X - dragStart.X;
            t.Y = dragOrig.Y + p.Y - dragStart.Y;
            PositionSel();
            return;
        }
        if (!drawing || cur == null) return;
        bool sh = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (cur)
        {
            case Polyline pl:
                pl.Points.Add(p);
                break;
            case Path path when path.Tag is not ShapeKind:
                if (sh) p = SnapAngle(p0, p);
                path.Tag = new LineInfo { A = p0, B = p, Arrow = tool == Tool.Arrow, Style = settings.ArrowStyle };
                path.Data = ArrowGeo(p0, p, tool == Tool.Arrow, path.StrokeThickness, settings.ArrowStyle);
                break;
            case Shape s:
                if (sh && tool != Tool.Crop)
                {
                    double d = Math.Max(Math.Abs(p.X - p0.X), Math.Abs(p.Y - p0.Y));
                    p = new Point(p0.X + Math.Sign(p.X - p0.X) * d, p0.Y + Math.Sign(p.Y - p0.Y) * d);
                }
                var r = new Rect(p0, p);
                Canvas.SetLeft(s, r.X); Canvas.SetTop(s, r.Y);
                s.Width = r.Width; s.Height = r.Height;
                break;
        }
    }

    void Art_Up(object sender, MouseButtonEventArgs e)
    {
        Art.ReleaseMouseCapture();
        if (cropDrag >= 8) { CropUp(); return; }
        if (dragEl != null)
        {
            var el = dragEl; var t = GetT(el);
            var from = dragOrig; var to = new Point(t.X, t.Y);
            dragEl = null;
            if (from != to)
                Push(() => { var tt = GetT(el); tt.X = from.X; tt.Y = from.Y; },
                     () => { var tt = GetT(el); tt.X = to.X; tt.Y = to.Y; });
            return;
        }
        if (!drawing || cur == null) return;
        drawing = false;
        var added = cur;
        cur = null;
        var p = e.GetPosition(Art);

        bool tiny = added switch
        {
            Polyline pl => pl.Points.Count < 2,
            Path => (p - p0).Length < 3,
            Shape s => s.Width < 3 || s.Height < 3 || double.IsNaN(s.Width),
            _ => false
        };
        if (tiny) { Art.Children.Remove(added); return; }

        if (tool == Tool.Crop && added is Shape cs)
        {
            Art.Children.Remove(added);
            ApplyCrop(new Rect(Canvas.GetLeft(cs), Canvas.GetTop(cs), cs.Width, cs.Height));
            return;
        }
        if ((tool == Tool.Blur || tool == Tool.Soften) && added is Polyline lassoLine)
        {
            Art.Children.Remove(added);
            var poly = new List<Point>(lassoLine.Points);
            double minX = double.MaxValue, minY = double.MaxValue, maxX = 0, maxY = 0;
            foreach (var q in poly) { minX = Math.Min(minX, q.X); minY = Math.Min(minY, q.Y); maxX = Math.Max(maxX, q.X); maxY = Math.Max(maxY, q.Y); }
            var box = new Rect(new Point(minX, minY), new Point(maxX, maxY));
            var lpx = tool == Tool.Soften ? MakeSoften(box, 2, poly) : MakePixelate(box, 2, poly);
            if (lpx == null) return;
            Art.Children.Add(lpx);
            Push(() => Art.Children.Remove(lpx), () => Art.Children.Add(lpx));
            Select(lpx);
            toolButtons[Tool.Pointer].IsChecked = true;
            return;
        }
        if ((tool == Tool.Blur || tool == Tool.Soften) && added is Shape bs)
        {
            Art.Children.Remove(added);
            var area = new Rect(Canvas.GetLeft(bs), Canvas.GetTop(bs), bs.Width, bs.Height);
            var px = tool == Tool.Soften ? MakeSoften(area, ShapeOf(tool), null) : MakePixelate(area, ShapeOf(tool), null);
            if (px == null) return;
            Art.Children.Add(px);
            Push(() => Art.Children.Remove(px), () => Art.Children.Add(px));
            Select(px);
            toolButtons[Tool.Pointer].IsChecked = true;
            return;
        }
        Push(() => Art.Children.Remove(added), () => Art.Children.Add(added));
        Select(added);
        toolButtons[Tool.Pointer].IsChecked = true;   // danach gleich wieder Auswahl: Element lässt sich verschieben/umfärben
    }

    // ---------- Reihenfolge (Ebenen) ----------
    /// <summary>Ausgewähltes Element in der Stapelreihenfolge verschieben. 0 = ganz vor, 1 = ganz zurück, 2 = eine Ebene vor, 3 = eine zurück.</summary>
    void Reorder(int mode)
    {
        CommitText();
        var el = selected;
        if (el == null || !Art.Children.Contains(el)) return;
        int from = Art.Children.IndexOf(el), count = Art.Children.Count;
        int first = Art.Children.IndexOf(baseImg) + 1;   // das Bild selbst bleibt immer ganz unten
        int to = mode switch
        {
            0 => count - 1,
            1 => first,
            2 => Math.Min(from + 1, count - 1),
            _ => Math.Max(from - 1, first),
        };
        if (to == from) return;
        void Move(int a, int b) { Art.Children.RemoveAt(a); Art.Children.Insert(b, el); UpdateSel(); }
        Move(from, to);
        Push(() => Move(to, from), () => Move(from, to));
    }

    // ---------- Auswahl & Anfasser ----------
    sealed class LineInfo { public Point A, B; public bool Arrow; public int Style; }

    Rectangle? selBox;
    readonly List<Rectangle> handles = new();
    int activeHandle = -1;
    Rect rectBefore;
    LineInfo? lineBefore;
    Rect textRect0; double textFs0;

    sealed class ShapeKind { public int Id; }

    static bool IsRectLike(UIElement e) => e is Rectangle || e is Ellipse || e is Image || e is Path { Tag: ShapeKind };

    static Rect LocalRect(UIElement e)
    {
        var f = (FrameworkElement)e;
        return new Rect(Canvas.GetLeft(e), Canvas.GetTop(e), f.Width, f.Height);
    }

    static Point[] Corners(Rect r) => new[] { r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft };

    /// <summary>Farbe eines vorhandenen Elements ändern (mit Rückgängig). false = Element hat keine Farbe.</summary>
    bool ApplyColor(UIElement el, Color c)
    {
        Action? undo = null, redo = null;
        SolidColorBrush Full() => new(c);
        switch (el)
        {
            case Path p when p.Tag is LineInfo:
            {
                var os = p.Stroke; var of = p.Fill; bool fill = of != null;
                undo = () => { p.Stroke = os; p.Fill = of; };
                redo = () => { p.Stroke = Full(); p.Fill = fill ? Full() : null; };
                break;
            }
            case Rectangle r when r.Stroke == null && r.Fill != null:   // Marker (halbtransparent)
            {
                var of = r.Fill;
                undo = () => r.Fill = of;
                redo = () => r.Fill = new SolidColorBrush(Color.FromArgb(0x70, c.R, c.G, c.B));
                break;
            }
            case Shape s when s.Stroke != null && (s.StrokeDashArray == null || s.StrokeDashArray.Count == 0):
            {
                var os = s.Stroke;
                undo = () => s.Stroke = os;
                redo = () => s.Stroke = Full();
                break;
            }
            case TextBlock tb:
            {
                var of = tb.Foreground;
                undo = () => tb.Foreground = of;
                redo = () => tb.Foreground = Full();
                break;
            }
            case Grid g when g.Children.Count == 2 && g.Children[0] is Ellipse e:   // Nummer
            {
                var of = e.Fill;
                undo = () => e.Fill = of;
                redo = () => e.Fill = Full();
                break;
            }
        }
        if (redo == null || undo == null) return false;
        redo();
        Push(undo, redo);
        return true;
    }

    Slider? thickSlider;
    bool syncing;

    /// <summary>Stärke eines vorhandenen Elements ändern (Linienstärke bzw. Schriftgröße). false = Element hat keine Stärke.</summary>
    bool ApplyThickness(UIElement el, double v)
    {
        switch (el)
        {
            case Path p when p.Tag is LineInfo li:
                p.StrokeThickness = v; p.Data = ArrowGeo(li.A, li.B, li.Arrow, v, li.Style); return true;
            case Shape s when s.Stroke != null && (s.StrokeDashArray == null || s.StrokeDashArray.Count == 0) && s is Rectangle or Ellipse or Polyline or Path:
                s.StrokeThickness = v; return true;
            case TextBlock tb:
                tb.FontSize = 14 + v * 2; return true;
        }
        return false;
    }

    static double? ThicknessOf(UIElement el) => el switch
    {
        Path p when p.Tag is LineInfo => p.StrokeThickness,
        Shape s when s.Stroke != null && (s.StrokeDashArray == null || s.StrokeDashArray.Count == 0) && s is Rectangle or Ellipse or Polyline or Path => s.StrokeThickness,
        TextBlock tb => (tb.FontSize - 14) / 2,
        _ => null,
    };

    void Select(UIElement? el)
    {
        selected = el;
        if (el != null && thickSlider != null && ThicknessOf(el) is double t)
        {
            syncing = true;
            thickSlider.Value = Math.Clamp(Math.Round(t), 1, 20);
            syncing = false;
        }
        UpdateSel();
    }

    /// <summary>Nach Rückgängig/Wiederholen/Zuschneiden: Auswahl prüfen und neu zeichnen.</summary>
    void RefreshSel()
    {
        if (selected != null && !Art.Children.Contains(selected)) selected = null;
        UpdateSel();
    }

    void UpdateSel()
    {
        if (cropMode) return;
        Sel.Children.Clear();
        handles.Clear();
        selBox = null;
        if (selected == null) return;
        Art.UpdateLayout();
        selBox = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5)),
            StrokeDashArray = new DoubleCollection { 3, 3 }, IsHitTestVisible = false,
        };
        Sel.Children.Add(selBox);
        int n = IsRectLike(selected) || selected is TextBlock ? 4 : selected is Path { Tag: LineInfo } ? 2 : 0;
        for (int i = 0; i < n; i++)
        {
            var h = MakeHandle(i);
            handles.Add(h);
            Sel.Children.Add(h);
        }
        PositionSel();
    }

    Rectangle MakeHandle(int idx)
    {
        var h = new Rectangle
        {
            Fill = Brushes.White, Stroke = new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5)), Cursor = Cursors.SizeAll,
        };
        h.MouseLeftButtonDown += (_, e) =>
        {
            if (selected == null) return;
            activeHandle = idx;
            if (IsRectLike(selected)) rectBefore = LocalRect(selected);
            else if (selected is TextBlock stb) { textRect0 = new Rect(Canvas.GetLeft(stb), Canvas.GetTop(stb), stb.ActualWidth, stb.ActualHeight); textFs0 = stb.FontSize; }
            else if (selected is Path { Tag: LineInfo li }) lineBefore = new LineInfo { A = li.A, B = li.B, Arrow = li.Arrow, Style = li.Style };
            h.CaptureMouse();
            e.Handled = true;
        };
        h.MouseMove += (_, e) =>
        {
            if (activeHandle != idx || !h.IsMouseCaptured) return;
            DragHandle(idx, e.GetPosition(Art));
        };
        h.MouseLeftButtonUp += (_, _) =>
        {
            if (activeHandle != idx) return;
            h.ReleaseMouseCapture();
            activeHandle = -1;
            EndHandle();
        };
        return h;
    }

    void PositionSel()
    {
        if (cropMode) { PositionCrop(); return; }
        if (selected == null || selBox == null) return;
        double z = Zoom.ScaleX, hs = 10 / z;
        Rect b;
        try { b = selected.TransformToVisual(Art).TransformBounds(VisualTreeHelper.GetDescendantBounds(selected)); }
        catch { return; }
        if (b.IsEmpty) return;
        b.Inflate(3 / z, 3 / z);
        Canvas.SetLeft(selBox, b.X); Canvas.SetTop(selBox, b.Y);
        selBox.Width = b.Width; selBox.Height = b.Height; selBox.StrokeThickness = 1 / z;

        var t = GetT(selected);
        Point[]? pts = null;
        if (IsRectLike(selected)) pts = Corners(LocalRect(selected));
        else if (selected is TextBlock ptb) pts = Corners(new Rect(Canvas.GetLeft(ptb), Canvas.GetTop(ptb), ptb.ActualWidth, ptb.ActualHeight));
        else if (selected is Path { Tag: LineInfo li }) pts = new[] { li.A, li.B };
        if (pts == null) return;
        for (int i = 0; i < handles.Count && i < pts.Length; i++)
        {
            var h = handles[i];
            h.Width = h.Height = hs; h.StrokeThickness = 1.5 / z;
            Canvas.SetLeft(h, pts[i].X + t.X - hs / 2);
            Canvas.SetTop(h, pts[i].Y + t.Y - hs / 2);
        }
    }

    void DragHandle(int i, Point p)
    {
        if (selected == null) return;
        var t = GetT(selected);
        var lp = new Point(p.X - t.X, p.Y - t.Y);
        if (IsRectLike(selected))
        {
            var opp = Corners(rectBefore)[(i + 2) % 4];
            var nr = new Rect(lp, opp);
            if (nr.Width < 4 || nr.Height < 4) return;
            SetRect(selected, nr);
        }
        else if (selected is TextBlock tb)
        {
            // Schrift proportional mit den Eckanfassern vergrößern/verkleinern (gegenüberliegende Ecke bleibt stehen)
            var r0 = textRect0; int o = (i + 2) % 4; var opp = Corners(r0)[o];
            double sc = Math.Max(Math.Abs(lp.X - opp.X) / Math.Max(r0.Width, 1), Math.Abs(lp.Y - opp.Y) / Math.Max(r0.Height, 1));
            tb.FontSize = Math.Clamp(textFs0 * Math.Max(sc, 0.05), 8, 1000);
            tb.UpdateLayout();
            Canvas.SetLeft(tb, (o == 0 || o == 3) ? opp.X : opp.X - tb.ActualWidth);
            Canvas.SetTop(tb, (o == 0 || o == 1) ? opp.Y : opp.Y - tb.ActualHeight);
            PositionSel();
        }
        else if (selected is Path path && path.Tag is LineInfo li)
        {
            if (i == 0) li.A = lp; else li.B = lp;
            path.Data = ArrowGeo(li.A, li.B, li.Arrow, path.StrokeThickness, li.Style);
            PositionSel();
        }
    }

    void SetRect(UIElement el, Rect r)
    {
        var f = (FrameworkElement)el;
        Canvas.SetLeft(el, r.X); Canvas.SetTop(el, r.Y);
        f.Width = r.Width; f.Height = r.Height;
        PositionSel();
    }

    void SetLine(Path path, LineInfo info)
    {
        path.Tag = new LineInfo { A = info.A, B = info.B, Arrow = info.Arrow, Style = info.Style };
        path.Data = ArrowGeo(info.A, info.B, info.Arrow, path.StrokeThickness, info.Style);
        PositionSel();
    }

    void EndHandle()
    {
        var el = selected;
        if (el == null) return;
        if (IsRectLike(el))
        {
            var before = rectBefore; var after = LocalRect(el);
            if (before != after) Push(() => SetRect(el, before), () => SetRect(el, after));
        }
        else if (el is TextBlock tb)
        {
            var fs0 = textFs0; var l0 = textRect0.X; var t0 = textRect0.Y;
            double fs1 = tb.FontSize, l1 = Canvas.GetLeft(tb), t1 = Canvas.GetTop(tb);
            void Set(double fs, double l, double t) { tb.FontSize = fs; Canvas.SetLeft(tb, l); Canvas.SetTop(tb, t); tb.UpdateLayout(); PositionSel(); }
            if (fs0 != fs1) Push(() => Set(fs0, l0, t0), () => Set(fs1, l1, t1));
        }
        else if (el is Path path && path.Tag is LineInfo li && lineBefore != null)
        {
            var b = lineBefore;
            var a = new LineInfo { A = li.A, B = li.B, Arrow = li.Arrow, Style = li.Style };
            Push(() => SetLine(path, b), () => SetLine(path, a));
        }
    }

    Point SnapAngle(Point a, Point b)
    {
        var v = b - a;
        double len = v.Length;
        if (len < 1) return b;
        double ang = Math.Round(Math.Atan2(v.Y, v.X) / (Math.PI / 4)) * (Math.PI / 4);
        return new Point(a.X + Math.Cos(ang) * len, a.Y + Math.Sin(ang) * len);
    }

    /// <summary>Linie, bei Pfeilen mit Spitze. style: 0 Klassisch, 1 Spitz, 2 Offen, 3 Modern, 4 Punkt.</summary>
    internal static Geometry ArrowGeo(Point a, Point b, bool head, double thick, int style = 0)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(a, false, false);
            c.LineTo(b, true, false);
            var v = a - b;
            if (head && v.Length > 1)
            {
                v.Normalize();
                if (style == 4)
                {
                    double r = Math.Max(2.5, 3 + thick * 1.6);
                    c.BeginFigure(new Point(b.X + r, b.Y), true, true);
                    c.ArcTo(new Point(b.X - r, b.Y), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
                    c.ArcTo(new Point(b.X + r, b.Y), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
                }
                else
                {
                    double half = style switch { 1 => 17, 3 => 26, _ => 28 };
                    double len = style switch { 1 => 18 + thick * 4, 2 => 12 + thick * 3.5, 3 => 14 + thick * 3.5, _ => 12 + thick * 3 };
                    var m1 = Matrix.Identity; m1.Rotate(half);
                    var m2 = Matrix.Identity; m2.Rotate(-half);
                    var p1 = b + m1.Transform(v) * len;
                    var p2 = b + m2.Transform(v) * len;
                    if (style == 2)
                    {
                        c.BeginFigure(p1, false, false);
                        c.LineTo(b, true, true);
                        c.LineTo(p2, true, true);
                    }
                    else if (style == 3)
                    {
                        var notch = b + v * (len * 0.68);
                        c.BeginFigure(b, true, true);
                        c.LineTo(p1, true, true);
                        c.LineTo(notch, true, true);
                        c.LineTo(p2, true, true);
                    }
                    else
                    {
                        c.BeginFigure(b, true, true);
                        c.LineTo(p1, true, true);
                        c.LineTo(p2, true, true);
                    }
                }
            }
        }
        g.Freeze();
        return g;
    }

    // ---------- Text ----------
    TextBox? editBox;

    void AddText(Point p)
    {
        var tb = new TextBox
        {
            FontSize = 14 + thick * 2, FontWeight = FontWeights.SemiBold, Foreground = Brush(), CaretBrush = Brush(),
            Background = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255)), BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1), AcceptsReturn = true, MinWidth = 80, Padding = new Thickness(2),
        };
        Canvas.SetLeft(tb, p.X); Canvas.SetTop(tb, p.Y);
        Art.Children.Add(tb);
        editBox = tb;
        Dispatcher.BeginInvoke(new Action(() => tb.Focus()), System.Windows.Threading.DispatcherPriority.Input);
        tb.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { CommitText(); toolButtons[Tool.Pointer].IsChecked = true; e.Handled = true; } };
    }

    // vorhandenen Text per Doppelklick erneut bearbeiten
    TextBlock? editOrig;
    int editIdx;
    (string text, Brush fg, double fs, double l, double t, double tx, double ty) editOld;

    void StartEdit(TextBlock t)
    {
        CommitText();
        Select(null);
        var tr = GetT(t);
        editOld = (t.Text, t.Foreground, t.FontSize, Canvas.GetLeft(t), Canvas.GetTop(t), tr.X, tr.Y);
        var tb = new TextBox
        {
            Text = t.Text, FontSize = t.FontSize, FontWeight = t.FontWeight, Foreground = t.Foreground, CaretBrush = t.Foreground,
            Background = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255)), BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1), AcceptsReturn = true, MinWidth = 80, Padding = new Thickness(2),
        };
        Canvas.SetLeft(tb, editOld.l + tr.X); Canvas.SetTop(tb, editOld.t + tr.Y);
        editIdx = Art.Children.IndexOf(t);
        Art.Children.Remove(t);
        editOrig = t;
        Art.Children.Add(tb);
        editBox = tb;
        Dispatcher.BeginInvoke(new Action(() => { tb.Focus(); tb.SelectAll(); }), System.Windows.Threading.DispatcherPriority.Input);
        tb.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { CommitText(); toolButtons[Tool.Pointer].IsChecked = true; e.Handled = true; } };
    }

    void CommitText()
    {
        var tb = editBox;
        if (tb == null) return;
        editBox = null;
        int idx = Art.Children.IndexOf(tb);
        if (idx < 0) return;
        Art.Children.RemoveAt(idx);
        if (editOrig != null)
        {
            var ot = editOrig; editOrig = null;
            var o = editOld; int at = Math.Min(editIdx, Art.Children.Count);
            if (string.IsNullOrWhiteSpace(tb.Text))
            {
                // leer gelassen = Text gelöscht
                Push(() => { Art.Children.Insert(Math.Min(at, Art.Children.Count), ot); }, () => Art.Children.Remove(ot));
                Select(null);
                return;
            }
            string nt = tb.Text; Brush nfg = tb.Foreground; double nfs = tb.FontSize;
            double nl = Canvas.GetLeft(tb), ntp = Canvas.GetTop(tb);
            void Apply(string text, Brush fg, double fs, double l, double t, double tx, double ty)
            {
                ot.Text = text; ot.Foreground = fg; ot.FontSize = fs;
                Canvas.SetLeft(ot, l); Canvas.SetTop(ot, t);
                var tr = GetT(ot); tr.X = tx; tr.Y = ty;
                if (!Art.Children.Contains(ot)) Art.Children.Insert(Math.Min(at, Art.Children.Count), ot);
                RefreshSel();
            }
            Apply(nt, nfg, nfs, nl, ntp, 0, 0);
            Push(() => Apply(o.text, o.fg, o.fs, o.l, o.t, o.tx, o.ty), () => Apply(nt, nfg, nfs, nl, ntp, 0, 0));
            Select(ot);
            return;
        }
        if (string.IsNullOrWhiteSpace(tb.Text)) return;
        var t = new TextBlock
        {
            Text = tb.Text, FontSize = tb.FontSize, FontWeight = tb.FontWeight, Foreground = tb.Foreground,
            Padding = new Thickness(3),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 3, Opacity = 0.6, Color = Colors.Black },
        };
        Canvas.SetLeft(t, Canvas.GetLeft(tb)); Canvas.SetTop(t, Canvas.GetTop(tb));
        Art.Children.Add(t);
        Push(() => Art.Children.Remove(t), () => Art.Children.Add(t));
        Select(t);
    }

    // ---------- Nummern ----------
    /// <summary>Kleinste Nummer ab 1, die noch nicht auf dem Bild vergeben ist (gelöschte Nummern werden wieder frei).</summary>
    int NextFreeNumber()
    {
        var used = new System.Collections.Generic.HashSet<int>();
        foreach (var c in Art.Children)
            if (c is Grid { Children.Count: 2 } g && g.Children[0] is Ellipse && g.Children[1] is TextBlock t && int.TryParse(t.Text, out int v)) used.Add(v);
        int n = 1;
        while (used.Contains(n)) n++;
        return n;
    }

    void AddNumber(Point p)
    {
        int n = NextFreeNumber();
        double d = 22 + thick * 2;
        var g = new Grid { Width = d, Height = d };
        g.Children.Add(new Ellipse { Fill = Brush(), Stroke = Brushes.White, StrokeThickness = 2 });
        g.Children.Add(new TextBlock { Text = n.ToString(), Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = d * 0.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        Canvas.SetLeft(g, p.X - d / 2); Canvas.SetTop(g, p.Y - d / 2);
        Art.Children.Add(g);
        Push(() => Art.Children.Remove(g), () => Art.Children.Add(g));
        Select(g);
    }

    // ---------- Verpixeln & Zuschneiden ----------
    UIElement? MakePixelate(Rect r, int shape = 0, List<Point>? poly = null)
    {
        var shapeRect = r;
        double feather = FeatherPx(shapeRect, settings.BlurEdge);
        if (feather > 0) r.Inflate(feather * 2.5, feather * 2.5);
        r.Intersect(new Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight));
        if (r.Width < 3 || r.Height < 3) return null;
        var crop = new CroppedBitmap(bmp, new Int32Rect((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height));
        double f = Math.Max(1, Math.Min(6 + thick, Math.Min(r.Width, r.Height) / 2));
        var small = new TransformedBitmap(crop, new ScaleTransform(1 / f, 1 / f));
        var img = new Image { Source = small, Width = (int)r.Width, Height = (int)r.Height, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
        Canvas.SetLeft(img, (int)r.X); Canvas.SetTop(img, (int)r.Y);
        ApplyShapeMask(img, shape, poly, shapeRect, feather);
        return img;
    }

    /// <summary>Weichgezeichneter Bereich (Gaußähnlich: dreifacher Boxfilter), Stärke über den Regler.</summary>
    UIElement? MakeSoften(Rect r, int shape = 0, List<Point>? poly = null)
    {
        var shapeRect = r;
        double feather = FeatherPx(shapeRect, settings.SoftenEdge);
        if (feather > 0) r.Inflate(feather * 2.5, feather * 2.5);
        r.Intersect(new Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight));
        if (r.Width < 3 || r.Height < 3) return null;
        int w = (int)r.Width, h = (int)r.Height;
        var crop = new CroppedBitmap(bmp, new Int32Rect((int)r.X, (int)r.Y, w, h));
        var conv = new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);
        var px = new byte[w * h * 4];
        conv.CopyPixels(px, w * 4, 0);
        int rad = (int)Math.Clamp(2 + thick * 1.6, 2, Math.Min(w, h) / 2.0);
        BlurBgra(px, w, h, Math.Max(1, rad));
        var res = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        res.Freeze();
        var img = new Image { Source = res, Width = w, Height = h, Stretch = Stretch.Fill };
        Canvas.SetLeft(img, (int)r.X); Canvas.SetTop(img, (int)r.Y);
        ApplyShapeMask(img, shape, poly, shapeRect, feather);
        return img;
    }

    /// <summary>Boxfilter-Weichzeichner (3 Durchgänge ≈ Gauß) auf einem BGRA-Puffer, Ränder werden festgehalten.</summary>
    static void BlurBgra(byte[] px, int w, int h, int r)
    {
        var tmp = new byte[px.Length];
        for (int pass = 0; pass < 3; pass++)
        {
            BoxPass(px, tmp, w, h, r, true);
            BoxPass(tmp, px, w, h, r, false);
        }
    }

    static void BoxPass(byte[] src, byte[] dst, int w, int h, int r, bool horizontal)
    {
        int len = horizontal ? w : h, lines = horizontal ? h : w;
        int step = horizontal ? 4 : w * 4;
        int lineStep = horizontal ? w * 4 : 4;
        double inv = 1.0 / (2 * r + 1);
        for (int line = 0; line < lines; line++)
        {
            int baseIdx = line * lineStep;
            for (int c = 0; c < 4; c++)
            {
                double sum = 0;
                for (int k = -r; k <= r; k++) sum += src[baseIdx + Math.Clamp(k, 0, len - 1) * step + c];
                for (int i = 0; i < len; i++)
                {
                    dst[baseIdx + i * step + c] = (byte)(sum * inv + 0.5);
                    int add = Math.Min(i + r + 1, len - 1), sub = Math.Max(i - r, 0);
                    sum += src[baseIdx + add * step + c] - src[baseIdx + sub * step + c];
                }
            }
        }
    }

    // ---------- Vorschau-Hilfe für Dialoge (verzögert, damit Schieberegler flüssig bleiben) ----------
    System.Windows.Threading.DispatcherTimer? prevTimer;
    Action? prevJob;

    void QueuePreview(Action job)
    {
        prevJob = job;
        if (prevTimer == null)
        {
            prevTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            prevTimer.Tick += (_, _) => { prevTimer.Stop(); prevJob?.Invoke(); };
        }
        prevTimer.Stop();
        prevTimer.Start();
    }

    bool IsPng => settings.ImageFormat.Equals("png", StringComparison.OrdinalIgnoreCase);

    /// <summary>Bild mit Rahmen (rund auf Wunsch). Außen transparent (PNG) oder weiß (JPG).</summary>
    BitmapSource BuildFrame(BitmapSource old, int b, Color color, int radius)
    {
        int iw = old.PixelWidth, ih = old.PixelHeight, W = iw + 2 * b, H = ih + 2 * b;
        double outer = Math.Min(radius, Math.Min(W, H) / 2.0), inner = Math.Max(0, outer - b);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            if (!IsPng && outer > 0) dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, W, H));
            dc.DrawRoundedRectangle(new SolidColorBrush(color), null, new Rect(0, 0, W, H), outer, outer);
            dc.PushClip(new RectangleGeometry(new Rect(b, b, iw, ih), inner, inner));
            dc.DrawImage(old, new Rect(b, b, iw, ih));
            dc.Pop();
        }
        var nb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        nb.Render(dv);
        nb.Freeze();
        return nb;
    }

    /// <summary>Bild mit weichem Schlagschatten. Liefert das neue Bild und den Rand, um den die Elemente verschoben werden.</summary>
    (BitmapSource bmp, int margin) BuildShadow(BitmapSource old, int offset, int rad, double opacity, int radius)
    {
        int dx = offset, dy = (int)Math.Round(offset * 1.3), m = rad * 3 + offset + 4;
        int iw = old.PixelWidth, ih = old.PixelHeight, W = iw + 2 * m, H = ih + 2 * m;
        double rr = Math.Min(radius, Math.Min(iw, ih) / 2.0);

        var mdv = new DrawingVisual();
        using (var dc = mdv.RenderOpen())
            dc.DrawRoundedRectangle(Brushes.Black, null, new Rect(m + dx, m + dy, iw, ih), rr, rr);
        var mrt = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        mrt.Render(mdv);
        var mask = new byte[W * H * 4];
        mrt.CopyPixels(mask, W * 4, 0);
        BlurBgra(mask, W, H, rad);

        bool png = IsPng;
        var sh = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++)
        {
            double a = mask[i * 4 + 3] / 255.0 * opacity;
            int o = i * 4;
            if (png) { sh[o] = sh[o + 1] = sh[o + 2] = 0; sh[o + 3] = (byte)(a * 255 + 0.5); }
            else { byte v = (byte)(255 * (1 - a) + 0.5); sh[o] = sh[o + 1] = sh[o + 2] = v; sh[o + 3] = 255; }
        }
        var shBmp = BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, sh, W * 4);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawImage(shBmp, new Rect(0, 0, W, H));
            dc.PushClip(new RectangleGeometry(new Rect(m, m, iw, ih), rr, rr));
            dc.DrawImage(old, new Rect(m, m, iw, ih));
            dc.Pop();
        }
        var nb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        nb.Render(dv);
        nb.Freeze();
        return (nb, m);
    }

    void CommitBitmapEffect(BitmapSource old, BitmapSource nb, int shift)
    {
        Action doIt = () => { SetBitmap(nb); Shift(shift, shift); };
        Action undo = () => { SetBitmap(old); Shift(-shift, -shift); };
        doIt();
        Push(undo, doIt);
        RefreshSel();
        FitZoom();
    }

    /// <summary>Schlagschatten um das ganze Bild – mit Live-Vorschau beim Einstellen.</summary>
    void ApplyImageShadow()
    {
        CommitText();
        Select(null);
        var orig = bmp;
        int shifted = 0;
        var dlg = new ShadowDialog(settings.ShadowOffset, settings.ShadowBlur, settings.ShadowOpacity, settings.ShadowRadius, (o, bl, op, r) =>
            QueuePreview(() =>
            {
                var (pv, m) = BuildShadow(orig, o, bl, op / 100.0, r);
                SetBitmap(pv); Shift(m - shifted, m - shifted); shifted = m;
            })) { Owner = this };
        bool ok = dlg.ShowDialog() == true;
        prevTimer?.Stop();
        SetBitmap(orig); Shift(-shifted, -shifted);      // Vorschau zurücknehmen
        if (!ok) { FitZoom(); return; }
        settings.ShadowOffset = dlg.Offset; settings.ShadowBlur = dlg.Blur; settings.ShadowOpacity = dlg.Opacity; settings.ShadowRadius = dlg.Radius;
        settings.Save();
        var (nb, margin) = BuildShadow(orig, dlg.Offset, dlg.Blur, dlg.Opacity / 100.0, dlg.Radius);
        CommitBitmapEffect(orig, nb, margin);
    }

    /// <summary>Rahmen um das ganze Bild – mit Live-Vorschau beim Einstellen.</summary>
    void ApplyImageFrame()
    {
        CommitText();
        Select(null);
        Color start;
        try { start = (Color)ColorConverter.ConvertFromString(settings.FrameColor); } catch { start = Color.FromRgb(0xE5, 0x39, 0x35); }
        var orig = bmp;
        int shifted = 0;
        var dlg = new FrameDialog(settings.FrameWidth, start, settings.FrameRadius, (b, c, r) =>
            QueuePreview(() =>
            {
                SetBitmap(BuildFrame(orig, b, c, r));
                Shift(b - shifted, b - shifted); shifted = b;
            })) { Owner = this };
        bool ok = dlg.ShowDialog() == true;
        prevTimer?.Stop();
        SetBitmap(orig); Shift(-shifted, -shifted);      // Vorschau zurücknehmen
        if (!ok) { FitZoom(); return; }
        settings.FrameWidth = dlg.FrameWidth; settings.FrameRadius = dlg.Radius;
        settings.FrameColor = $"#{dlg.FrameColor.R:X2}{dlg.FrameColor.G:X2}{dlg.FrameColor.B:X2}";
        settings.Save();
        CommitBitmapEffect(orig, BuildFrame(orig, dlg.FrameWidth, dlg.FrameColor, dlg.Radius), dlg.FrameWidth);
    }

    /// <summary>Bild in Graustufen umwandeln (Markierungen darüber behalten ihre Farbe).</summary>
    void ApplyGrayscale()
    {
        CommitText();
        Select(null);
        var old = bmp;
        int w = old.PixelWidth, h = old.PixelHeight;
        var conv = new FormatConvertedBitmap(old, PixelFormats.Bgra32, null, 0);
        var px = new byte[w * h * 4];
        conv.CopyPixels(px, w * 4, 0);
        for (int i = 0; i < px.Length; i += 4)
        {
            byte g = (byte)Math.Clamp(0.114 * px[i] + 0.587 * px[i + 1] + 0.299 * px[i + 2] + 0.5, 0, 255);
            px[i] = px[i + 1] = px[i + 2] = g;
        }
        var nb = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        nb.Freeze();
        Action doIt = () => SetBitmap(nb);
        Action undo = () => SetBitmap(old);
        doIt();
        Push(undo, doIt);
        RefreshSel();
    }

    // ---------- Größe, Drehen, Kippen (Markierungen werden dabei ins Bild eingerechnet) ----------
    void ApplyFlatTransform(Func<BitmapSource, BitmapSource> f)
    {
        CommitText();
        Select(null);
        var oldBmp = bmp;
        var oldEls = new List<UIElement>();
        foreach (UIElement c in Art.Children) if (!ReferenceEquals(c, baseImg)) oldEls.Add(c);
        var nb = f(Render());
        if (nb.CanFreeze) nb.Freeze();
        Action doIt = () => { foreach (var el in oldEls) Art.Children.Remove(el); SetBitmap(nb); };
        Action undo = () => { SetBitmap(oldBmp); foreach (var el in oldEls) if (!Art.Children.Contains(el)) Art.Children.Add(el); };
        doIt();
        Push(undo, doIt);
        RefreshSel();
        FitZoom();
    }

    void ResizeImage()
    {
        var dlg = new ResizeDialog(bmp.PixelWidth, bmp.PixelHeight) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        int nw = dlg.NewWidth, nh = dlg.NewHeight;
        if (nw == bmp.PixelWidth && nh == bmp.PixelHeight) return;
        ApplyFlatTransform(src =>
        {
            var dv = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
            using (var dc = dv.RenderOpen()) dc.DrawImage(src, new Rect(0, 0, nw, nh));
            var rtb = new RenderTargetBitmap(nw, nh, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            return rtb;
        });
    }

    void RotateImage(int deg) => ApplyFlatTransform(src => new TransformedBitmap(src, new RotateTransform(deg)));
    void FlipImage(bool horizontal) => ApplyFlatTransform(src => new TransformedBitmap(src, horizontal ? new ScaleTransform(-1, 1) : new ScaleTransform(1, -1)));

    // ---------- Farbanpassungen ----------
    static BitmapSource PixelEffect(BitmapSource src, Action<byte[], int, int> fx)
    {
        int w = src.PixelWidth, h = src.PixelHeight;
        var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        var px = new byte[w * h * 4];
        conv.CopyPixels(px, w * 4, 0);
        fx(px, w, h);
        var nb = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        nb.Freeze();
        return nb;
    }

    void ApplyPixelEffect(Action<byte[], int, int> fx)
    {
        CommitText();
        Select(null);
        var old = bmp;
        var nb = PixelEffect(old, fx);
        Action doIt = () => SetBitmap(nb);
        Action undo = () => SetBitmap(old);
        doIt();
        Push(undo, doIt);
        RefreshSel();
    }

    static void InvertFx(byte[] px, int w, int h)
    {
        for (int i = 0; i < px.Length; i += 4) { px[i] = (byte)(255 - px[i]); px[i + 1] = (byte)(255 - px[i + 1]); px[i + 2] = (byte)(255 - px[i + 2]); }
    }

    static void SharpenFx(byte[] px, int w, int h)
    {
        var blur = (byte[])px.Clone();
        BlurBgra(blur, w, h, 2);
        for (int i = 0; i < px.Length; i += 4)
            for (int c = 0; c < 3; c++)
                px[i + c] = (byte)Math.Clamp(px[i + c] + 1.2 * (px[i + c] - blur[i + c]) + 0.5, 0, 255);
    }

    static Action<byte[], int, int> BrightnessFx(int brightness, int contrast)
    {
        double c = contrast * 2.55, f = (259 * (c + 255)) / (255 * (259 - c)), b = brightness * 2.55;
        return (px, w, h) =>
        {
            for (int i = 0; i < px.Length; i += 4)
                for (int k = 0; k < 3; k++)
                    px[i + k] = (byte)Math.Clamp(f * (px[i + k] - 128) + 128 + b + 0.5, 0, 255);
        };
    }

    void BrightnessContrast()
    {
        CommitText();
        Select(null);
        var dlg = new BrightnessDialog((b, c) => baseImg.Source = PixelEffect(bmp, BrightnessFx(b, c))) { Owner = this };
        bool ok = dlg.ShowDialog() == true;
        baseImg.Source = bmp;                      // Vorschau zurücknehmen
        if (ok && (dlg.Brightness != 0 || dlg.Contrast != 0)) ApplyPixelEffect(BrightnessFx(dlg.Brightness, dlg.Contrast));
    }

    // ---------- Schatten für das ausgewählte Element ----------
    void ToggleElementShadow()
    {
        var el = selected;
        if (el == null) { Status.Text = "Erst ein Element mit dem Auswahl-Werkzeug anklicken."; return; }
        var old = el.Effect;
        bool has = old is System.Windows.Media.Effects.DropShadowEffect;
        Action doIt = () => el.Effect = has ? null : new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 4, BlurRadius = 8, Opacity = 0.6, Color = Colors.Black, Direction = 315 };
        Action undo = () => el.Effect = old;
        doIt();
        Push(undo, doIt);
        UpdateSel();
    }

    // ---------- Weitere Bildeffekte ----------
    void SlidersEffect(string title, (string label, int min, int max, int val, string unit)[] rows, Func<int[], Action<byte[], int, int>> makeFx)
    {
        CommitText();
        Select(null);
        var orig = bmp;
        var dlg = new SlidersDialog(title, rows, v => QueuePreview(() => baseImg.Source = PixelEffect(orig, makeFx(v)))) { Owner = this };
        bool ok = dlg.ShowDialog() == true;
        prevTimer?.Stop();
        baseImg.Source = orig;
        if (!ok) return;
        var vals = dlg.Values;
        bool changed = false;
        for (int i = 0; i < rows.Length; i++) if (vals[i] != rows[i].val) changed = true;
        if (changed) ApplyPixelEffect(makeFx(vals));
    }

    void HueSaturation() => SlidersEffect("Farbton und Sättigung", new[] { ("Farbton", -180, 180, 0, "°"), ("Sättigung", -100, 100, 0, ""), ("Helligkeit", -100, 100, 0, "") },
        v => (px, w, h) =>
        {
            double hs = v[0], sat = 1 + v[1] / 100.0, li = v[2] / 100.0;
            for (int i = 0; i < px.Length; i += 4)
            {
                ColorPickerWindow.ToHsv(Color.FromRgb(px[i + 2], px[i + 1], px[i]), out double hh, out double ss, out double vv);
                hh = ((hh + hs) % 360 + 360) % 360;
                ss = Math.Clamp(ss * sat, 0, 1);
                vv = li >= 0 ? vv + (1 - vv) * li : vv * (1 + li);
                var c = ColorPickerWindow.FromHsv(hh, ss, Math.Clamp(vv, 0, 1));
                px[i] = c.B; px[i + 1] = c.G; px[i + 2] = c.R;
            }
        });

    void ColorBalance() => SlidersEffect("Farbbalance", new[] { ("Rot ↔ Cyan", -100, 100, 0, ""), ("Grün ↔ Magenta", -100, 100, 0, ""), ("Blau ↔ Gelb", -100, 100, 0, "") },
        v => (px, w, h) =>
        {
            for (int i = 0; i < px.Length; i += 4)
            {
                px[i] = (byte)Math.Clamp(px[i] + v[2], 0, 255);
                px[i + 1] = (byte)Math.Clamp(px[i + 1] + v[1], 0, 255);
                px[i + 2] = (byte)Math.Clamp(px[i + 2] + v[0], 0, 255);
            }
        });

    void PixelateWhole() => SlidersEffect("In Pixel auflösen", new[] { ("Blockgröße", 1, 80, 1, " px") },
        v => (px, w, h) =>
        {
            int n = Math.Max(1, v[0]);
            if (n <= 1) return;
            for (int by = 0; by < h; by += n)
                for (int bx = 0; bx < w; bx += n)
                {
                    int x1 = Math.Min(w, bx + n), y1 = Math.Min(h, by + n);
                    long sb = 0, sg = 0, sr = 0, sa = 0; int cnt = 0;
                    for (int y = by; y < y1; y++) for (int x = bx; x < x1; x++) { int i = (y * w + x) * 4; sb += px[i]; sg += px[i + 1]; sr += px[i + 2]; sa += px[i + 3]; cnt++; }
                    byte ab = (byte)(sb / cnt), ag = (byte)(sg / cnt), ar = (byte)(sr / cnt), aa = (byte)(sa / cnt);
                    for (int y = by; y < y1; y++) for (int x = bx; x < x1; x++) { int i = (y * w + x) * 4; px[i] = ab; px[i + 1] = ag; px[i + 2] = ar; px[i + 3] = aa; }
                }
        });

    BitmapSource BuildWatermark(BitmapSource src, string text, int size, int opacity, int pos, bool dark)
    {
        int w = src.PixelWidth, h = src.PixelHeight;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawImage(src, new Rect(0, 0, w, h));
            if (!string.IsNullOrEmpty(text))
            {
                byte a = (byte)(Math.Clamp(opacity, 5, 100) * 255 / 100);
                Brush fg = new SolidColorBrush(dark ? Color.FromArgb(a, 0, 0, 0) : Color.FromArgb(a, 255, 255, 255));
                Brush sh = new SolidColorBrush(dark ? Color.FromArgb((byte)(a / 2), 255, 255, 255) : Color.FromArgb((byte)(a / 2), 0, 0, 0));
                double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                var face = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
                FormattedText Ft(Brush b) => new(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, b, ppd);
                var f = Ft(fg); var f2 = Ft(sh);
                if (pos == 5)
                {
                    dc.PushTransform(new RotateTransform(-30, w / 2.0, h / 2.0));
                    double sx = f.Width + size * 2, sy = f.Height + size * 2;
                    for (double y = -h; y < h * 2; y += sy) for (double x = -w; x < w * 2; x += sx) { dc.DrawText(f2, new Point(x + 1, y + 1)); dc.DrawText(f, new Point(x, y)); }
                    dc.Pop();
                }
                else
                {
                    double m = size * 0.6;
                    double x = pos is 1 or 3 ? w - f.Width - m : pos is 2 or 4 ? m : (w - f.Width) / 2;
                    double y = pos is 1 or 2 ? h - f.Height - m : pos is 3 or 4 ? m : (h - f.Height) / 2;
                    dc.DrawText(f2, new Point(x + 1, y + 1)); dc.DrawText(f, new Point(x, y));
                }
            }
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv); rtb.Freeze();
        return rtb;
    }

    void Watermark()
    {
        CommitText();
        Select(null);
        var orig = bmp;
        var dlg = new WatermarkDialog(settings.WatermarkText, settings.WatermarkSize, settings.WatermarkOpacity, settings.WatermarkPos, settings.WatermarkDark,
            (tx, sz, op, ps, dk) => QueuePreview(() => baseImg.Source = BuildWatermark(orig, tx, sz, op, ps, dk))) { Owner = this };
        bool ok = dlg.ShowDialog() == true;
        prevTimer?.Stop();
        baseImg.Source = orig;
        if (!ok || string.IsNullOrWhiteSpace(dlg.WmText)) return;
        settings.WatermarkText = dlg.WmText; settings.WatermarkSize = dlg.WmSize; settings.WatermarkOpacity = dlg.WmOpacity; settings.WatermarkPos = dlg.WmPos; settings.WatermarkDark = dlg.WmDark;
        settings.Save();
        var nb = BuildWatermark(orig, dlg.WmText, dlg.WmSize, dlg.WmOpacity, dlg.WmPos, dlg.WmDark);
        Action doIt = () => SetBitmap(nb);
        Action undo = () => SetBitmap(orig);
        doIt();
        Push(undo, doIt);
        RefreshSel();
    }

    // ---------- Arbeitsbereich ----------
    void ChangeCanvas()
    {
        CommitText();
        Select(null);
        var dlg = new CanvasDialog(bmp.PixelWidth, bmp.PixelHeight) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        int l = dlg.Left0, t = dlg.Top0, r = dlg.Right0, b = dlg.Bottom0;
        if (l + t + r + b == 0) return;
        var old = bmp;
        int iw = old.PixelWidth, ih = old.PixelHeight, W = iw + l + r, H = ih + t + b;
        Color fillColor = dlg.Fill switch { 1 => Colors.Black, 3 => color, _ => Colors.White };
        bool transparent = dlg.Fill == 2 && IsPng;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            if (!transparent) dc.DrawRectangle(new SolidColorBrush(dlg.Fill == 2 ? Colors.White : fillColor), null, new Rect(0, 0, W, H));
            dc.DrawImage(old, new Rect(l, t, iw, ih));
        }
        var nb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        nb.Render(dv); nb.Freeze();
        Action doIt = () => { SetBitmap(nb); Shift(l, t); };
        Action undo = () => { SetBitmap(old); Shift(-l, -t); };
        doIt();
        Push(undo, doIt);
        RefreshSel();
        FitZoom();
    }

    // ---------- Füllwerkzeug ----------
    static readonly string[] FillNames = { "Fläche füllen", "Farbe im ganzen Bild ersetzen" };

    void HookListMenu(RadioButton rb, string[] names, Func<int> get, Action<int> set)
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => { timer.Stop(); ShowListMenu(rb, names, get, set); };
        rb.PreviewMouseLeftButtonDown += (_, e) =>
        {
            var pos = e.GetPosition(rb);
            if (pos.X > rb.ActualWidth - 22 && pos.Y < 38) { ShowListMenu(rb, names, get, set); e.Handled = true; return; }
            timer.Start();
        };
        rb.PreviewMouseLeftButtonUp += (_, _) => timer.Stop();
        rb.MouseLeave += (_, _) => timer.Stop();
        rb.MouseRightButtonUp += (_, e) => { ShowListMenu(rb, names, get, set); e.Handled = true; };
    }

    void ShowListMenu(RadioButton target, string[] names, Func<int> get, Action<int> set)
    {
        var pop = new Popup { PlacementTarget = target, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        var panel = new StackPanel();
        for (int i = 0; i < names.Length; i++)
        {
            int id = i;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = id == get() ? "\u2713" : "", Foreground = Ink, Width = 16, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = names[id], Foreground = Ink, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), MinWidth = 190 });
            var b = new Button { Content = row, Style = (Style)FindResource("RibbonBtn"), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 3, 6, 3) };
            b.Click += (_, _) => { set(id); settings.Save(); target.IsChecked = true; pop.IsOpen = false; };
            panel.Children.Add(b);
        }
        pop.Child = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(3), Child = panel,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.5 },
        };
        pop.IsOpen = true;
    }

    /// <summary>Füllt per Klick: Fläche (zusammenhängend) oder alle ähnlichen Farben im Bild. Die Stärke bestimmt die Toleranz.</summary>
    void DoFill(Point p)
    {
        int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y);
        var old = bmp;
        int w = old.PixelWidth, h = old.PixelHeight;
        if (x < 0 || y < 0 || x >= w || y >= h) return;
        var conv = new FormatConvertedBitmap(old, PixelFormats.Bgra32, null, 0);
        var px = new byte[w * h * 4];
        conv.CopyPixels(px, w * 4, 0);
        int tol = (int)(thick * 6);
        int ti = (y * w + x) * 4;
        byte tb = px[ti], tg = px[ti + 1], tr = px[ti + 2];
        bool Match(int i) => Math.Abs(px[i] - tb) <= tol && Math.Abs(px[i + 1] - tg) <= tol && Math.Abs(px[i + 2] - tr) <= tol;
        byte fb = color.B, fg = color.G, fr = color.R;
        if (tb == fb && tg == fg && tr == fr) return;
        void Set(int i) { px[i] = fb; px[i + 1] = fg; px[i + 2] = fr; px[i + 3] = 255; }
        if (settings.FillMode == 1)
        {
            for (int i = 0; i < px.Length; i += 4) if (Match(i)) Set(i);
        }
        else
        {
            var seen = new bool[w * h];
            var stack = new Stack<(int x, int y)>();
            stack.Push((x, y));
            while (stack.Count > 0)
            {
                var (cx, cy) = stack.Pop();
                if (cx < 0 || cy < 0 || cx >= w || cy >= h || seen[cy * w + cx]) continue;
                int idx = (cy * w + cx) * 4;
                if (!Match(idx)) continue;
                int lx = cx; while (lx > 0 && !seen[cy * w + lx - 1] && Match((cy * w + lx - 1) * 4)) lx--;
                int rx = cx; while (rx < w - 1 && !seen[cy * w + rx + 1] && Match((cy * w + rx + 1) * 4)) rx++;
                for (int xx = lx; xx <= rx; xx++)
                {
                    seen[cy * w + xx] = true; Set((cy * w + xx) * 4);
                    if (cy > 0 && !seen[(cy - 1) * w + xx]) stack.Push((xx, cy - 1));
                    if (cy < h - 1 && !seen[(cy + 1) * w + xx]) stack.Push((xx, cy + 1));
                }
            }
        }
        var nb = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        nb.Freeze();
        Action doIt = () => SetBitmap(nb);
        Action undo = () => SetBitmap(old);
        doIt();
        Push(undo, doIt);
    }

    // ---------- Externes Programm ----------
    void OpenExternal()
    {
        if (string.IsNullOrWhiteSpace(settings.ExternalProgram)) { Status.Text = "Erst in den Einstellungen unter „Speichern“ ein Programm wählen."; return; }
        try
        {
            Directory.CreateDirectory(settings.SaveFolder);
            var path = settings.UniquePath(Ext);
            SaveTo(path);
            var args = string.IsNullOrWhiteSpace(settings.ExternalArgs) ? $"\"{path}\"" : settings.ExternalArgs.Replace("%f", $"\"{path}\"");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(settings.ExternalProgram, args) { UseShellExecute = false });
        }
        catch (Exception ex) { Status.Text = "Programm konnte nicht gestartet werden: " + ex.Message; }
    }

    // ---------- Drucken ----------
    void PrintImage()
    {
        try
        {
            var pd = new PrintDialog();
            if (pd.ShowDialog() != true) return;
            var img = new Image { Source = Render(), Stretch = Stretch.Uniform, Width = pd.PrintableAreaWidth, Height = pd.PrintableAreaHeight };
            img.Measure(new Size(pd.PrintableAreaWidth, pd.PrintableAreaHeight));
            img.Arrange(new Rect(0, 0, pd.PrintableAreaWidth, pd.PrintableAreaHeight));
            pd.PrintVisual(img, "Knipser");
            Status.Text = "An den Drucker gesendet.";
        }
        catch (Exception ex) { Status.Text = "Drucken fehlgeschlagen: " + ex.Message; }
    }

    void ApplyCrop(Rect r)
    {
        r.Intersect(new Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight));
        if (r.Width < 3 || r.Height < 3) return;
        var old = bmp;
        int x = (int)r.X, y = (int)r.Y;
        var nw = new CroppedBitmap(old, new Int32Rect(x, y, (int)r.Width, (int)r.Height));
        Action doIt = () => { SetBitmap(nw); Shift(-x, -y); };
        Action undo = () => { SetBitmap(old); Shift(x, y); };
        doIt();
        Push(undo, doIt);
        RefreshSel();
    }

    void Shift(double dx, double dy)
    {
        foreach (UIElement c in Art.Children)
        {
            if (ReferenceEquals(c, baseImg)) continue;
            var t = GetT(c);
            t.X += dx; t.Y += dy;
        }
    }

    // ---------- Zuschneiden (wie in Photoshop: Anfasser am Bildrand nach innen ziehen) ----------
    bool cropMode;
    Rect cropRect, cropOrig;
    int cropDrag = -1;          // 0-7 = Anfasser, 8 = Rahmen verschieben, 9 = neuen Rahmen aufziehen
    Point cropStartPt;
    readonly Dictionary<Tool, RadioButton> toolButtons = new();
    Button? cropApplyBtn, cropCancelBtn;
    readonly Path cropDim = new() { Fill = new SolidColorBrush(Color.FromArgb(0xAA, 0, 0, 0)), IsHitTestVisible = false };
    readonly Rectangle cropBorder = new() { Stroke = Brushes.White, IsHitTestVisible = false };
    readonly TextBlock cropLabel = new() { Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x20, 0x20, 0x20)), Padding = new Thickness(5, 1, 5, 1), IsHitTestVisible = false };
    readonly List<Rectangle> cropHandles = new();

    static readonly Cursor[] CropCursors =
    {
        Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE,
        Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE,
    };

    void StartCrop()
    {
        Select(null);                    // vor dem Setzen von cropMode, damit die Auswahl-Anzeige verschwindet
        cropMode = true;
        cropRect = new Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight);
        Sel.Children.Clear();
        cropHandles.Clear();
        Sel.Children.Add(cropDim);
        Sel.Children.Add(cropBorder);
        Sel.Children.Add(cropLabel);
        for (int i = 0; i < 8; i++)
        {
            var h = MakeCropHandle(i);
            cropHandles.Add(h);
            Sel.Children.Add(h);
        }
        if (cropApplyBtn != null) cropApplyBtn.Visibility = Visibility.Visible;
        if (cropCancelBtn != null) cropCancelBtn.Visibility = Visibility.Visible;
        Status.Text = "Zuschneiden: Anfasser an Rand und Ecken nach innen ziehen · Enter = anwenden · Esc = abbrechen";
        PositionCrop();
    }

    void EndCrop()
    {
        cropMode = false;
        cropDrag = -1;
        Sel.Children.Clear();
        cropHandles.Clear();
        if (cropApplyBtn != null) cropApplyBtn.Visibility = Visibility.Collapsed;
        if (cropCancelBtn != null) cropCancelBtn.Visibility = Visibility.Collapsed;
        Status.Text = Hint;
    }

    void CancelCrop()
    {
        EndCrop();
        toolButtons[Tool.Pointer].IsChecked = true;
    }

    void ApplyCropNow()
    {
        var r = cropRect;
        EndCrop();
        toolButtons[Tool.Pointer].IsChecked = true;
        if (r.Width < bmp.PixelWidth - 0.5 || r.Height < bmp.PixelHeight - 0.5) ApplyCrop(r);
    }

    Rectangle MakeCropHandle(int idx)
    {
        var h = new Rectangle
        {
            Fill = Brushes.White, Stroke = new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5)), Cursor = CropCursors[idx],
        };
        h.MouseLeftButtonDown += (_, e) =>
        {
            cropDrag = idx; cropOrig = cropRect;
            h.CaptureMouse();
            e.Handled = true;
        };
        h.MouseMove += (_, e) =>
        {
            if (cropDrag != idx || !h.IsMouseCaptured) return;
            CropDragHandle(idx, e.GetPosition(Art));
        };
        h.MouseLeftButtonUp += (_, _) =>
        {
            if (cropDrag != idx) return;
            h.ReleaseMouseCapture();
            cropDrag = -1;
        };
        return h;
    }

    void CropDragHandle(int idx, Point p)
    {
        double W = bmp.PixelWidth, H = bmp.PixelHeight;
        var o = cropOrig;
        double l = o.Left, t = o.Top, r = o.Right, b = o.Bottom;
        bool L = idx is 0 or 6 or 7, R = idx is 2 or 3 or 4, T = idx is 0 or 1 or 2, B = idx is 4 or 5 or 6;
        if (L) l = Math.Clamp(p.X, 0, r - 8);
        if (R) r = Math.Clamp(p.X, l + 8, W);
        if (T) t = Math.Clamp(p.Y, 0, b - 8);
        if (B) b = Math.Clamp(p.Y, t + 8, H);
        cropRect = new Rect(l, t, r - l, b - t);
        PositionCrop();
    }

    void CropDown(Point p)
    {
        cropStartPt = p; cropOrig = cropRect;
        cropDrag = cropRect.Contains(p) ? 8 : 9;
        Art.CaptureMouse();
    }

    void CropMove(Point p)
    {
        double W = bmp.PixelWidth, H = bmp.PixelHeight;
        if (cropDrag == 8)
        {
            double dx = p.X - cropStartPt.X, dy = p.Y - cropStartPt.Y;
            double x = Math.Clamp(cropOrig.X + dx, 0, W - cropOrig.Width);
            double y = Math.Clamp(cropOrig.Y + dy, 0, H - cropOrig.Height);
            cropRect = new Rect(x, y, cropOrig.Width, cropOrig.Height);
        }
        else
        {
            var a = new Point(Math.Clamp(cropStartPt.X, 0, W), Math.Clamp(cropStartPt.Y, 0, H));
            var b = new Point(Math.Clamp(p.X, 0, W), Math.Clamp(p.Y, 0, H));
            cropRect = new Rect(a, b);
        }
        PositionCrop();
    }

    void CropUp()
    {
        cropDrag = -1;
        if (cropRect.Width < 8 || cropRect.Height < 8) { cropRect = cropOrig; PositionCrop(); }
    }

    void PositionCrop()
    {
        if (!cropMode || cropHandles.Count < 8) return;
        double z = Zoom.ScaleX, hs = 12 / z;
        double W = bmp.PixelWidth, H = bmp.PixelHeight;
        var r = cropRect;
        cropDim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, W, H)), new RectangleGeometry(r));
        Canvas.SetLeft(cropBorder, r.X); Canvas.SetTop(cropBorder, r.Y);
        cropBorder.Width = r.Width; cropBorder.Height = r.Height; cropBorder.StrokeThickness = 1.5 / z;

        double cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
        var pts = new[]
        {
            r.TopLeft, new Point(cx, r.Top), r.TopRight, new Point(r.Right, cy),
            r.BottomRight, new Point(cx, r.Bottom), r.BottomLeft, new Point(r.Left, cy),
        };
        for (int i = 0; i < 8; i++)
        {
            var h = cropHandles[i];
            h.Width = h.Height = hs; h.StrokeThickness = 1.5 / z;
            Canvas.SetLeft(h, pts[i].X - hs / 2); Canvas.SetTop(h, pts[i].Y - hs / 2);
        }
        cropLabel.Text = $"{(int)Math.Round(r.Width)} × {(int)Math.Round(r.Height)}";
        cropLabel.FontSize = 12 / z;
        Canvas.SetLeft(cropLabel, r.X + 6 / z);
        Canvas.SetTop(cropLabel, r.Y + 6 / z);
    }

    // ---------- Ausgabe ----------
    BitmapSource Render()
    {
        CommitText();
        Art.Focus();
        Art.UpdateLayout();
        int w = bmp.PixelWidth, h = bmp.PixelHeight;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var vb = new VisualBrush(Art)
            {
                Viewbox = new Rect(0, 0, w, h), ViewboxUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, w, h), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill,
            };
            dc.DrawRectangle(vb, null, new Rect(0, 0, w, h));
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    BitmapEncoder EncoderFor(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = Math.Clamp(settings.JpgQuality, 1, 100) },
            ".bmp" => new BmpBitmapEncoder(),
            ".gif" => new GifBitmapEncoder(),
            _ => new PngBitmapEncoder(),
        };
    }

    void SaveTo(string path)
    {
        var enc = EncoderFor(path);
        enc.Frames.Add(BitmapFrame.Create(Render()));
        using var fs = File.Create(path);
        enc.Save(fs);
        saved = true;
        Status.Text = "Gespeichert: " + path;
    }

    string Ext => settings.ImageFormat.Equals("png", StringComparison.OrdinalIgnoreCase) ? "png" : "jpg";

    void QuickSave()
    {
        try
        {
            Directory.CreateDirectory(settings.SaveFolder);
            SaveTo(settings.UniquePath(Ext));
        }
        catch (Exception ex) { Status.Text = "Fehler beim Speichern: " + ex.Message; }
    }

    void SaveAs()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "JPEG (*.jpg)|*.jpg|PNG (*.png)|*.png|Bitmap (*.bmp)|*.bmp|GIF (*.gif)|*.gif",
            FilterIndex = Ext == "png" ? 2 : 1,
            FileName = settings.MakeFileName(),
            DefaultExt = "." + Ext,
        };
        try { Directory.CreateDirectory(settings.SaveFolder); dlg.InitialDirectory = settings.SaveFolder; } catch { }
        if (dlg.ShowDialog(this) != true) return;
        try { SaveTo(dlg.FileName); }
        catch (Exception ex) { Status.Text = "Fehler beim Speichern: " + ex.Message; }
    }

    void CopyToClipboard()
    {
        try { Clipboard.SetImage(Render()); Status.Text = "In die Zwischenablage kopiert."; }
        catch (Exception ex) { Status.Text = "Kopieren fehlgeschlagen: " + ex.Message; }
    }

    // ---------- Tastatur ----------
    void OnKey(object sender, KeyEventArgs e)
    {
        if (picking && e.Key == Key.Escape) { EndPick(); e.Handled = true; return; }
        if (Keyboard.FocusedElement is TextBox) return;
        if (cropMode && e.Key == Key.Enter) { ApplyCropNow(); e.Handled = true; return; }
        if (cropMode && e.Key == Key.Escape) { CancelCrop(); e.Handled = true; return; }
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl && e.Key == Key.Z) { Undo(); e.Handled = true; }
        else if (ctrl && e.Key == Key.Y) { Redo(); e.Handled = true; }
        else if (ctrl && e.Key == Key.S) { QuickSave(); e.Handled = true; }
        else if (ctrl && e.Key == Key.C) { CopyToClipboard(); e.Handled = true; }
        else if (ctrl && e.Key == Key.V) { PasteFromClipboard(); e.Handled = true; }
        else if (ctrl && e.Key == Key.OemQuestion) { Reorder(2); e.Handled = true; }   // Strg+# (deutsche Tastatur)
        else if (ctrl && e.Key == Key.OemOpenBrackets) { Reorder(3); e.Handled = true; }   // Strg+ß
        else if (e.Key == Key.Delete && selected != null)
        {
            var el = selected; Select(null);
            if (Art.Children.Contains(el))
            {
                Art.Children.Remove(el);
                Push(() => Art.Children.Add(el), () => Art.Children.Remove(el));
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && selected != null) { Select(null); e.Handled = true; }
    }
}
