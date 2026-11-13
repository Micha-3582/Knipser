using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Knipser;

/// <summary>Einstellungen: Tastenkürzel einzeln vergeben, Speicherordner, Verhalten nach der Aufnahme.</summary>
internal class SettingsWindow : Window
{
    readonly Settings s;
    readonly App app;
    readonly TextBox region = Hk(), full = Hk(), win = Hk(), scroll = Hk(), picker = Hk(), magnifier = Hk(), rulerKey = Hk(), fixedKey = Hk(), repeatKey = Hk(), freeKey = Hk();
    readonly TextBox folder = new() { Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), Padding = new Thickness(6, 4, 6, 4), VerticalContentAlignment = VerticalAlignment.Center };
    readonly RadioButton jpg = new() { Content = "JPG", GroupName = "fmt" }, png = new() { Content = "PNG", GroupName = "fmt" }, webp = new() { Content = "WebP", GroupName = "fmt" };
    readonly Slider webpQuality = new() { Minimum = 10, Maximum = 100, Width = 130, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock webpQualityText = new() { Width = 34, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
    readonly Slider quality = new() { Minimum = 10, Maximum = 100, Width = 130, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock qualityText = new() { Width = 34, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
    readonly CheckBox openEditor = new() { Content = "Nach der Aufnahme den Editor öffnen" };
    readonly CheckBox saveOnClose = new() { Content = "Beim Schließen des Editors automatisch speichern" };
    readonly CheckBox copy = new() { Content = "Aufnahme automatisch in die Zwischenablage kopieren" };
    readonly CheckBox cursorCb = new() { Content = "Mauszeiger im Bild mit aufnehmen" };
    readonly Slider delay = new() { Minimum = 0, Maximum = 10, Width = 130, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock delayText = new() { Width = 60, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
    readonly CheckBox autostart = new() { Content = "Mit Windows starten" };
    readonly TextBox fixedW = new() { Width = 60, Padding = new Thickness(4, 3, 4, 3) }, fixedH = new() { Width = 60, Padding = new Thickness(4, 3, 4, 3) };
    readonly TextBox pattern = new() { Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(6, 4, 6, 4), Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)) };
    readonly ComboBox colorFmt = new() { Width = 170 };
    readonly CheckBox hideEditorCb = new() { Content = "Editor-Fenster vor der Aufnahme ausblenden" };
    readonly CheckBox dimCb = new() { Content = "Bildschirm außerhalb der Auswahl abdunkeln" };
    readonly Slider scrollDelay = new() { Minimum = 100, Maximum = 1500, Width = 150, IsSnapToTickEnabled = true, TickFrequency = 50, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock scrollDelayText = new() { Width = 70, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
    readonly TextBox extProg = new() { Padding = new Thickness(6, 4, 6, 4), Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)) };
    readonly TextBox extArgs = new() { Padding = new Thickness(6, 4, 6, 4), Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)) };
    readonly TextBlock namePreview = new() { FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5)), Margin = new Thickness(0, 4, 0, 10) };
    readonly TextBlock msg = new() { Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x65)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    readonly Button updBtn = new() { Content = "Nach Updates suchen", Padding = new Thickness(10, 4, 10, 4) };
    readonly TextBlock updText = new() { Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)), TextWrapping = TextWrapping.Wrap };
    readonly CheckBox checkUpd = new() { Content = "Beim Start still nach Updates suchen" };
    Updater.Info? found;

    void ShowPending(Updater.Info info)
    {
        found = info;
        updBtn.Content = $"Auf {info.Tag} aktualisieren";
        updText.Text = $"Neue Version {info.Tag} verfügbar." + (string.IsNullOrWhiteSpace(info.Notes) ? "" : "\n" + info.Notes.Trim());
    }

    async System.Threading.Tasks.Task UpdateClick()
    {
        updBtn.IsEnabled = false;
        try
        {
            if (found == null)
            {
                updText.Text = "Suche …";
                var info = await app.CheckForUpdate();
                if (info == null) updText.Text = "Du hast die neueste Version.";
                else ShowPending(info);
            }
            else
            {
                updText.Text = "Lade Update …";
                await app.InstallUpdate(found);
            }
        }
        catch (Exception ex) { updText.Text = "Fehlgeschlagen: " + ex.Message; }
        finally { updBtn.IsEnabled = true; }
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    static TextBox Hk() => new()
    {
        IsReadOnly = true, Padding = new Thickness(6, 4, 6, 4), Width = 230, HorizontalAlignment = HorizontalAlignment.Left,
        Cursor = Cursors.Hand, IsReadOnlyCaretVisible = false,
        Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)), Foreground = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
        ToolTip = "Klicken und die gewünschte Tastenkombination drücken. Entf/Rücktaste = nicht belegt.",
    };

    public SettingsWindow(Settings settings, App app)
    {
        s = settings; this.app = app;
        Title = "Knipser – Einstellungen";
        Icon = App.WindowIcon;
        Width = 560; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1B));
        Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));

        Width = 820; Height = 660; MinHeight = 480; MinWidth = 700;
        SizeToContent = SizeToContent.Manual;
        ResizeMode = ResizeMode.CanResize;
        MaxHeight = SystemParameters.WorkArea.Height - 10;

        // ---------- Seiten ----------
        var pages = new Dictionary<string, FrameworkElement>();
        StackPanel NewPage(string name)
        {
            var sp = new StackPanel { Margin = new Thickness(22, 4, 22, 14) };
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp, Visibility = Visibility.Collapsed };
            pages[name] = sv;
            return sp;
        }
        StackPanel Line(string label, UIElement ctl, double labelWidth = 230)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 6) };
            p.Children.Add(new TextBlock { Text = label, Width = labelWidth, VerticalAlignment = VerticalAlignment.Center });
            p.Children.Add(ctl);
            return p;
        }
        void Checks(StackPanel page, params CheckBox[] boxes)
        {
            foreach (var c in boxes) { c.Foreground = Foreground; c.Margin = new Thickness(0, 4, 0, 4); page.Children.Add(c); }
        }

        // Allgemein
        var pAllg = NewPage("Allgemein");
        pAllg.Children.Add(Head("Start"));
        Checks(pAllg, autostart, checkUpd);
        pAllg.Children.Add(Head("Farbpipette"));
        colorFmt.Items.Add("Hex (#RRGGBB)"); colorFmt.Items.Add("RGB (rgb(r, g, b))"); colorFmt.Items.Add("C++ (0xRRGGBB)");
        colorFmt.SelectedIndex = s.ColorFormat switch { "rgb" => 1, "cpp" => 2, _ => 0 };
        pAllg.Children.Add(Line("Farbwert kopieren als", colorFmt));

        // Editor
        var pEd = NewPage("Editor");
        pEd.Children.Add(Head("Editor-Optionen"));
        Checks(pEd, openEditor, saveOnClose, hideEditorCb);

        // Aufnahme
        var pAuf = NewPage("Aufnahme");
        pAuf.Children.Add(Head("Aufnahmeoptionen"));
        Checks(pAuf, cursorCb, copy, dimCb);
        var dr = new StackPanel { Orientation = Orientation.Horizontal };
        dr.Children.Add(delay); dr.Children.Add(delayText);
        delay.Value = Math.Clamp(s.CaptureDelay, 0, 10);
        void ShowDelay() => delayText.Text = delay.Value < 1 ? "sofort" : $"{(int)delay.Value} s";
        ShowDelay();
        delay.ValueChanged += (_, _) => ShowDelay();
        pAuf.Children.Add(Line("Zeitverzögerung", dr));
        var fr2 = new StackPanel { Orientation = Orientation.Horizontal };
        fr2.Children.Add(fixedW);
        fr2.Children.Add(new TextBlock { Text = " × ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
        fr2.Children.Add(fixedH);
        pAuf.Children.Add(Line("Fester Bereich (Pixel)", fr2));
        pAuf.Children.Add(Head("Automatischer Bildlauf"));
        var sd = new StackPanel { Orientation = Orientation.Horizontal };
        sd.Children.Add(scrollDelay); sd.Children.Add(scrollDelayText);
        scrollDelay.Value = Math.Clamp(s.ScrollDelayMs, 100, 1500);
        void ShowScrollDelay() => scrollDelayText.Text = $"{(int)scrollDelay.Value} ms";
        ShowScrollDelay();
        scrollDelay.ValueChanged += (_, _) => ShowScrollDelay();
        pAuf.Children.Add(Line("Wartezeit je Schritt", sd));
        pAuf.Children.Add(new TextBlock { Text = "Bei Seiten mit Animationen oder langsamem Nachladen eine längere Wartezeit wählen.", Foreground = Brushes.Gray, FontSize = 12, TextWrapping = TextWrapping.Wrap });

        // Dateiname
        var pName = NewPage("Dateiname");
        pName.Children.Add(Head("Dateiname"));
        pName.Children.Add(new TextBlock { Text = "Namensmuster für automatisch gespeicherte Bilder:", Margin = new Thickness(0, 0, 0, 4) });
        pName.Children.Add(pattern);
        pattern.Margin = new Thickness(0, 0, 0, 2);
        pName.Children.Add(namePreview);
        var help = new TextBlock { Foreground = Brushes.Gray, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
            Text = "Platzhalter in geschweiften Klammern:\n" +
                   "{yyyy} Jahr, {MM} Monat, {dd} Tag, {HH} Stunde, {mm} Minute, {ss} Sekunde (auch als Gruppe, z. B. {yyyy-MM-dd_HH-mm-ss})\n" +
                   "{n} laufende Nummer, {user} Benutzername, {pc} Computername" };
        pName.Children.Add(help);
        pName.Children.Add(Head("Dateiformat"));
        var fmt = new StackPanel { Orientation = Orientation.Horizontal };
        fmt.Children.Add(jpg); fmt.Children.Add(png); fmt.Children.Add(webp);
        foreach (var rb in new[] { jpg, png, webp }) { rb.Foreground = Foreground; rb.Margin = new Thickness(0, 0, 18, 0); }
        var fm = s.ImageFormat.ToLowerInvariant();
        png.IsChecked = fm == "png"; webp.IsChecked = fm == "webp"; jpg.IsChecked = !(png.IsChecked == true || webp.IsChecked == true);
        pName.Children.Add(Line("Bildformat", fmt));
        var qr = new StackPanel { Orientation = Orientation.Horizontal };
        qr.Children.Add(quality); qr.Children.Add(qualityText);
        qr.Children.Add(new TextBlock { Text = "← kleiner", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        quality.Value = Math.Clamp(s.JpgQuality, 1, 100);
        qualityText.Text = ((int)quality.Value).ToString();
        quality.ValueChanged += (_, e) => qualityText.Text = ((int)e.NewValue).ToString();
        pName.Children.Add(Line("JPG-Qualität", qr));
        var wq = new StackPanel { Orientation = Orientation.Horizontal };
        wq.Children.Add(webpQuality); wq.Children.Add(webpQualityText);
        wq.Children.Add(new TextBlock { Text = "← kleiner", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        webpQuality.Value = Math.Clamp(s.WebpQuality, 10, 100);
        webpQualityText.Text = ((int)webpQuality.Value).ToString();
        webpQuality.ValueChanged += (_, e) => webpQualityText.Text = ((int)e.NewValue).ToString();
        pName.Children.Add(Line("WebP-Qualität", wq));
        void ShowPreviewName() => namePreview.Text = Settings.Expand(pattern.Text, s.FileCounter + 1) + (png.IsChecked == true ? ".png" : webp.IsChecked == true ? ".webp" : ".jpg");
        pattern.TextChanged += (_, _) => ShowPreviewName();
        jpg.Checked += (_, _) => ShowPreviewName(); png.Checked += (_, _) => ShowPreviewName(); webp.Checked += (_, _) => ShowPreviewName();

        // Speichern
        var pSave = NewPage("Speichern");
        pSave.Children.Add(Head("Speicherort"));
        var fr = new DockPanel { Margin = new Thickness(0, 2, 0, 6) };
        var browse = new Button { Content = "Durchsuchen…", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(browse, Dock.Right);
        fr.Children.Add(browse); fr.Children.Add(folder);
        pSave.Children.Add(fr);
        browse.Click += (_, _) =>
        {
            using var d = new Forms.FolderBrowserDialog { SelectedPath = folder.Text, Description = "Speicherordner für Screenshots" };
            if (d.ShowDialog() == Forms.DialogResult.OK) folder.Text = d.SelectedPath;
        };
        pSave.Children.Add(Head("Externes Programm"));
        pSave.Children.Add(new TextBlock { Text = "Programm, in dem Aufnahmen über „Datei → Öffnen mit“ geöffnet werden:", Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap });
        var pr = new DockPanel { Margin = new Thickness(0, 2, 0, 6) };
        var browseProg = new Button { Content = "Durchsuchen…", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(browseProg, Dock.Right);
        pr.Children.Add(browseProg); pr.Children.Add(extProg);
        pSave.Children.Add(pr);
        browseProg.Click += (_, _) =>
        {
            var d = new OpenFileDialog { Filter = "Programme (*.exe)|*.exe|Alle Dateien|*.*" };
            if (d.ShowDialog(this) == true) extProg.Text = d.FileName;
        };
        pSave.Children.Add(new TextBlock { Text = "Parameter (%f = Pfad der Bilddatei, leer = nur der Pfad):", Margin = new Thickness(0, 4, 0, 4) });
        pSave.Children.Add(extArgs);

        // Tastenkürzel
        var pKeys = NewPage("Tastenkürzel");
        pKeys.Children.Add(Head("Tastenkürzel"));
        pKeys.Children.Add(Row("Bereich aufnehmen", region));
        pKeys.Children.Add(Row("Vollbild aufnehmen", full));
        pKeys.Children.Add(Row("Fenster unter dem Mauszeiger", win));
        pKeys.Children.Add(Row("Scrollende Aufnahme", scroll));
        pKeys.Children.Add(Row("Fester Bereich", fixedKey));
        pKeys.Children.Add(Row("Freihand-Auswahl", freeKey));
        pKeys.Children.Add(Row("Letzten Bereich wiederholen", repeatKey));
        pKeys.Children.Add(Row("Farbpipette", picker));
        pKeys.Children.Add(Row("Lupe", magnifier));
        pKeys.Children.Add(Row("Messen (Lineal)", rulerKey));

        // Info
        var pInfo = NewPage("Über Knipser");
        pInfo.Children.Add(Head("Knipser"));
        var ur = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 4) };
        ur.Children.Add(new TextBlock { Text = "Installierte Version: " + Updater.CurrentText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) });
        ur.Children.Add(updBtn);
        pInfo.Children.Add(ur);
        pInfo.Children.Add(updText);
        updBtn.Click += async (_, _) => await UpdateClick();
        if (app.PendingUpdate != null) ShowPending(app.PendingUpdate);
        var about = new TextBlock { Foreground = Brushes.Gray, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) };
        about.Inlines.Add(new System.Windows.Documents.Run("Knipser ist ein freies Screenshot-Programm für Windows.\nEntwickelt von Michael Arthen.\nQuelltext und Downloads: "));
        var gh = new System.Windows.Documents.Run("github.com/Micha-3582/Knipser") { Foreground = new SolidColorBrush(Color.FromRgb(0x4F, 0xA3, 0xF0)), Cursor = System.Windows.Input.Cursors.Hand, TextDecorations = null };
        gh.MouseEnter += (_, _) => gh.Foreground = Brushes.White;
        gh.MouseLeave += (_, _) => gh.Foreground = new SolidColorBrush(Color.FromRgb(0x4F, 0xA3, 0xF0));
        gh.MouseLeftButtonUp += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/Micha-3582/Knipser") { UseShellExecute = true }); } catch { } };
        about.Inlines.Add(gh);
        pInfo.Children.Add(about);

        // ---------- Seitenleiste ----------
        var side = new StackPanel();
        var sideBorder = new Border { Width = 170, Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)), Child = side };
        var tpl = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='RadioButton'>" +
            "<Border x:Name='b' Background='Transparent' Padding='16,9' BorderThickness='3,0,0,0' BorderBrush='Transparent'><ContentPresenter VerticalAlignment='Center'/></Border>" +
            "<ControlTemplate.Triggers>" +
            "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#2D2D2D'/></Trigger>" +
            "<Trigger Property='IsChecked' Value='True'><Setter TargetName='b' Property='Background' Value='#333333'/><Setter TargetName='b' Property='BorderBrush' Value='#42A5F5'/></Trigger>" +
            "</ControlTemplate.Triggers></ControlTemplate>");
        var area = new Grid();
        foreach (var kv in pages) area.Children.Add(kv.Value);
        bool first = true;
        foreach (var kv in pages)
        {
            var name = kv.Key;
            var rb = new RadioButton { Content = name, GroupName = "nav", Template = tpl, Foreground = Foreground, Cursor = Cursors.Hand, IsChecked = first };
            rb.Checked += (_, _) => { foreach (var q in pages) q.Value.Visibility = q.Key == name ? Visibility.Visible : Visibility.Collapsed; };
            side.Children.Add(rb);
            first = false;
        }
        pages[pages.Keys.First()].Visibility = Visibility.Visible;

        // ---------- Unterkante ----------
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "Speichern", Padding = new Thickness(20, 6, 20, 6), IsDefault = true };
        var cancel = new Button { Content = "Abbrechen", Padding = new Thickness(20, 6, 20, 6), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        var bottom = new DockPanel { Margin = new Thickness(14, 8, 14, 12) };
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        msg.Margin = new Thickness(0, 0, 12, 0); msg.VerticalAlignment = VerticalAlignment.Center;
        bottom.Children.Add(msg);

        var dock = new DockPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        DockPanel.SetDock(sideBorder, Dock.Left);
        dock.Children.Add(bottom);
        dock.Children.Add(sideBorder);
        dock.Children.Add(area);
        Content = dock;

        region.Text = s.HotkeyRegion; full.Text = s.HotkeyFullscreen; win.Text = s.HotkeyWindow; scroll.Text = s.HotkeyScroll;
        picker.Text = s.HotkeyPicker; magnifier.Text = s.HotkeyMagnifier; rulerKey.Text = s.HotkeyRuler;
        fixedKey.Text = s.HotkeyFixed; repeatKey.Text = s.HotkeyRepeat; freeKey.Text = s.HotkeyFreehand;
        fixedW.Text = s.FixedWidth.ToString(); fixedH.Text = s.FixedHeight.ToString(); pattern.Text = s.FileNamePattern;
        folder.Text = s.SaveFolder;
        openEditor.IsChecked = s.OpenEditor; saveOnClose.IsChecked = s.SaveOnClose; copy.IsChecked = s.CopyToClipboard;
        autostart.IsChecked = GetAutostart();
        hideEditorCb.IsChecked = s.HideEditorOnCapture; dimCb.IsChecked = s.DimOutside;
        extProg.Text = s.ExternalProgram; extArgs.Text = s.ExternalArgs;
        ShowPreviewName();
        cursorCb.IsChecked = s.IncludeCursor;
        checkUpd.IsChecked = s.CheckUpdates;

        foreach (var t in new[] { region, full, win, scroll, picker, magnifier, rulerKey, fixedKey, repeatKey, freeKey })
        {
            t.PreviewKeyDown += HotkeyKey;
            t.GotKeyboardFocus += HotkeyFocus;
            t.LostKeyboardFocus += HotkeyBlur;
        }
        ok.Click += (_, _) => Apply();
    }

    static TextBlock Head(string t) => new() { Text = t, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6), Foreground = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5)) };

    static UIElement Row(string label, TextBox box)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(box, 1);
        g.Children.Add(box);
        return g;
    }

    const string Prompt = "▶ Jetzt Tastenkombination drücken …";
    readonly Dictionary<TextBox, string> prevText = new();

    void HotkeyFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var b = (TextBox)sender;
        if (b.Text != Prompt) prevText[b] = b.Text;
        b.Text = Prompt;
        b.BorderBrush = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5));
        b.BorderThickness = new Thickness(2);
        b.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F));
        b.FontStyle = FontStyles.Italic;
    }

    void ResetHotkeyStyle(TextBox b)
    {
        b.BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        b.BorderThickness = new Thickness(1);
        b.Foreground = Brushes.White;
        b.FontStyle = FontStyles.Normal;
    }

    void HotkeyBlur(object sender, KeyboardFocusChangedEventArgs e)
    {
        var b = (TextBox)sender;
        if (b.Text == Prompt) b.Text = prevText.TryGetValue(b, out var old) ? old : "";   // nichts gedrückt: alten Wert behalten
        ResetHotkeyStyle(b);
    }

    void HotkeyKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var box = (TextBox)sender;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin or Key.None or Key.ImeProcessed or Key.DeadCharProcessed) return;
        if (key == Key.Escape) { box.Text = prevText.TryGetValue(box, out var o) ? o : ""; ResetHotkeyStyle(box); Focus(); return; }   // Abbrechen
        if (key is Key.Delete or Key.Back) { box.Text = ""; prevText[box] = ""; ResetHotkeyStyle(box); Focus(); return; }
        if (key == Key.Tab) { box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); return; }

        var vk = KeyInterop.VirtualKeyFromKey(key);
        var parts = new List<string>();
        var m = Keyboard.Modifiers;
        if (m.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (m.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (m.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        parts.Add(((Forms.Keys)vk).ToString());
        box.Text = string.Join("+", parts);
        prevText[box] = box.Text;
        ResetHotkeyStyle(box);
        Focus();   // Fokus weg vom Feld, damit der nächste Klick wieder die Aufforderung zeigt
    }

    static bool GetAutostart()
    {
        try { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue("Knipser") != null; }
        catch { return false; }
    }

    static void SetAutostart(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (k == null) return;
            if (on) k.SetValue("Knipser", "\"" + Environment.ProcessPath + "\"");
            else k.DeleteValue("Knipser", false);
        }
        catch { }
    }

    void Apply()
    {
        var list = new[] { region.Text, full.Text, win.Text, scroll.Text, picker.Text, magnifier.Text, rulerKey.Text, fixedKey.Text, repeatKey.Text, freeKey.Text };
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in list)
            if (!string.IsNullOrEmpty(h) && !used.Add(h)) { msg.Text = $"„{h}“ ist doppelt vergeben."; return; }

        // Immer speichern – auch wenn ein Kürzel gerade von einem anderen Programm belegt ist
        // (z. B. PicPick). Es wird dann nur gemeldet und ist aktiv, sobald es frei ist.
        s.HotkeyRegion = region.Text; s.HotkeyFullscreen = full.Text; s.HotkeyWindow = win.Text; s.HotkeyScroll = scroll.Text;
        s.HotkeyPicker = picker.Text; s.HotkeyMagnifier = magnifier.Text; s.HotkeyRuler = rulerKey.Text;
        s.HotkeyFixed = fixedKey.Text; s.HotkeyRepeat = repeatKey.Text; s.HotkeyFreehand = freeKey.Text;
        if (int.TryParse(fixedW.Text, out int fw) && fw >= 10) s.FixedWidth = fw;
        if (int.TryParse(fixedH.Text, out int fh) && fh >= 10) s.FixedHeight = fh;
        s.FileNamePattern = string.IsNullOrWhiteSpace(pattern.Text) ? "Knipser_{yyyy-MM-dd_HH-mm-ss}" : pattern.Text.Trim();
        s.ColorFormat = colorFmt.SelectedIndex switch { 1 => "rgb", 2 => "cpp", _ => "hex" };
        var failed = app.RegisterAll();
        s.SaveFolder = string.IsNullOrWhiteSpace(folder.Text) ? s.SaveFolder : folder.Text.Trim();
        s.ImageFormat = png.IsChecked == true ? "png" : webp.IsChecked == true ? "webp" : "jpg";
        s.JpgQuality = (int)quality.Value;
        s.WebpQuality = (int)webpQuality.Value;
        s.CheckUpdates = checkUpd.IsChecked == true;
        s.CaptureDelay = (int)delay.Value;
        s.ScrollDelayMs = (int)scrollDelay.Value;
        s.HideEditorOnCapture = hideEditorCb.IsChecked == true;
        s.DimOutside = dimCb.IsChecked == true;
        s.ExternalProgram = extProg.Text.Trim(); s.ExternalArgs = extArgs.Text.Trim();
        s.IncludeCursor = cursorCb.IsChecked == true;
        s.OpenEditor = openEditor.IsChecked == true;
        s.SaveOnClose = saveOnClose.IsChecked == true;
        s.CopyToClipboard = copy.IsChecked == true;
        SetAutostart(autostart.IsChecked == true);
        s.Save();
        if (failed.Count > 0)
        {
            msg.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x65));
            msg.Text = "Gespeichert. Diese Kürzel sind aber gerade nicht aktiv, weil ein anderes Programm sie belegt: " +
                       string.Join(", ", failed) + ". Beende das andere Programm und starte Knipser neu oder wähle ein anderes Kürzel.";
            return;   // Fenster offen lassen, damit die Meldung gelesen wird
        }
        Close();
    }
}
