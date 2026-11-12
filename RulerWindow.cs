using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Knipser;

/// <summary>Bildschirm-Lineal in Pixeln: ziehen = verschieben, rechtes Ende ziehen = Länge ändern, Doppelklick = drehen, Esc = schließen.</summary>
internal class RulerWindow : Window
{
    sealed class Face : FrameworkElement
    {
        public bool Vertical;
        static readonly Typeface Tf = new("Segoe UI");

        protected override void OnRender(DrawingContext dc)
        {
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;          // Pixel je DIP
            double w = ActualWidth, h = ActualHeight;
            var bg = new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0xD5, 0x4F));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x5D, 0x40, 0x37)), 1);
            dc.DrawRoundedRectangle(bg, pen, new Rect(0.5, 0.5, w - 1, h - 1), 4, 4);

            double lenPx = (Vertical ? h : w) * scale;
            var dark = new SolidColorBrush(Color.FromRgb(0x3E, 0x27, 0x23));
            var tick = new Pen(dark, 1);
            for (int px = 0; px <= lenPx; px += 10)
            {
                double pos = px / scale;
                double l = px % 100 == 0 ? 22 : px % 50 == 0 ? 15 : 8;
                if (Vertical) dc.DrawLine(tick, new Point(0, pos + 0.5), new Point(l, pos + 0.5));
                else dc.DrawLine(tick, new Point(pos + 0.5, 0), new Point(pos + 0.5, l));
                if (px % 100 == 0 && px > 0)
                {
                    var t = new FormattedText(px.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Tf, 11, dark, scale);
                    if (Vertical) dc.DrawText(t, new Point(26, pos - 7));
                    else dc.DrawText(t, new Point(pos - t.Width / 2, 24));
                }
            }
            var len = new FormattedText($"{Math.Round(lenPx)} px", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Tf, 13, dark, scale)
            { };
            len.SetFontWeight(FontWeights.SemiBold);
            if (Vertical) dc.DrawText(len, new Point(w - len.Width - 6, h / 2 - len.Height / 2));
            else dc.DrawText(len, new Point(w / 2 - len.Width / 2, h - len.Height - 6));
            // Griff am Ende
            var grip = new SolidColorBrush(Color.FromArgb(0x70, 0x3E, 0x27, 0x23));
            if (Vertical) dc.DrawRectangle(grip, null, new Rect(2, h - 8, w - 4, 6));
            else dc.DrawRectangle(grip, null, new Rect(w - 8, 2, 6, h - 4));
        }
    }

    readonly Face face = new();
    bool resizing;
    const double GripZone = 14;

    public RulerWindow()
    {
        Title = "Knipser Lineal";
        Icon = App.WindowIcon;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = face;
        double s = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Width = 700 / s; Height = 64 / s;

        var menu = new ContextMenu();
        var rot = new MenuItem { Header = "Drehen (Doppelklick)" }; rot.Click += (_, _) => Rotate();
        var close = new MenuItem { Header = "Schließen (Esc)" }; close.Click += (_, _) => Close();
        menu.Items.Add(rot); menu.Items.Add(close);
        ContextMenu = menu;

        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += (_, _) => { if (resizing) { resizing = false; ReleaseMouseCapture(); } };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); else if (e.Key == Key.R) Rotate(); };
        Loaded += (_, _) => { Activate(); Focus(); };
    }

    void Rotate()
    {
        (Width, Height) = (Height, Width);
        face.Vertical = !face.Vertical;
        face.InvalidateVisual();
    }

    bool InGrip(Point p) => face.Vertical ? p.Y > ActualHeight - GripZone : p.X > ActualWidth - GripZone;

    void OnDown(object s, MouseButtonEventArgs e)
    {
        Focus();
        if (e.ClickCount == 2) { Rotate(); return; }
        if (InGrip(e.GetPosition(this))) { resizing = true; CaptureMouse(); return; }
        try { DragMove(); } catch { }
    }

    void OnMove(object s, MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (resizing)
        {
            if (face.Vertical) Height = Math.Max(60, p.Y);
            else Width = Math.Max(60, p.X);
            face.InvalidateVisual();
            return;
        }
        Cursor = InGrip(p) ? (face.Vertical ? Cursors.SizeNS : Cursors.SizeWE) : Cursors.SizeAll;
    }
}
