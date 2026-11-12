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
        Icon = App.WindowIcon;
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
        foreach (var f in new[] { "jpg", "png" })
        {
            var rb = new RadioButton { Content = f.ToUpperInvariant(), GroupName = "fmt", IsChecked = Ext == f, ToolTip = "Format beim Speichern" };
            var ff = f;
            rb.Checked += (_, _) => { settings.ImageFormat = ff; settings.Save(); };
            Bar.Children.Add(rb);
        }
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
            rb.Checked += (_, _) =>
            {
                CommitText();
                if (cropMode && tt != Tool.Crop) EndCrop();
                tool = tt;
                Art.Cursor = tt == Tool.Pointer ? Cursors.Arrow : Cursors.Cross;
                if (tt == Tool.Crop) StartCrop();
            };
            toolButtons[t] = rb;
            Bar.Children.Add(rb);
        }
        cropApplyBtn = new Button { Content = "Zuschnitt anwenden", Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)), Visibility = Visibility.Collapsed };
        cropApplyBtn.Click += (_, _) => ApplyCropNow();
        cropCancelBtn = new Button { Content = "Abbrechen", Visibility = Visibility.Collapsed };
        cropCancelBtn.Click += (_, _) => CancelCrop();
        Bar.Children.Add(cropApplyBtn);
        Bar.Children.Add(cropCancelBtn);
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
        thickSlider = slider;
        slider.ValueChanged += (_, e) =>
        {
            if (syncing) return;
            if (selected != null && ApplyThickness(selected, e.NewValue)) { UpdateSel(); return; }
            thick = e.NewValue;
        };
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
        Sel.Width = b.PixelWidth; Sel.Height = b.PixelHeight;
        Status.Text = $"{b.PixelWidth} × {b.PixelHeight} px  ·  Elemente verschieben: Auswahl-Werkzeug oder Strg+Ziehen, Anfasser zum Ändern der Größe";
    }

    void SetZoom(double z)
    {
        z = Math.Clamp(z, 0.1, 8);
        Zoom.ScaleX = Zoom.ScaleY = z;
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
        CommitText();
        Art.Focus();
        p0 = e.GetPosition(Art);
        shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (cropMode) { CropDown(p0); return; }

        var hit = FindEl(e.OriginalSource);
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
            case Path path:
                if (sh) p = SnapAngle(p0, p);
                path.Tag = new LineInfo { A = p0, B = p, Arrow = tool == Tool.Arrow };
                path.Data = ArrowGeo(p0, p, tool == Tool.Arrow, path.StrokeThickness);
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
        if (tool == Tool.Blur && added is Shape bs)
        {
            Art.Children.Remove(added);
            var px = MakePixelate(new Rect(Canvas.GetLeft(bs), Canvas.GetTop(bs), bs.Width, bs.Height));
            if (px == null) return;
            Art.Children.Add(px);
            Push(() => Art.Children.Remove(px), () => Art.Children.Add(px));
            Select(px);
            return;
        }
        Push(() => Art.Children.Remove(added), () => Art.Children.Add(added));
        Select(added);
    }

    // ---------- Auswahl & Anfasser ----------
    sealed class LineInfo { public Point A, B; public bool Arrow; }

    Rectangle? selBox;
    readonly List<Rectangle> handles = new();
    int activeHandle = -1;
    Rect rectBefore;
    LineInfo? lineBefore;

    static bool IsRectLike(UIElement e) => e is Rectangle || e is Ellipse || e is Image;

    static Rect LocalRect(UIElement e)
    {
        var f = (FrameworkElement)e;
        return new Rect(Canvas.GetLeft(e), Canvas.GetTop(e), f.Width, f.Height);
    }

    static Point[] Corners(Rect r) => new[] { r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft };

    Slider? thickSlider;
    bool syncing;

    /// <summary>Stärke eines vorhandenen Elements ändern (Linienstärke bzw. Schriftgröße). false = Element hat keine Stärke.</summary>
    bool ApplyThickness(UIElement el, double v)
    {
        switch (el)
        {
            case Path p when p.Tag is LineInfo li:
                p.StrokeThickness = v; p.Data = ArrowGeo(li.A, li.B, li.Arrow, v); return true;
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
        int n = IsRectLike(selected) ? 4 : selected is Path { Tag: LineInfo } ? 2 : 0;
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
            else if (selected is Path { Tag: LineInfo li }) lineBefore = new LineInfo { A = li.A, B = li.B, Arrow = li.Arrow };
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
        else if (selected is Path path && path.Tag is LineInfo li)
        {
            if (i == 0) li.A = lp; else li.B = lp;
            path.Data = ArrowGeo(li.A, li.B, li.Arrow, path.StrokeThickness);
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
        path.Tag = new LineInfo { A = info.A, B = info.B, Arrow = info.Arrow };
        path.Data = ArrowGeo(info.A, info.B, info.Arrow, path.StrokeThickness);
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
        else if (el is Path path && path.Tag is LineInfo li && lineBefore != null)
        {
            var b = lineBefore;
            var a = new LineInfo { A = li.A, B = li.B, Arrow = li.Arrow };
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

    static Geometry ArrowGeo(Point a, Point b, bool head, double thick)
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
        Select(t);
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
        Select(g);
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
        Status.Text = $"{bmp.PixelWidth} × {bmp.PixelHeight} px";
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
            SaveTo(System.IO.Path.Combine(settings.SaveFolder, $"Knipser_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.{Ext}"));
        }
        catch (Exception ex) { Status.Text = "Fehler beim Speichern: " + ex.Message; }
    }

    void SaveAs()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "JPEG (*.jpg)|*.jpg|PNG (*.png)|*.png|Bitmap (*.bmp)|*.bmp",
            FilterIndex = Ext == "png" ? 2 : 1,
            FileName = $"Knipser_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}",
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
        if (Keyboard.FocusedElement is TextBox) return;
        if (cropMode && e.Key == Key.Enter) { ApplyCropNow(); e.Handled = true; return; }
        if (cropMode && e.Key == Key.Escape) { CancelCrop(); e.Handled = true; return; }
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl && e.Key == Key.Z) { Undo(); e.Handled = true; }
        else if (ctrl && e.Key == Key.Y) { Redo(); e.Handled = true; }
        else if (ctrl && e.Key == Key.S) { QuickSave(); e.Handled = true; }
        else if (ctrl && e.Key == Key.C) { CopyToClipboard(); e.Handled = true; }
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
