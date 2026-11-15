using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Knipser;

/// <summary>Schnellzugriffsleiste: Am oberen Bildschirmrand liegt ein kleiner Griff. Fährt die Maus dort hinein,
/// fährt die Leiste mit den Grundfunktionen heraus und nach dem Verlassen wieder ein.</summary>
internal class QuickBar : Window
{
    const double BarW = 640, BarH = 74, HandleW = 150, HandleH = 4;
    static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));

    readonly Border bar = new()
    {
        CornerRadius = new CornerRadius(0, 0, 14, 14), Opacity = 0, Width = BarW, Height = BarH,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
        Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x20, 0x20, 0x22)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x48)), BorderThickness = new Thickness(1, 0, 1, 1)
    };
    readonly Border pill = new()
    {
        Width = HandleW, Height = HandleH, CornerRadius = new CornerRadius(0, 0, 3, 3),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
        Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x3D, 0x9B, 0xFF))
    };
    readonly DispatcherTimer poll = new() { Interval = TimeSpan.FromMilliseconds(15) };
    double t;              // 0 = eingefahren, 1 = ausgefahren
    bool opening;
    DateTime sinceIn = DateTime.MaxValue, sinceOut = DateTime.MaxValue;
    bool suspended;
    double scale = 1;

    double offset;                 // Verschiebung gegen die Bildschirmmitte in DIPs
    readonly Action<double, string> saveOffset;
    bool dragging, pressed;
    Point pressPos;                // Bildschirmposition (Pixel) beim Drücken
    double grabDx;                 // Abstand Mauszeiger – Leistenmitte beim Greifen (Pixel)
    string screenName;             // Monitor, an dessen oberem Rand die Leiste sitzt

    /// <summary>Der Monitor der Leiste (fehlt er, z. B. Kabel gezogen, gilt der Hauptbildschirm).</summary>
    Forms.Screen Scr()
    {
        foreach (var s in Forms.Screen.AllScreens) if (s.DeviceName == screenName) return s;
        return Forms.Screen.PrimaryScreen!;
    }

    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Native.POINT pt, uint flags);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr hMon, int type, out uint x, out uint y);
    /// <summary>Skalierungsfaktor eines Monitors (1,0 = 100 %).</summary>
    static double MonitorScale(Forms.Screen s)
    {
        try
        {
            var m = MonitorFromPoint(new Native.POINT { X = s.Bounds.X + 1, Y = s.Bounds.Y + 1 }, 2);
            if (GetDpiForMonitor(m, 0, out var dx, out _) == 0 && dx > 0) return dx / 96.0;
        }
        catch { }
        return 1;
    }

    public QuickBar(IList<(string Label, Func<UIElement> Icon, Action Run)?> items, double startOffset, string startScreen, Action<double, string> saveOffset)
    {
        offset = startOffset; screenName = startScreen; this.saveOffset = saveOffset;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; Topmost = true; ShowActivated = false; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = "Knipser Schnellzugriff";

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        foreach (var it in items)
        {
            if (it is null)
            {
                row.Children.Add(new Rectangle { Width = 1, Height = 38, Fill = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x4E)), Margin = new Thickness(6, 0, 6, 0) });
                continue;
            }
            var (label, icon, run) = it.Value;
            var sp = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
            var ic = icon();
            ic.IsHitTestVisible = false;
            sp.Children.Add(new Viewbox { Width = 26, Height = 26, Child = ic, Margin = new Thickness(0, 0, 0, 4), IsHitTestVisible = false });
            sp.Children.Add(new TextBlock { Text = label, Foreground = Ink, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false });
            var b = new Border { Child = sp, Padding = new Thickness(8, 6, 8, 6), MinWidth = 58, CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            b.MouseEnter += (_, _) => b.Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x40));
            b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonUp += async (_, _) =>
            {
                Suspend();
                await Task.Delay(320);   // Leiste ist sicher weg, bevor fotografiert wird
                run();
            };
            row.Children.Add(b);
        }
        bar.Child = row;
        bar.RenderTransformOrigin = new Point(0.5, 0);
        bar.RenderTransform = new TransformGroup { Children = { barScale, barMove } };
        // Mit gedrückter linker Maustaste seitlich verschieben (auch beginnend auf einem Knopf: erst ab 5 px Bewegung)
        bar.PreviewMouseLeftButtonDown += (_, e) =>
        {
            Native.GetCursorPos(out var p);
            pressed = true; dragging = false; pressPos = new Point(p.X, p.Y);
            var hw = new WindowInteropHelper(this).Handle;
            grabDx = Native.GetWindowRect(hw, out var wr) ? p.X - (wr.Left + wr.Right) / 2.0 : 0;
        };
        bar.PreviewMouseMove += (_, e) =>
        {
            if (!pressed) return;
            Native.GetCursorPos(out var p);
            double dx = p.X - pressPos.X;
            if (!dragging && Math.Abs(dx) < 5) return;
            if (!dragging) bar.CaptureMouse();
            dragging = true;
            // Monitor unter dem Mauszeiger (nach links/rechts über die Monitorgrenze ziehen wechselt den Monitor)
            Forms.Screen target = Scr();
            foreach (var s in Forms.Screen.AllScreens)
                if (p.X >= s.Bounds.Left && p.X < s.Bounds.Right) { target = s; break; }
            bool changed = target.DeviceName != Scr().DeviceName;
            if (changed) { screenName = target.DeviceName; scale = MonitorScale(target); }
            var sb = target.Bounds;
            double wpx = Math.Min(BarW * scale, sb.Width);
            double offPx = Math.Clamp(p.X - grabDx - (sb.X + sb.Width / 2.0), -(sb.Width - wpx) / 2, (sb.Width - wpx) / 2);
            offset = offPx / scale;
            if (changed) Place(true); else MoveOnly();
        };
        bar.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!pressed) return;
            pressed = false; bar.ReleaseMouseCapture();
            if (dragging) { dragging = false; e.Handled = true; saveOffset(offset, screenName); }
        };
        var root = new Grid { ClipToBounds = true };
        root.Children.Add(pill); root.Children.Add(bar);
        Content = root;

        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            Native.SetWindowLong(h, Native.GWL_EXSTYLE, Native.GetWindowLong(h, Native.GWL_EXSTYLE) | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
        };
        Loaded += (_, _) => { Place(true); poll.Start(); };
        poll.Tick += (_, _) => Tick();
        anim.Tick += Frame;
    }

    bool wasFull;
    readonly ScaleTransform barScale = new(0.6, 1);
    readonly TranslateTransform barMove = new(0, -BarH);

    double ClampOffset(double o)
    {
        var sb = Scr().Bounds;
        double max = (sb.Width / scale - BarW) / 2;
        return Math.Clamp(o, -max, max);
    }

    bool placed;

    /// <summary>Setzt die Leiste in Pixeln an den oberen Rand ihres Monitors (unabhängig von Skalierung und Windows-Startposition).</summary>
    void PlaceWindow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var sb = Scr().Bounds;
        if (!dragging) offset = ClampOffset(offset);
        bool full = t > 0;
        // 1) erst auf den Hauptbildschirm holen, damit die richtige Skalierung gilt (nur wenn das Fenster nicht schon dort liegt)
        if (!Native.GetWindowRect(hwnd, out var cur) || cur.Left < sb.Left || cur.Right > sb.Right || cur.Top < sb.Top || cur.Top > sb.Top + 50)
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, sb.X + sb.Width / 2, sb.Y, 0, 0, 0x0001 | Native.SWP_NOACTIVATE);   // SWP_NOSIZE
        scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Width = full ? BarW : HandleW;
        Height = full ? BarH : HandleH;
        // 2) dann mittig (plus Verschiebung) ganz oben
        int x = (int)Math.Round(sb.X + (sb.Width - Width * scale) / 2 + offset * scale);
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, sb.Y, 0, 0, 0x0001 | Native.SWP_NOACTIVATE);
        wasFull = full; placed = true;
    }

    /// <summary>Beim Ziehen: nur seitlich verschieben, ein einziger Aufruf pro Mausbewegung (kein Flackern).</summary>
    void MoveOnly()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !Native.GetWindowRect(hwnd, out var r)) return;
        var sb = Scr().Bounds;
        int x = (int)Math.Round(sb.X + (sb.Width - (r.Right - r.Left)) / 2.0 + offset * scale);
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, sb.Y, 0, 0, 0x0001 | Native.SWP_NOACTIVATE);
    }

    /// <summary>Sitzt das Fenster noch dort, wo es hingehört? (Windows oder andere Programme könnten es verschoben haben.)</summary>
    bool PositionOk()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !Native.GetWindowRect(hwnd, out var r)) return true;
        var sb = Scr().Bounds;
        double w = r.Right - r.Left;
        double x = sb.X + (sb.Width - w) / 2 + offset * scale;
        return Math.Abs(r.Left - x) <= 2 && r.Top == sb.Y;
    }

    void Place(bool reposition = false)
    {
        bool full = t > 0;
        if (reposition || !placed || full != wasFull) PlaceWindow();
        double e = 1 - Math.Pow(1 - t, 3);          // weich abbremsen
        barMove.Y = -(1 - e) * BarH;
        barScale.ScaleX = 0.6 + 0.4 * e;
        bar.Opacity = Math.Min(1, e * 1.6);
        pill.Opacity = 1 - e;
    }

    /// <summary>Liegt der Mauszeiger im (ggf. vergrößerten) Bereich der Leiste?</summary>
    bool CursorIn(double margin, bool handleOnly)
    {
        Native.GetCursorPos(out var p);
        var sb = Scr().Bounds;
        double cx = sb.X + sb.Width / 2.0 + offset * scale;
        double w = (handleOnly ? HandleW + 120 : BarW) * scale / 2 + margin;
        double h = (handleOnly ? 4 : BarH * scale) + margin;
        return p.X >= cx - w && p.X <= cx + w && p.Y >= sb.Y - 5 && p.Y <= sb.Y + h;
    }

    static bool LeftDown() => (GetAsyncKeyState(0x01) & 0x8000) != 0;
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);

    /// <summary>Vollbild-Programm (Spiel, Video) im Vordergrund: Leiste nicht ausfahren.</summary>
    bool ForegroundFullscreen()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero || !Native.GetWindowRect(h, out var r)) return false;
        var sb = Scr().Bounds;
        if (r.Left > sb.Left || r.Top > sb.Top || r.Right < sb.Right || r.Bottom < sb.Bottom) return false;
        // Maximierte Fenster ragen ein paar Pixel über den Rand, haben aber eine Titelleiste: das ist kein Vollbild
        if ((Native.GetWindowLong(h, -16) & 0x00C00000) != 0) return false;
        var cls = new System.Text.StringBuilder(64);
        GetClassName(h, cls, 64);
        var c = cls.ToString();
        return c != "WorkerW" && c != "Progman" && c != "Shell_TrayWnd";
    }

    bool fsHidden;                  // Vollbild-Programm im Vordergrund: Leiste samt Griff komplett ausgeblendet
    DateTime nextFsCheck = DateTime.MinValue;

    void Tick()
    {
        var t0 = DateTime.UtcNow;
        if (t0 >= nextFsCheck)
        {
            nextFsCheck = t0.AddMilliseconds(250);
            bool fs = ForegroundFullscreen();
            if (fs && !fsHidden)
            {
                fsHidden = true; opening = false; anim.Stop(); t = 0; sinceIn = sinceOut = DateTime.MaxValue;
                Place();
                Hide();
            }
            else if (!fs && fsHidden)
            {
                fsHidden = false;
                if (!suspended) Show();
            }
        }
        if (suspended || fsHidden) return;
        if (!dragging && placed && !PositionOk()) Place(true);
        var now = DateTime.UtcNow;
        if (!opening)
        {
            // Sofort ausfahren, auch mitten im Einfahren (nicht beim Ziehen mit gedrückter Maustaste und nicht über Vollbild-Programmen)
            bool inZone = t > 0 ? CursorIn(14, false) : CursorIn(0, true);
            if (inZone && !LeftDown() && !ForegroundFullscreen()) { opening = true; sinceOut = DateTime.MaxValue; }
        }
        else if (dragging || pressed) sinceOut = DateTime.MaxValue;
        else if (opening)
        {
            if (CursorIn(14, false)) sinceOut = DateTime.MaxValue;
            else if (sinceOut == DateTime.MaxValue) sinceOut = now;
            else if ((now - sinceOut).TotalMilliseconds > 450) { opening = false; sinceOut = DateTime.MaxValue; }
        }
        if (t != (opening ? 1 : 0)) KickAnim();
    }

    readonly System.Diagnostics.Stopwatch clock = new();
    // Eigener Takt statt CompositionTarget.Rendering: Das feuert im Leerlauf nicht und ließ die Leiste manchmal 1-2 s hängen
    readonly DispatcherTimer anim = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(8) };

    void KickAnim()
    {
        if (anim.IsEnabled) return;
        clock.Restart();
        anim.Start();
    }

    /// <summary>Läuft im Bildwiederholtakt des Monitors, daher flüssig.</summary>
    void Frame(object? sender, EventArgs e)
    {
        double dt = clock.Elapsed.TotalSeconds; clock.Restart();
        dt = Math.Min(dt, 0.05);
        double target = opening ? 1 : 0;
        if (t == target) { anim.Stop(); return; }
        double speed = opening ? 1 / 0.26 : 1 / 0.20;
        t = target > t ? Math.Min(target, t + dt * speed) : Math.Max(target, t - dt * speed);
        Place();
    }

    /// <summary>Leiste sofort ausblenden (vor einer Aufnahme).</summary>
    public void Suspend()
    {
        suspended = true; opening = false; anim.Stop(); t = 0; sinceIn = sinceOut = DateTime.MaxValue;
        Place();
        Hide();
    }

    public void Resume()
    {
        if (!suspended) return;
        suspended = false;
        if (!fsHidden) Show();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) { base.OnDpiChanged(oldDpi, newDpi); Place(true); }

    protected override void OnClosed(EventArgs e) { poll.Stop(); anim.Stop(); base.OnClosed(e); }

    // ---------- Symbole ----------
    static Path P(string data, double th = 1.8) => new()
    {
        Data = Geometry.Parse(data), Stroke = Ink, StrokeThickness = th, StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Fill = Brushes.Transparent
    };
    static UIElement Icon(params Path[] ps)
    {
        var c = new Canvas { Width = 24, Height = 24 };
        foreach (var p in ps) c.Children.Add(p);
        return c;
    }
    public static UIElement IcRegion()
    {
        var box = P("M4,4 H20 V20 H4 Z");
        box.StrokeDashArray = new DoubleCollection { 2.2, 1.6 };
        return Icon(box, P("M9,15 L15,15 M15,9 L15,15", 2.2));
    }
    public static UIElement IcFull() => Icon(P("M3,3 H21 V17 H3 Z"), P("M8,21 H16 M12,17 V21"));
    public static UIElement IcWindow() => Icon(P("M3,4 H21 V20 H3 Z"), P("M3,9 H21"), P("M6,6.5 H6.1 M9,6.5 H9.1", 2.2));
    public static UIElement IcScroll() => Icon(P("M4,3 H20 V21 H4 Z"), P("M12,7 V17 M8,13 L12,17 L16,13"));
    public static UIElement IcFree() => Icon(P("M4,15 C2,9 8,3 13,5 C19,7 21,12 16,15 C13,17 11,14 9,17 C8,19 6,20 4,19"));
    public static UIElement IcPicker() => Icon(P("M14,4 L20,10 M16,6 L7,15 L5,20 L10,18 L19,9"));
    public static UIElement IcLoupe() => Icon(P("M10,3 A7,7 0 1 0 10,17 A7,7 0 1 0 10,3 Z"), P("M15,15 L21,21", 2.6), P("M10,7 V13 M7,10 H13", 1.4));
    public static UIElement IcRuler() => Icon(P("M2,8 H22 V16 H2 Z"), P("M6,8 V12 M10,8 V11 M14,8 V12 M18,8 V11"));
    /// <summary>Zahnrad im gleichen Strich-Stil und in der gleichen Größe (18 px Durchmesser in 24er Fläche) wie die übrigen Symbole.</summary>
    public static UIElement IcGear()
    {
        const double cx = 12, cy = 12, R = 9, r = 6.9;
        var sg = new StreamGeometry();
        using (var c = sg.Open())
        {
            Point Pt(double deg, double rad) => new(cx + rad * Math.Cos(deg * Math.PI / 180), cy + rad * Math.Sin(deg * Math.PI / 180));
            for (int i = 0; i < 8; i++)
            {
                double a = i * 45;
                if (i == 0) c.BeginFigure(Pt(a - 11, r), false, true); else c.LineTo(Pt(a - 11, r), true, true);
                c.LineTo(Pt(a - 7, R), true, true);
                c.LineTo(Pt(a + 7, R), true, true);
                c.LineTo(Pt(a + 11, r), true, true);
            }
        }
        sg.Freeze();
        var body = new Path { Data = sg, Stroke = Ink, StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round, Fill = Brushes.Transparent };
        var hole = P("M12,8.8 A3.2,3.2 0 1 0 12,15.2 A3.2,3.2 0 1 0 12,8.8 Z");
        return Icon(body, hole);
    }
}

