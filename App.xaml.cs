using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace Knipser;

public partial class App : Application
{
    Mutex? mutex;
    Forms.NotifyIcon? tray;
    HwndSource? hotkeyWindow;
    readonly Dictionary<int, Action> hotkeys = new();
    bool busy;
    public Settings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        mutex = new Mutex(true, "Knipser.SingleInstance", out bool created);
        if (!created) { Shutdown(); return; }

        Settings = Settings.Load();
        BuildTray();

        var p = new HwndSourceParameters("KnipserHotkeys") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 };
        hotkeyWindow = new HwndSource(p);
        hotkeyWindow.AddHook(WndProc);
        if (Settings.CheckUpdates) SilentUpdateCheck();
        var failed = RegisterAll();
        if (failed.Count > 0)
            tray!.ShowBalloonTip(5000, "Knipser",
                "Tastenkürzel belegt (läuft evtl. noch ein anderes Screenshot-Programm?): " + string.Join(", ", failed),
                Forms.ToolTipIcon.Warning);
    }

    /// <summary>Registriert alle Tastenkürzel neu (auch nach Änderung in den Einstellungen). Liefert die nicht registrierbaren.</summary>
    public List<string> RegisterAll()
    {
        foreach (var id in hotkeys.Keys) Native.UnregisterHotKey(hotkeyWindow!.Handle, id);
        hotkeys.Clear();
        var failed = new List<string>();
        Register(1, Settings.HotkeyRegion, CaptureRegion, failed);
        Register(2, Settings.HotkeyFullscreen, CaptureFullscreen, failed);
        Register(3, Settings.HotkeyWindow, CaptureWindow, failed);
        Register(4, Settings.HotkeyScroll, CaptureScroll, failed);
        Register(5, Settings.HotkeyPicker, () => PixelTool(PixelMode.Picker), failed);
        Register(6, Settings.HotkeyMagnifier, () => PixelTool(PixelMode.Magnifier), failed);
        Register(7, Settings.HotkeyRuler, ToggleRuler, failed);
        return failed;
    }

    // ---------- Updates ----------
    Forms.ToolStripMenuItem? updateItem;
    public Updater.Info? PendingUpdate { get; private set; }

    /// <summary>Sucht nach einer neuen Version. Wirft bei Netzwerkfehlern.</summary>
    public async System.Threading.Tasks.Task<Updater.Info?> CheckForUpdate()
    {
        var info = await Updater.CheckAsync();
        PendingUpdate = info;
        if (updateItem != null)
        {
            updateItem.Visible = info != null;
            if (info != null) updateItem.Text = $"Update auf {info.Tag} installieren";
        }
        return info;
    }

    /// <summary>Lädt die neue Version und startet Knipser neu. Wirft bei Fehlern.</summary>
    public async System.Threading.Tasks.Task InstallUpdate(Updater.Info info)
    {
        await Updater.PrepareInstallAsync(info);
        Quit();
    }

    async void InstallFromTray()
    {
        if (PendingUpdate == null) return;
        try { await InstallUpdate(PendingUpdate); }
        catch (Exception ex) { tray?.ShowBalloonTip(4000, "Knipser", "Update fehlgeschlagen: " + ex.Message, Forms.ToolTipIcon.Error); }
    }

    async void SilentUpdateCheck()
    {
        await System.Threading.Tasks.Task.Delay(8000);
        try { await CheckForUpdate(); } catch { }   // offline o. Ä.: still ignorieren
    }

    SettingsWindow? settingsWin;

    public void ShowSettings()
    {
        if (settingsWin != null) { settingsWin.Activate(); return; }
        settingsWin = new SettingsWindow(Settings, this);
        settingsWin.Closed += (_, _) => settingsWin = null;
        settingsWin.Show();
        settingsWin.Activate();
    }

    void Register(int id, string text, Action action, List<string> failed)
    {
        if (string.IsNullOrWhiteSpace(text)) return;   // leer = nicht belegt
        if (!TryParse(text, out uint mod, out uint vk) ||
            !Native.RegisterHotKey(hotkeyWindow!.Handle, id, mod | Native.MOD_NOREPEAT, vk))
        { failed.Add(text); return; }
        hotkeys[id] = action;
    }

    static bool TryParse(string text, out uint mod, out uint vk)
    {
        mod = 0; vk = 0;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        foreach (var m in parts[..^1])
        {
            switch (m.ToLowerInvariant())
            {
                case "ctrl": case "strg": mod |= Native.MOD_CONTROL; break;
                case "alt": mod |= Native.MOD_ALT; break;
                case "shift": mod |= Native.MOD_SHIFT; break;
                default: return false;
            }
        }
        if (!Enum.TryParse<Forms.Keys>(parts[^1], true, out var key)) return false;
        vk = (uint)key;
        return true;
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && hotkeys.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            Dispatcher.BeginInvoke(action);
        }
        return IntPtr.Zero;
    }

    void BuildTray()
    {
        var menu = new Forms.ContextMenuStrip();
        updateItem = new Forms.ToolStripMenuItem("Update installieren") { Visible = false, Font = new Font(menu.Font, System.Drawing.FontStyle.Bold) };
        updateItem.Click += (_, _) => InstallFromTray();
        menu.Items.Add(updateItem);
        menu.Items.Add("Bereich aufnehmen", null, (_, _) => CaptureRegion());
        menu.Items.Add("Vollbild aufnehmen", null, (_, _) => CaptureFullscreen());
        menu.Items.Add("Fenster unter Mauszeiger", null, (_, _) => CaptureWindow());
        menu.Items.Add("Scrollende Aufnahme", null, (_, _) => CaptureScroll());
        menu.Items.Add("Farbpipette", null, (_, _) => PixelTool(PixelMode.Picker));
        menu.Items.Add("Lupe", null, (_, _) => PixelTool(PixelMode.Magnifier));
        menu.Items.Add("Lineal", null, (_, _) => ToggleRuler());
        menu.Items.Add("Bild aus Zwischenablage öffnen", null, (_, _) => OpenClipboard());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Einstellungen…", null, (_, _) => ShowSettings());
        menu.Items.Add("Speicherordner öffnen", null, (_, _) =>
        {
            Directory.CreateDirectory(Settings.SaveFolder);
            Process.Start("explorer.exe", Settings.SaveFolder);
        });
        menu.Items.Add("Einstellungsdatei öffnen", null, (_, _) =>
        {
            var f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Knipser", "settings.json");
            Process.Start(new ProcessStartInfo(f) { UseShellExecute = true });
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => Quit());

        tray = new Forms.NotifyIcon { Icon = MakeIcon(), Text = "Knipser", Visible = true, ContextMenuStrip = menu };
        tray.MouseDoubleClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) CaptureRegion(); };
    }

    /// <summary>Fenster-Symbol (Editor, Einstellungen).</summary>
    public static System.Windows.Media.ImageSource WindowIcon { get; } =
        BitmapFrame.Create(new Uri("pack://application:,,,/Knipser.ico"));

    static Icon MakeIcon()
    {
        try
        {
            var sr = GetResourceStream(new Uri("pack://application:,,,/Knipser.ico"));
            if (sr != null) return new Icon(sr.Stream, Forms.SystemInformation.SmallIconSize);
        }
        catch { }
        return MakeFallbackIcon();
    }

    static Icon MakeFallbackIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var bg = new SolidBrush(Color.FromArgb(30, 136, 229));
            g.FillEllipse(bg, 0, 0, 31, 31);
            using var pen = new Pen(Color.White, 2.5f);
            g.DrawLine(pen, 8, 14, 8, 8); g.DrawLine(pen, 8, 8, 14, 8);
            g.DrawLine(pen, 24, 14, 24, 8); g.DrawLine(pen, 24, 8, 18, 8);
            g.DrawLine(pen, 8, 18, 8, 24); g.DrawLine(pen, 8, 24, 14, 24);
            g.DrawLine(pen, 24, 18, 24, 24); g.DrawLine(pen, 24, 24, 18, 24);
        }
        var h = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(h).Clone();
        Native.DestroyIcon(h);
        return icon;
    }

    void Quit()
    {
        tray!.Visible = false;
        tray.Dispose();
        if (hotkeyWindow != null)
            foreach (var id in hotkeys.Keys) Native.UnregisterHotKey(hotkeyWindow.Handle, id);
        hotkeyWindow?.Dispose();
        Shutdown();
    }

    void CaptureRegion()
    {
        if (busy) return;
        busy = true;
        try
        {
            var vs = Capture.VirtualScreen;
            var shot = Capture.Grab(vs);
            var ov = new SelectionOverlay(shot, vs);
            ov.ShowDialog();
            if (ov.Result is Int32Rect r)
            {
                var crop = new CroppedBitmap(shot, r);
                crop.Freeze();
                Finish(crop);
            }
        }
        finally { busy = false; }
    }

    void PixelTool(PixelMode mode)
    {
        if (busy) return;
        busy = true;
        try
        {
            var vs = Capture.VirtualScreen;
            var ov = new PixelOverlay(Capture.Grab(vs), vs, mode);
            ov.ShowDialog();
            if (ov.Result is string hex)
            {
                try { Clipboard.SetText(hex); } catch { }
                tray?.ShowBalloonTip(1500, "Knipser", $"Farbe {hex} in die Zwischenablage kopiert.", Forms.ToolTipIcon.Info);
            }
        }
        finally { busy = false; }
    }

    RulerWindow? ruler;

    void ToggleRuler()
    {
        if (ruler != null) { ruler.Close(); return; }
        ruler = new RulerWindow();
        ruler.Closed += (_, _) => ruler = null;
        ruler.Show();
        ruler.Activate();
    }

    async void CaptureScroll()
    {
        if (busy) return;
        busy = true;
        try
        {
            var vs = Capture.VirtualScreen;
            var shot = Capture.Grab(vs);
            var ov = new SelectionOverlay(shot, vs);
            ov.ShowDialog();
            if (ov.Result is not Int32Rect r) return;
            await System.Threading.Tasks.Task.Delay(250);   // Overlay ist weg
            var rect = new Rectangle(vs.X + r.X, vs.Y + r.Y, r.Width, r.Height);
            var result = await ScrollCapture.Run(rect);
            if (result != null) Finish(result);
        }
        catch (Exception ex)
        {
            tray?.ShowBalloonTip(4000, "Knipser", "Scroll-Aufnahme fehlgeschlagen: " + ex.Message, Forms.ToolTipIcon.Error);
        }
        finally { busy = false; }
    }

    void CaptureFullscreen()
    {
        if (busy) return;
        Finish(Capture.Grab(Capture.VirtualScreen));
    }

    void CaptureWindow()
    {
        if (busy) return;
        var r = Capture.WindowUnderCursor();
        if (r is Rectangle rect) Finish(Capture.Grab(rect));
    }

    void OpenClipboard()
    {
        if (Clipboard.ContainsImage() && Clipboard.GetImage() is BitmapSource bmp)
            OpenEditor(bmp);
    }

    void Finish(BitmapSource bmp)
    {
        if (Settings.CopyToClipboard)
        {
            try { Clipboard.SetImage(bmp); } catch { }
        }
        if (Settings.OpenEditor) OpenEditor(bmp);
        else tray?.ShowBalloonTip(1500, "Knipser", "In die Zwischenablage kopiert.", Forms.ToolTipIcon.Info);
    }

    void OpenEditor(BitmapSource bmp)
    {
        var w = new EditorWindow(bmp, Settings);
        w.Show();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Topmost = true;   // Trick, damit das Fenster sicher nach vorn kommt
        w.Activate();
        w.Topmost = false;
        w.Focus();
    }
}
