using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Knipser;

/// <summary>Gemeinsame Hülle für kleine dunkle Dialoge.</summary>
internal abstract class DarkDialog : Window
{
    protected readonly StackPanel Body = new() { Margin = new Thickness(18, 16, 18, 8) };
    protected readonly Button Ok = new() { Content = "OK", Padding = new Thickness(18, 5, 18, 5), IsDefault = true };

    protected DarkDialog(string title)
    {
        Title = title; Icon = App.WindowIcon;
        SizeToContent = SizeToContent.WidthAndHeight; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1B));
        Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));
        var cancel = new Button { Content = "Abbrechen", Padding = new Thickness(18, 5, 18, 5), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(18, 4, 18, 16) };
        row.Children.Add(Ok); row.Children.Add(cancel);
        var dock = new DockPanel();
        DockPanel.SetDock(row, Dock.Bottom);
        dock.Children.Add(row); dock.Children.Add(Body);
        Content = dock;
        Ok.Click += (_, _) => { DialogResult = true; };
    }

    protected static TextBox Field(double width = 80) => new()
    {
        Width = width, Padding = new Thickness(5, 3, 5, 3),
        Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)), Foreground = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
    };

    protected static UIElement Line(string label, UIElement ctl)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        p.Children.Add(new TextBlock { Text = label, Width = 120, VerticalAlignment = VerticalAlignment.Center });
        p.Children.Add(ctl);
        return p;
    }
}

/// <summary>Bildgröße ändern: Breite/Höhe in Pixeln oder Prozent, optional mit festem Seitenverhältnis.</summary>
internal class ResizeDialog : DarkDialog
{
    public int NewWidth { get; private set; }
    public int NewHeight { get; private set; }
    readonly int w0, h0;
    readonly TextBox tw = Field(), th = Field(), tp = Field(60);
    readonly CheckBox keep = new() { Content = "Seitenverhältnis beibehalten", IsChecked = true, Margin = new Thickness(0, 6, 0, 2) };
    bool busy;

    public ResizeDialog(int w, int h) : base("Bildgröße ändern")
    {
        w0 = w; h0 = h; NewWidth = w; NewHeight = h;
        keep.Foreground = Foreground;
        Body.Children.Add(new TextBlock { Text = $"Aktuell: {w} × {h} Pixel", Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 6) });
        Body.Children.Add(Line("Breite (px)", tw));
        Body.Children.Add(Line("Höhe (px)", th));
        Body.Children.Add(Line("Prozent", tp));
        Body.Children.Add(keep);
        tw.Text = w.ToString(); th.Text = h.ToString(); tp.Text = "100";
        tw.TextChanged += (_, _) =>
        {
            if (busy || !int.TryParse(tw.Text, out int v) || v < 1) return;
            busy = true;
            if (keep.IsChecked == true) th.Text = Math.Max(1, (int)Math.Round(v * (double)h0 / w0)).ToString();
            tp.Text = Math.Round(v * 100.0 / w0).ToString();
            busy = false;
        };
        th.TextChanged += (_, _) =>
        {
            if (busy || !int.TryParse(th.Text, out int v) || v < 1) return;
            busy = true;
            if (keep.IsChecked == true) tw.Text = Math.Max(1, (int)Math.Round(v * (double)w0 / h0)).ToString();
            tp.Text = Math.Round(v * 100.0 / h0).ToString();
            busy = false;
        };
        tp.TextChanged += (_, _) =>
        {
            if (busy || !double.TryParse(tp.Text, out double v) || v < 1) return;
            busy = true;
            tw.Text = Math.Max(1, (int)Math.Round(w0 * v / 100)).ToString();
            th.Text = Math.Max(1, (int)Math.Round(h0 * v / 100)).ToString();
            busy = false;
        };
        Ok.Click += (_, _) =>
        {
            if (int.TryParse(tw.Text, out int a) && int.TryParse(th.Text, out int b) && a >= 1 && b >= 1 && a <= 20000 && b <= 20000)
            { NewWidth = a; NewHeight = b; }
        };
    }
}

/// <summary>Helligkeit und Kontrast mit Live-Vorschau im Editor.</summary>
internal class BrightnessDialog : DarkDialog
{
    public int Brightness => (int)bs.Value;
    public int Contrast => (int)cs.Value;
    readonly Slider bs = new() { Minimum = -100, Maximum = 100, Width = 200, IsSnapToTickEnabled = true, TickFrequency = 1 };
    readonly Slider cs = new() { Minimum = -100, Maximum = 100, Width = 200, IsSnapToTickEnabled = true, TickFrequency = 1 };

