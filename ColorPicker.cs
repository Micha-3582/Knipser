using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Knipser;

/// <summary>
/// Farbwähler (Spektrum, Helligkeit, Hex/RGB, Vorgaben, eigene Farbfelder). Bleibt beim Öffnen bedienbar neben dem Editor:
/// Ein Klick ins Bild nimmt dort eine Farbe auf (wie bei Photoshop) – dafür ruft der Editor <see cref="SetNewColor"/> auf.
/// </summary>
internal class ColorPickerWindow : Window
{
    public event Action<Color>? Applied;

    const double Size = 220;
    // Reihe 1: grelle Signalfarben (zum Markieren), Reihe 2: Standardfarben, Reihe 3: weitere
    static readonly string[] Presets =
    {
        "#FF0000", "#FF7A00", "#FFE600", "#00E640", "#00D5FF", "#0066FF", "#B300FF", "#FF00A8",
        "#E53935", "#FB8C00", "#FDD835", "#43A047", "#1E88E5", "#8E24AA", "#000000", "#FFFFFF",
        "#EC407A", "#26A69A", "#795548", "#3949AB", "#9E9E9E", "#37474F", "#CDDC39", "#00BCD4",
    };
    const int CustomSlots = 8;

    readonly Settings settings;
    readonly Color original;
    double h, s, v;
    bool busy;

