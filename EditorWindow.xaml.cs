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

enum Tool { Pointer, Rect, Ellipse, Line, Arrow, Pen, Marker, Text, Number, Blur, Soften, Crop }

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
                Data = ArrowGeo(new Point(4, 12), new Point(58, 12), true, 2.5, st), Stroke = Ink, StrokeThickness = 2.5, Fill = st == 2 ? null : Ink,
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
            BigBtn(Glyph("\uE8C8"), "Kopieren", (_, _) => CopyToClipboard())));
        panels["Datei"].Children.Add(Group("Programm",
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
            ("Rahmen", Tool.Rect, Geo("M4,6 H20 V18 H4 Z")),
            ("Ellipse", Tool.Ellipse, Geo("M3,12 A9,6 0 1 0 21,12 A9,6 0 1 0 3,12")),
            ("Stift", Tool.Pen, Geo("M3,17 C6,5 9,5 12,12 S18,19 21,7")),
            ("Marker", Tool.Marker, Glyph("\uE7E6")),
            ("Text", Tool.Text, Glyph("\uE8D2")),
            ("Nummer", Tool.Number, NumberIcon()),
            ("Verpixeln", Tool.Blur, Geo("M4,4 h6 v6 h-6 z M14,4 h6 v6 h-6 z M9,9 h6 v6 h-6 z M4,14 h6 v6 h-6 z M14,14 h6 v6 h-6 z")),
            ("Weichzeichnen", Tool.Soften, Geo("M12,3 C16,8 19,11 19,14.5 A7,7 0 0 1 5,14.5 C5,11 8,8 12,3 Z")),
            ("Zuschneiden", Tool.Crop, Glyph("\uE7A8")),
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
            (t == Tool.Crop ? bildGroup : toolGroup).Add(rb);
        }

        panels["Start"].Children.Add(Group("Bild", bildGroup.ToArray()));
        panels["Bild"].Children.Add(Group("Effekte",
            BigBtn(Geo("M5,5 H19 V19 H5 Z M9,9 H15 V15 H9 Z"), "Rahmen", (_, _) => ApplyImageFrame(), "Rahmen um das ganze Bild (Farbe und Breite wie gewählt)"),
            BigBtn(Geo("M4,4 H15 V15 H4 Z M18,7 V18 H7"), "Schatten", (_, _) => ApplyImageShadow(), "Schlagschatten um das ganze Bild (Rand mit weichem Schatten)"),
            BigBtn(Geo("M12,4 A8,8 0 1 0 12,20 A8,8 0 1 0 12,4 Z M12,4 V20"), "Graustufen", (_, _) => ApplyGrayscale(), "Bild in Graustufen umwandeln")));
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

        var palette = new[] { "#E53935", "#FB8C00", "#FDD835", "#43A047", "#1E88E5", "#8E24AA", "#000000", "#FFFFFF" };
        var grid = new UniformGrid { Rows = 2, Columns = 4, VerticalAlignment = VerticalAlignment.Center };
        foreach (var hex in palette)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var sw = new Border
            {
                Width = 22, Height = 22, Margin = new Thickness(3), CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(c), BorderThickness = new Thickness(2), Cursor = Cursors.Hand,
                BorderBrush = c == color ? Brushes.White : Brushes.Transparent, ToolTip = hex,
            };
            sw.MouseLeftButtonDown += (_, _) =>
            {
                color = c;
                if (editBox != null) { editBox.Foreground = Brush(); editBox.CaretBrush = Brush(); }   // Text wird gerade geschrieben
                else if (selected != null) ApplyColor(selected, c);
                foreach (var s2 in swatches) s2.BorderBrush = Brushes.Transparent;
                sw.BorderBrush = Brushes.White;
            };
            swatches.Add(sw);
            grid.Children.Add(sw);
        }
        panels["Start"].Children.Add(Group("Farben", grid));

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
            case Tool.Soften:
                cur = new Rectangle { Stroke = Brushes.White, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 }, Fill = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255)) };
                break;
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
        if ((tool == Tool.Blur || tool == Tool.Soften) && added is Shape bs)
        {
            Art.Children.Remove(added);
            var area = new Rect(Canvas.GetLeft(bs), Canvas.GetTop(bs), bs.Width, bs.Height);
            var px = tool == Tool.Soften ? MakeSoften(area) : MakePixelate(area);
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

    static bool IsRectLike(UIElement e) => e is Rectangle || e is Ellipse || e is Image;

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

    /// <summary>Weichgezeichneter Bereich (Gaußähnlich: dreifacher Boxfilter), Stärke über den Regler.</summary>
    UIElement? MakeSoften(Rect r)
    {
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

    /// <summary>Schlagschatten um das ganze Bild: Rand mit weichem Schatten (PNG: transparent, JPG: weiß).</summary>
    void ApplyImageShadow()
    {
        CommitText();
        Select(null);
        const int m = 28, dx = 6, dy = 8, rad = 7;
        var old = bmp;
        int iw = old.PixelWidth, ih = old.PixelHeight, W = iw + 2 * m, H = ih + 2 * m;
        bool transparent = settings.ImageFormat.Equals("png", StringComparison.OrdinalIgnoreCase);

        var mask = new byte[W * H * 4];
        for (int y = m + dy; y < m + dy + ih && y < H; y++)
            for (int x = m + dx; x < m + dx + iw && x < W; x++)
                mask[(y * W + x) * 4 + 3] = 255;
        BlurBgra(mask, W, H, rad);

        var img = new FormatConvertedBitmap(old, PixelFormats.Bgra32, null, 0);
        var ip = new byte[iw * ih * 4];
        img.CopyPixels(ip, iw * 4, 0);

        var outp = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++)
        {
            double a = mask[i * 4 + 3] / 255.0 * 0.5;
            int o = i * 4;
            if (transparent) { outp[o] = outp[o + 1] = outp[o + 2] = 0; outp[o + 3] = (byte)(a * 255 + 0.5); }
            else { byte v = (byte)(255 * (1 - a) + 0.5); outp[o] = outp[o + 1] = outp[o + 2] = v; outp[o + 3] = 255; }
        }
        for (int y = 0; y < ih; y++)
            Buffer.BlockCopy(ip, y * iw * 4, outp, ((y + m) * W + m) * 4, iw * 4);
        var nb = BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, outp, W * 4);
        nb.Freeze();

        Action doIt = () => { SetBitmap(nb); Shift(m, m); };
        Action undo = () => { SetBitmap(old); Shift(-m, -m); };
        doIt();
        Push(undo, doIt);
        RefreshSel();
        FitZoom();
    }

    /// <summary>Rahmen um das ganze Bild in der gewählten Farbe; Breite = Stärke-Regler (mindestens 2 px).</summary>
    void ApplyImageFrame()
    {
        CommitText();
        Select(null);
        var old = bmp;
        int b = (int)Math.Max(2, Math.Round(thick));
        int iw = old.PixelWidth, ih = old.PixelHeight, W = iw + 2 * b, H = ih + 2 * b;
        var img = new FormatConvertedBitmap(old, PixelFormats.Bgra32, null, 0);
        var ip = new byte[iw * ih * 4];
        img.CopyPixels(ip, iw * 4, 0);
        var outp = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++)
        {
            outp[i * 4] = color.B; outp[i * 4 + 1] = color.G; outp[i * 4 + 2] = color.R; outp[i * 4 + 3] = 255;
        }
        for (int y = 0; y < ih; y++)
            Buffer.BlockCopy(ip, y * iw * 4, outp, ((y + b) * W + b) * 4, iw * 4);
        var nb = BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, outp, W * 4);
        nb.Freeze();
        Action doIt = () => { SetBitmap(nb); Shift(b, b); };
        Action undo = () => { SetBitmap(old); Shift(-b, -b); };
        doIt();
        Push(undo, doIt);
        RefreshSel();
        FitZoom();
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
