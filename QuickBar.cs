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
    readonly DispatcherTimer poll = new() { Interval = TimeSpan.FromMilliseconds(40) };
    double t;              // 0 = eingefahren, 1 = ausgefahren
    bool opening;
    DateTime sinceIn = DateTime.MaxValue, sinceOut = DateTime.MaxValue;
    bool suspended;
    double scale = 1;

    public QuickBar(IList<(string Label, Func<UIElement> Icon, Action Run)?> items)
    {
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
        var root = new Grid { ClipToBounds = true };
        root.Children.Add(pill); root.Children.Add(bar);
        Content = root;

        Width = HandleW; Height = HandleH;
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            Native.SetWindowLong(h, Native.GWL_EXSTYLE, Native.GetWindowLong(h, Native.GWL_EXSTYLE) | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
        };
        Loaded += (_, _) => { scale = VisualTreeHelper.GetDpi(this).DpiScaleX; Place(); poll.Start(); };
        poll.Tick += (_, _) => Tick();
    }

    void Place()
    {
        var sb = Forms.Screen.PrimaryScreen!.Bounds;
        double sw = sb.Width / scale;
        Width = HandleW + (BarW - HandleW) * t;
        Height = HandleH + (BarH - HandleH) * t;
        Left = sb.X / scale + (sw - Width) / 2;
        Top = sb.Y / scale;
        bar.Opacity = t; pill.Opacity = 1 - t;
    }

    /// <summary>Liegt der Mauszeiger im (ggf. vergrößerten) Bereich der Leiste?</summary>
    bool CursorIn(double margin, bool handleOnly)
    {
        Native.GetCursorPos(out var p);
        var sb = Forms.Screen.PrimaryScreen!.Bounds;
        double cx = sb.X + sb.Width / 2.0;
        double w = (handleOnly ? HandleW : BarW) * scale / 2 + margin;
        double h = (handleOnly ? 3 : BarH * scale) + margin;
        return p.X >= cx - w && p.X <= cx + w && p.Y >= sb.Y - 5 && p.Y <= sb.Y + h;
    }

    static bool LeftDown() => (GetAsyncKeyState(0x01) & 0x8000) != 0;
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);

    /// <summary>Vollbild-Programm (Spiel, Video) im Vordergrund: Leiste nicht ausfahren.</summary>
    static bool ForegroundFullscreen()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero || !Native.GetWindowRect(h, out var r)) return false;
        var sb = Forms.Screen.PrimaryScreen!.Bounds;
        if (r.Left > sb.Left || r.Top > sb.Top || r.Right < sb.Right || r.Bottom < sb.Bottom) return false;
        var cls = new System.Text.StringBuilder(64);
        GetClassName(h, cls, 64);
        var c = cls.ToString();
        return c != "WorkerW" && c != "Progman" && c != "Shell_TrayWnd";
    }

    void Tick()
    {
        if (suspended) return;
        var now = DateTime.UtcNow;
        if (!opening && t <= 0)
        {
            // Ausfahren verlangt kurzes Verweilen am Rand (verhindert Auslösen beim Fensterziehen)
            if (CursorIn(0, true) && !LeftDown() && !ForegroundFullscreen())
            {
                if (sinceIn == DateTime.MaxValue) sinceIn = now;
                else if ((now - sinceIn).TotalMilliseconds > 140) { opening = true; sinceIn = DateTime.MaxValue; }
            }
            else sinceIn = DateTime.MaxValue;
        }
        else if (opening)
        {
            if (CursorIn(14, false)) sinceOut = DateTime.MaxValue;
            else if (sinceOut == DateTime.MaxValue) sinceOut = now;
            else if ((now - sinceOut).TotalMilliseconds > 450) { opening = false; sinceOut = DateTime.MaxValue; }
        }
        double target = opening ? 1 : 0;
        if (Math.Abs(t - target) > 0.001)
        {
            t += (target - t) * 0.32 + Math.Sign(target - t) * 0.01;
            t = Math.Clamp(t, 0, 1);
            if (Math.Abs(t - target) < 0.02) t = target;
            Place();
        }
    }

    /// <summary>Leiste sofort ausblenden (vor einer Aufnahme).</summary>
    public void Suspend()
    {
        suspended = true; opening = false; t = 0; sinceIn = sinceOut = DateTime.MaxValue;
        Place();
        Hide();
    }

    public void Resume()
    {
        if (!suspended) return;
        suspended = false;
        Show();
    }

    protected override void OnClosed(EventArgs e) { poll.Stop(); base.OnClosed(e); }

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
    public static UIElement IcGear() => new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 22, Foreground = Ink };
}