    readonly SolidColorBrush hueBrush = new(Colors.Red);
    readonly Ellipse marker = new() { Width = 14, Height = 14, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false, Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 3, Opacity = 0.9 } };
    readonly Rectangle hueMarker = new() { Width = 30, Height = 5, Stroke = Brushes.White, StrokeThickness = 1.5, Fill = Brushes.Transparent, IsHitTestVisible = false, Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 3, Opacity = 0.9 } };
    readonly Border newBox = new() { Height = 44, CornerRadius = new CornerRadius(4, 4, 0, 0), BorderBrush = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)), BorderThickness = new Thickness(1) };
    readonly Border oldBox = new() { Height = 28, CornerRadius = new CornerRadius(0, 0, 4, 4), BorderBrush = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = "Ursprüngliche Farbe wiederherstellen" };
    readonly TextBox hex = Box(80), tr = Box(46), tg = Box(46), tb = Box(46);
    readonly WrapPanel customPanel = new() { Width = 8 * 28, HorizontalAlignment = HorizontalAlignment.Left };

    public Color Current => FromHsv(h, s, v);

    static TextBox Box(double w) => new()
    {
        Width = w, Padding = new Thickness(4, 2, 4, 2), VerticalContentAlignment = VerticalAlignment.Center,
        Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)), Foreground = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
    };

    public ColorPickerWindow(Color start, Settings settings)
    {
        this.settings = settings;
        original = start;
        Title = "Farbwähler"; Icon = App.WindowIcon;
        SizeToContent = SizeToContent.WidthAndHeight; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1B));
        Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));
        Loaded += (_, _) =>
        {
            if (Owner == null) return;
            Left = Math.Max(0, Owner.Left + Owner.ActualWidth - ActualWidth - 24);
            Top = Owner.Top + 165;
        };

        // --- Spektrumfläche (Sättigung nach rechts, Helligkeit nach oben) ---
        var sv = new Grid { Width = Size, Height = Size, Cursor = Cursors.Cross, ClipToBounds = true, Background = hueBrush };
        sv.Children.Add(new Rectangle { Fill = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), new Point(0, 0), new Point(1, 0)) });
        sv.Children.Add(new Rectangle { Fill = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, new Point(0, 0), new Point(0, 1)) });
        var svCanvas = new Canvas { IsHitTestVisible = false };
        svCanvas.Children.Add(marker);
        sv.Children.Add(svCanvas);
        void PickSv(MouseEventArgs e)
        {
            var p = e.GetPosition(sv);
            s = Math.Clamp(p.X / Size, 0, 1); v = 1 - Math.Clamp(p.Y / Size, 0, 1);
            Update(null);
        }
        sv.MouseLeftButtonDown += (_, e) => { sv.CaptureMouse(); PickSv(e); };
        sv.MouseMove += (_, e) => { if (sv.IsMouseCaptured) PickSv(e); };
        sv.MouseLeftButtonUp += (_, _) => sv.ReleaseMouseCapture();

        // --- Farbtonleiste ---
        var hueGrid = new Grid { Width = 30, Height = Size, Margin = new Thickness(12, 0, 0, 0), Cursor = Cursors.Hand };
        var gb = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        string[] stops = { "#FF0000", "#FFFF00", "#00FF00", "#00FFFF", "#0000FF", "#FF00FF", "#FF0000" };
        for (int i = 0; i < stops.Length; i++) gb.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(stops[i]), i / 6.0));
        hueGrid.Children.Add(new Rectangle { Fill = gb, Margin = new Thickness(0, 0, 0, 0) });
        var hueCanvas = new Canvas { IsHitTestVisible = false };
        hueCanvas.Children.Add(hueMarker);
        hueGrid.Children.Add(hueCanvas);
        void PickHue(MouseEventArgs e)
        {
            var p = e.GetPosition(hueGrid);
            h = Math.Clamp(p.Y / Size, 0, 1) * 359.999;
            Update(null);
        }
        hueGrid.MouseLeftButtonDown += (_, e) => { hueGrid.CaptureMouse(); PickHue(e); };
        hueGrid.MouseMove += (_, e) => { if (hueGrid.IsMouseCaptured) PickHue(e); };
        hueGrid.MouseLeftButtonUp += (_, _) => hueGrid.ReleaseMouseCapture();

        // --- rechte Spalte: neu/aktuell, Felder ---
        var right = new StackPanel { Margin = new Thickness(14, 0, 0, 0), Width = 150 };
        right.Children.Add(Cap("neu"));
        right.Children.Add(newBox);
        right.Children.Add(oldBox);
        right.Children.Add(Cap("aktuell"));
        oldBox.Background = new SolidColorBrush(start);
        oldBox.MouseLeftButtonDown += (_, _) => SetNewColor(original);
        right.Children.Add(FieldRow("#", hex));
        right.Children.Add(FieldRow("R", tr));
        right.Children.Add(FieldRow("G", tg));
        right.Children.Add(FieldRow("B", tb));
        hex.TextChanged += (_, _) =>
        {
            if (busy) return;
            var t = hex.Text.Trim().TrimStart('#');
            if (t.Length != 6) return;
            try { SetFromRgb((Color)ColorConverter.ConvertFromString("#" + t), hex); } catch { }
        };
        foreach (var t in new[] { tr, tg, tb })
            t.TextChanged += (_, _) =>
            {
                if (busy) return;
                if (int.TryParse(tr.Text, out int r) && int.TryParse(tg.Text, out int g) && int.TryParse(tb.Text, out int b)
                    && r is >= 0 and <= 255 && g is >= 0 and <= 255 && b is >= 0 and <= 255)
                    SetFromRgb(Color.FromRgb((byte)r, (byte)g, (byte)b), t);
            };

        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(sv); top.Children.Add(hueGrid); top.Children.Add(right);

        // --- Vorgaben und eigene Farben ---
        var presetPanel = new WrapPanel { Width = 8 * 28, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var hx in Presets)
        {
            var c = (Color)ColorConverter.ConvertFromString(hx);
            presetPanel.Children.Add(Dot(c, hx, () => SetNewColor(c)));
        }
        var sample = new ComboBox { Width = 190, Margin = new Thickness(0, 0, 0, 0) };
        int[] sizes = { 1, 3, 5, 11, 31 };
        foreach (var n in sizes) sample.Items.Add(n == 1 ? "1 Pixel" : $"Durchschnitt {n} × {n} Pixel");
        sample.SelectedIndex = Math.Max(0, Array.IndexOf(sizes, settings.PickSample));
        sample.SelectionChanged += (_, _) => { if (sample.SelectedIndex >= 0) { settings.PickSample = sizes[sample.SelectedIndex]; settings.Save(); } };
        var sampleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        sampleRow.Children.Add(new TextBlock { Text = "Pipette nimmt", Width = 96, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray });
        sampleRow.Children.Add(sample);
        var addBtn = new Button { Content = "Aktuelle Farbe in ein freies Feld speichern", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        addBtn.Click += (_, _) => AddCustom(Current);
        BuildCustom();

        var ok = new Button { Content = "OK", Padding = new Thickness(22, 5, 22, 5), IsDefault = true };
        var cancel = new Button { Content = "Abbrechen", Padding = new Thickness(18, 5, 18, 5), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        ok.Click += (_, _) => { Applied?.Invoke(Current); Close(); };
        cancel.Click += (_, _) => Close();
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        btns.Children.Add(ok); btns.Children.Add(cancel);

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(top);
        root.Children.Add(Head("Vorgaben"));
        root.Children.Add(presetPanel);
        root.Children.Add(Head("Eigene Farben"));
        root.Children.Add(customPanel);
        root.Children.Add(addBtn);
        root.Children.Add(sampleRow);
        root.Children.Add(new TextBlock { Text = "Tipp: Mit der Maus ins Bild fahren und klicken, um dort eine Farbe aufzunehmen.", Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 400 });
        root.Children.Add(btns);
        Content = root;

        ToColorParts(start);
        Update(null);
    }

    // ---------- Aufbauhilfen ----------
    static TextBlock Cap(string t) => new() { Text = t, FontSize = 11, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 4) };
    static TextBlock Head(string t) => new() { Text = t, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5)), Margin = new Thickness(0, 12, 0, 4) };

    static UIElement FieldRow(string label, TextBox box)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
        p.Children.Add(new TextBlock { Text = label, Width = 22, VerticalAlignment = VerticalAlignment.Center });
        p.Children.Add(box);
        return p;
    }

    static Border Dot(Color c, string tip, Action click)
    {
        var b = new Border { Width = 24, Height = 24, Margin = new Thickness(2), CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(c), BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = tip };
        b.MouseLeftButtonDown += (_, _) => click();
        return b;
    }

    void BuildCustom()
    {
        customPanel.Children.Clear();
        while (settings.CustomColors.Count < CustomSlots) settings.CustomColors.Add("");
        for (int i = 0; i < CustomSlots; i++)
        {
            int slot = i;
            var hx = settings.CustomColors[i];
            Border b;
            if (string.IsNullOrEmpty(hx))
            {
                b = new Border { Width = 24, Height = 24, Margin = new Thickness(2), CornerRadius = new CornerRadius(12), BorderBrush = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)), BorderThickness = new Thickness(1.5), Background = Brushes.Transparent, ToolTip = "Freies Feld" };
            }
            else
            {
                Color c;
                try { c = (Color)ColorConverter.ConvertFromString(hx); } catch { c = Colors.Gray; }
                b = Dot(c, hx + "  (Rechtsklick = löschen)", () => SetNewColor(c));
                b.MouseRightButtonDown += (_, _) => { settings.CustomColors[slot] = ""; settings.Save(); BuildCustom(); };
            }
            customPanel.Children.Add(b);
        }
    }

    void AddCustom(Color c)
    {
        var hx = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        while (settings.CustomColors.Count < CustomSlots) settings.CustomColors.Add("");
        int slot = settings.CustomColors.FindIndex(x => string.IsNullOrEmpty(x));
        if (slot < 0) { settings.CustomColors.RemoveAt(0); settings.CustomColors.Add(hx); }
        else settings.CustomColors[slot] = hx;
        settings.Save();
        BuildCustom();
    }

    // ---------- Live-Vorschau (Maus über dem Bild) ----------
    /// <summary>Zeigt die Farbe unter dem Mauszeiger im großen „neu“-Feld, ohne sie schon zu übernehmen.</summary>
    public void ShowPreview(Color c)
    {
        newBox.Background = new SolidColorBrush(c);
        busy = true;
        hex.Text = $"{c.R:X2}{c.G:X2}{c.B:X2}";
        tr.Text = c.R.ToString(); tg.Text = c.G.ToString(); tb.Text = c.B.ToString();
        busy = false;
    }

    /// <summary>Zurück zur tatsächlich gewählten Farbe, wenn die Maus das Bild verlässt.</summary>
    public void ClearPreview() => Update(null);

    // ---------- Farbe setzen ----------
    /// <summary>Neue Farbe von außen setzen (z. B. aus dem Bild aufgenommen).</summary>
    public void SetNewColor(Color c)
    {
        ToColorParts(c);
        Update(null);
    }

    void SetFromRgb(Color c, TextBox? skip)
    {
        ToColorParts(c);
        Update(skip);
    }

    void ToColorParts(Color c)
    {
        ToHsv(c, out double nh, out double ns, out double nv);
        if (ns > 0.001 && nv > 0.001) h = nh;   // bei Grau/Schwarz den Farbton behalten
        s = ns; v = nv;
    }

    void Update(TextBox? skip)
    {
        var c = Current;
        newBox.Background = new SolidColorBrush(c);
        hueBrush.Color = FromHsv(h, 1, 1);
        Canvas.SetLeft(marker, s * Size - 7); Canvas.SetTop(marker, (1 - v) * Size - 7);
        Canvas.SetLeft(hueMarker, 0); Canvas.SetTop(hueMarker, h / 359.999 * Size - 2.5);
        busy = true;
        if (skip != hex) hex.Text = $"{c.R:X2}{c.G:X2}{c.B:X2}";
        if (skip != tr) tr.Text = c.R.ToString();
        if (skip != tg) tg.Text = c.G.ToString();
        if (skip != tb) tb.Text = c.B.ToString();
        busy = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) Close();
    }

    // ---------- Farbumrechnung ----------
    public static Color FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c, r, g, b;
        if (h < 60) (r, g, b) = (c, x, 0);
        else if (h < 120) (r, g, b) = (x, c, 0);
        else if (h < 180) (r, g, b) = (0, c, x);
        else if (h < 240) (r, g, b) = (0, x, c);
        else if (h < 300) (r, g, b) = (x, 0, c);
        else (r, g, b) = (c, 0, x);
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    public static void ToHsv(Color c, out double h, out double s, out double v)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        v = max;
        s = max <= 0 ? 0 : d / max;
        if (d <= 0) h = 0;
        else if (max == r) h = 60 * (((g - b) / d) % 6);
        else if (max == g) h = 60 * ((b - r) / d + 2);
        else h = 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
    }
}