    public BrightnessDialog(Action<int, int> preview) : base("Helligkeit und Kontrast")
    {
        var bt = new TextBlock { Width = 36, Text = "0", TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        var ct = new TextBlock { Width = 36, Text = "0", TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        var r1 = new StackPanel { Orientation = Orientation.Horizontal }; r1.Children.Add(bs); r1.Children.Add(bt);
        var r2 = new StackPanel { Orientation = Orientation.Horizontal }; r2.Children.Add(cs); r2.Children.Add(ct);
        Body.Children.Add(Line("Helligkeit", r1));
        Body.Children.Add(Line("Kontrast", r2));
        void Upd() { bt.Text = ((int)bs.Value).ToString(); ct.Text = ((int)cs.Value).ToString(); preview((int)bs.Value, (int)cs.Value); }
        bs.ValueChanged += (_, _) => Upd();
        cs.ValueChanged += (_, _) => Upd();
    }
}

/// <summary>Bildrahmen: Breite und Farbe wählen.</summary>
internal class FrameDialog : DarkDialog
{
    public int FrameWidth => (int)ws.Value;
    public Color FrameColor { get; private set; }
    readonly Slider ws = new() { Minimum = 1, Maximum = 100, Width = 200, IsSnapToTickEnabled = true, TickFrequency = 1 };
    readonly TextBox hex = Field(90);
    readonly System.Collections.Generic.List<Border> swatches = new();
    bool busy;

    static readonly string[] Palette = { "#E53935", "#FB8C00", "#FDD835", "#43A047", "#1E88E5", "#8E24AA", "#000000", "#FFFFFF", "#9E9E9E", "#37474F" };

    public FrameDialog(int width, Color color) : base("Rahmen um das Bild")
    {
        FrameColor = color;
        ws.Value = Math.Clamp(width, 1, 100);
        var wt = new TextBlock { Width = 50, Text = $"{(int)ws.Value} px", TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        var r1 = new StackPanel { Orientation = Orientation.Horizontal }; r1.Children.Add(ws); r1.Children.Add(wt);
        Body.Children.Add(Line("Breite", r1));
        ws.ValueChanged += (_, _) => wt.Text = $"{(int)ws.Value} px";

        var sw = new WrapPanel { Width = 230 };
        foreach (var h in Palette)
        {
            var c = (Color)ColorConverter.ConvertFromString(h);
            var b = new Border
            {
                Width = 24, Height = 24, Margin = new Thickness(3), CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(c),
                BorderThickness = new Thickness(2), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = h, Tag = c,
            };
            b.MouseLeftButtonDown += (_, _) => Pick(c);
            swatches.Add(b); sw.Children.Add(b);
        }
        Body.Children.Add(Line("Farbe", sw));
        Body.Children.Add(Line("Farbcode", hex));
        hex.TextChanged += (_, _) =>
        {
            if (busy) return;
            try { var c = (Color)ColorConverter.ConvertFromString(hex.Text.Trim()); busy = true; Pick(c, false); busy = false; } catch { }
        };
        Pick(color);
    }

    void Pick(Color c, bool setHex = true)
    {
        FrameColor = c;
        foreach (var b in swatches) b.BorderBrush = (Color)b.Tag == c ? Brushes.White : Brushes.Transparent;
        if (setHex) { busy = true; hex.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}"; busy = false; }
    }
}

/// <summary>Schlagschatten um das Bild: Abstand, Weichheit und Deckkraft.</summary>
internal class ShadowDialog : DarkDialog
{
    public int Offset => (int)os.Value;
    public int Blur => (int)bs.Value;
    public int Opacity => (int)ps.Value;
    readonly Slider os = new() { Minimum = 0, Maximum = 30, Width = 200, IsSnapToTickEnabled = true, TickFrequency = 1 };
    readonly Slider bs = new() { Minimum = 1, Maximum = 30, Width = 200, IsSnapToTickEnabled = true, TickFrequency = 1 };
    readonly Slider ps = new() { Minimum = 10, Maximum = 100, Width = 200, IsSnapToTickEnabled = true, TickFrequency = 1 };

    public ShadowDialog(int offset, int blur, int opacity) : base("Schlagschatten um das Bild")
    {
        os.Value = Math.Clamp(offset, 0, 30); bs.Value = Math.Clamp(blur, 1, 30); ps.Value = Math.Clamp(opacity, 10, 100);
        void Row(string label, Slider s, string unit)
        {
            var t = new TextBlock { Width = 50, Text = $"{(int)s.Value}{unit}", TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            var r = new StackPanel { Orientation = Orientation.Horizontal }; r.Children.Add(s); r.Children.Add(t);
            s.ValueChanged += (_, _) => t.Text = $"{(int)s.Value}{unit}";
            Body.Children.Add(Line(label, r));
        }
        Row("Abstand", os, " px"); Row("Weichheit", bs, " px"); Row("Deckkraft", ps, " %");
    }
}
