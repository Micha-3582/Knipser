using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using Path = System.Windows.Shapes.Path;

namespace Knipser;

enum Tool { Pointer, Rect, Ellipse, Line, Arrow, Pen, Marker, Text, Number, Blur, Crop }

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
        this.settings = settings;
        bmp = image;
        Art.Children.Add(baseImg);
        SetBitmap(bmp);
        BuildToolbar();
        Loaded += (_, _) => FitZoom();
        PreviewKeyDown += OnKey;
        Closing += (_, _) =>
        {
            if (settings.SaveOnClose && !saved) QuickSave();
        };
    }

    // ---------- Aufbau Werkzeugleiste ----------
    void BuildToolbar()
    {
        AddButton("Speichern", (_, _) => QuickSave());
        AddButton("Speichern unter…", (_, _) => SaveAs());
        AddButton("Kopieren", (_, _) => CopyToClipboard());
        AddSep();
        AddButton("Rückgängig", (_, _) => Undo());
        AddButton("Wiederholen", (_, _) => Redo());
        AddSep();
        (string, Tool)[] tools =
        {
            ("Auswahl", Tool.Pointer), ("Pfeil", Tool.Arrow), ("Linie", Tool.Line), ("Rahmen", Tool.Rect),
            ("Ellipse", Tool.Ellipse), ("Stift", Tool.Pen), ("Marker", Tool.Marker), ("Text", Tool.Text),
            ("Nummer", Tool.Number), ("Verpixeln", Tool.Blur), ("Zuschneiden", Tool.Crop),
        };
        foreach (var (name, t) in tools)
        {
            var rb = new RadioButton { Content = name, GroupName = "tools", IsChecked = t == tool };
            var tt = t;
            rb.Checked += (_, _) => { CommitText(); tool = tt; Art.Cursor = tt == Tool.Pointer ? Cursors.Arrow : Cursors.Cross; };
            Bar.Children.Add(rb);
        }
        AddSep();
        var palette = new[] { "#E53935", "#FB8C00", "#FDD835", "#43A047", "#1E88E5", "#8E24AA", "#000000", "#FFFFFF" };
        foreach (var hex in palette)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var sw = new Border
            {
                Width = 22, Height = 22, Margin = new Thickness(3, 2, 3, 2), CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(c), BorderThickness = new Thickness(2), Cursor = Cursors.Hand,
                BorderBrush = c == color ? Brushes.White : Brushes.Transparent,
            };
            sw.MouseLeftButtonDown += (_, _) =>
            {
                color = c;
                foreach (var s in swatches) s.BorderBrush = Brushes.Transparent;
                sw.BorderBrush = Brushes.White;
            };
            swatches.Add(sw);
            Bar.Children.Add(sw);
        }
        AddSep();
        Bar.Children.Add(new TextBlock { Text = "Stärke", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0), Foreground = Brushes.Gray });
        var slider = new Slider { Minimum = 1, Maximum = 20, Value = thick, Width = 110, VerticalAlignment = VerticalAlignment.Center, IsSnapToTickEnabled = true, TickFrequency = 1 };
        slider.ValueChanged += (_, e) => thick = e.NewValue;
        Bar.Children.Add(slider);
        AddSep();
        AddButton("−", (_, _) => SetZoom(Zoom.ScaleX / 1.25));
        AddButton("+", (_, _) => SetZoom(Zoom.ScaleX * 1.25));
        AddButton("Anpassen", (_, _) => FitZoom());
        AddButton("100 %", (_, _) => SetZoom(1));
        AddSep();
        AddButton("Einstellungen", (_, _) => ((App)Application.Current).ShowSettings());
    }

    void AddButton(string text, RoutedEventHandler click)
    {
        var b = new Button { Content = text };
        b.Click += click;
        Bar.Children.Add(b);
    }

    void AddSep() => Bar.Children.Add(new Border { Width = 1, Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)), Margin = new Thickness(6, 2, 6, 2) });

    // ---------- Bild & Zoom ----------
    void SetBitmap(BitmapSource b)
    {
        bmp = b;
        baseImg.Source = b;
        baseImg.Width = b.PixelWidth; baseImg.Height = b.PixelHeight;
        Art.Width = b.PixelWidth; Art.Height = b.PixelHeight;
        Status.Text = $"{b.PixelWidth} × {b.PixelHeight} px";
    }

    void SetZoom(double z)
    {
        z = Math.Clamp(z, 0.1, 8);
        Zoom.ScaleX = Zoom.ScaleY = z;
    }

    void FitZoom()
    {
        double aw = Scroller.ActualWidth - 60, ah = Scroller.ActualHeight - 60;
        if (aw <= 0 || ah <= 0) return;
        SetZoom(Math.Min(1, Math.Min(aw / bmp.PixelWidth, ah / bmp.PixelHeight)));
    }

    void Scroller_Wheel(object sender, MouseWheelEventArgs e)
    {
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
    }

    void Redo()
    {
        if (redoStack.Count == 0) return;
        var a = redoStack.Pop();
        a.redo();
        undoStack.Push(a);
        saved = false;
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
        CommitText();
        Art.Focus();
        p0 = e.GetPosition(Art);
        shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (tool == Tool.Pointer)
        {
            dragEl = FindEl(e.OriginalSource);
            selected = dragEl;
            if (dragEl != null)
            {
                var t = GetT(dragEl);
                dragStart = p0; dragOrig = new Point(t.X, t.Y);
                Art.CaptureMouse();
            }
            return;
        }
        if (tool == Tool.Text) { AddText(p0); return; }
        if (tool == Tool.Number) { AddNumber(p0); return; }

        switch (tool)
        {
            case Tool.Rect:
                cur = new Rectangle { Stroke = Brush(), StrokeThickness = thick, Fill = Brushes.Transparent, RadiusX = 2, RadiusY = 2 };
                break;
            case Tool.Ellipse:
                cur = new Ellipse { Stroke = Brush(), StrokeThickness = thick, Fill = Brushes.Transparent };
                break;
            case Tool.Marker:
                cur = new Rectangle { Fill = Brush(0x70) };
                break;
            case Tool.Blur:
                cur = new Rectangle { Stroke = Brushes.White, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 }, Fill = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255)) };
                break;
            case Tool.Crop:
                cur = new Rectangle { Stroke = Brushes.White, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 }, Fill = new SolidColorBrush(Color.FromArgb(0x33, 30, 136, 229)) };
                break;
            case Tool.Line:
            case Tool.Arrow:
                cur = new Path { Stroke = Brush(), StrokeThickness = thick, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Fill = tool == Tool.Arrow ? Brush() : null };
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
        if (tool == Tool.Pointer && dragEl != null)
        {
            var t = GetT(dragEl);
            t.X = dragOrig.X + p.X - dragStart.X;
            t.Y = dragOrig.Y + p.Y - dragStart.Y;
            return;
        }
        if (!drawing || cur == null) return;
        bool sh = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (cur)
        {
            case Polyline pl:
                pl.Points.Add(p);
                break;
            case Path path:
                if (sh) p = SnapAngle(p0, p);
                path.Data = ArrowGeo(p0, p, tool == Tool.Arrow);
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
        if (tool == Tool.Blur && added is Shape bs)
        {
            Art.Children.Remove(added);
            var px = MakePixelate(new Rect(Canvas.GetLeft(bs), Canvas.GetTop(bs), bs.Width, bs.Height));
            if (px == null) return;
            Art.Children.Add(px);
            Push(() => Art.Children.Remove(px), () => Art.Children.Add(px));
            return;
        }
        Push(() => Art.Children.Remove(added), () => Art.Children.Add(added));
    }

    Point SnapAngle(Point a, Point b)
    {
        var v = b - a;
        double len = v.Length;
        if (len < 1) return b;
        double ang = Math.Round(Math.Atan2(v.Y, v.X) / (Math.PI / 4)) * (Math.PI / 4);
        return new Point(a.X + Math.Cos(ang) * len, a.Y + Math.Sin(ang) * len);
    }

    Geometry ArrowGeo(Point a, Point b, bool head)
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
                double len = 12 + thick * 3, ang = 28 * Math.PI / 180;
                var m1 = Matrix.Identity; m1.Rotate(ang * 180 / Math.PI);
                var m2 = Matrix.Identity; m2.Rotate(-ang * 180 / Math.PI);
                var p1 = b + m1.Transform(v) * len;
                var p2 = b + m2.Transform(v) * len;
                c.BeginFigure(b, true, true);
                c.LineTo(p1, true, true);
                c.LineTo(p2, true, true);
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
        tb.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { CommitText(); e.Handled = true; } };
    }

    void CommitText()
    {
        var tb = editBox;
        if (tb == null) return;
        editBox = null;
        int idx = Art.Children.IndexOf(tb);
        if (idx < 0) return;
        Art.Children.RemoveAt(idx);
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
    }

    // ---------- Nummern ----------
    void AddNumber(Point p)
    {
        int n = counter++;
        double d = 22 + thick * 2;
        var g = new Grid { Width = d, Height = d };
        g.Children.Add(new Ellipse { Fill = Brush(), Stroke = Brushes.White, StrokeThickness = 2 });
        g.Children.Add(new TextBlock { Text = n.ToString(), Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = d * 0.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        Canvas.SetLeft(g, p.X - d / 2); Canvas.SetTop(g, p.Y - d / 2);
        Art.Children.Add(g);
        Push(() => { Art.Children.Remove(g); counter--; }, () => { Art.Children.Add(g); counter++; });
    }

    // ---------- Verpixeln & Zuschneiden ----------
    UIElement? MakePixelate(Rect r)
    {
        r.Intersect(new Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight));
        if (r.Width < 3 || r.Height < 3) return null;
        var crop = new CroppedBitmap(bmp, new Int32Rect((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height));
        double f = Math.Max(1, Math.Min(6 + thick, Math.Min(r.Width, r.Height) / 2));
        var small = new TransformedBitmap(crop, new ScaleTransform(1 / f, 1 / f));
        var img = new Image { Source = small, Width = (int)r.Width, Height = (int)r.Height, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
        Canvas.SetLeft(img, (int)r.X); Canvas.SetTop(img, (int)r.Y);
        return img;
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

    static BitmapEncoder EncoderFor(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 92 },
            ".bmp" => new BmpBitmapEncoder(),
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

    void QuickSave()
    {
        try
        {
            Directory.CreateDirectory(settings.SaveFolder);
            SaveTo(System.IO.Path.Combine(settings.SaveFolder, $"Knipser_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png"));
        }
        catch (Exception ex) { Status.Text = "Fehler beim Speichern: " + ex.Message; }
    }

    void SaveAs()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg|Bitmap (*.bmp)|*.bmp",
            FileName = $"Knipser_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}",
            DefaultExt = ".png",
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
        if (Keyboard.FocusedElement is TextBox) return;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl && e.Key == Key.Z) { Undo(); e.Handled = true; }
        else if (ctrl && e.Key == Key.Y) { Redo(); e.Handled = true; }
        else if (ctrl && e.Key == Key.S) { QuickSave(); e.Handled = true; }
        else if (ctrl && e.Key == Key.C) { CopyToClipboard(); e.Handled = true; }
        else if (e.Key == Key.Delete && selected != null)
        {
            var el = selected; selected = null;
            if (Art.Children.Contains(el))
            {
                Art.Children.Remove(el);
                Push(() => Art.Children.Add(el), () => Art.Children.Remove(el));
            }
            e.Handled = true;
        }
    }
}
