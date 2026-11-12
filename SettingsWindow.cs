using System;
using System.Collections.Generic;
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
    readonly TextBox region = Hk(), full = Hk(), win = Hk(), scroll = Hk();
    readonly TextBox folder = new() { Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), Padding = new Thickness(6, 4, 6, 4), VerticalContentAlignment = VerticalAlignment.Center };
    readonly CheckBox openEditor = new() { Content = "Nach der Aufnahme den Editor öffnen" };
    readonly CheckBox saveOnClose = new() { Content = "Beim Schließen des Editors automatisch speichern" };
    readonly CheckBox copy = new() { Content = "Aufnahme automatisch in die Zwischenablage kopieren" };
    readonly CheckBox autostart = new() { Content = "Mit Windows starten" };
    readonly TextBlock msg = new() { Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x65)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

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
        Width = 560; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1B));
        Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(Head("Tastenkürzel"));
        root.Children.Add(Row("Bereich aufnehmen", region));
        root.Children.Add(Row("Vollbild aufnehmen", full));
        root.Children.Add(Row("Fenster unter dem Mauszeiger", win));
        root.Children.Add(Row("Scrollende Aufnahme", scroll));

        root.Children.Add(Head("Speichern"));
        var fr = new DockPanel { Margin = new Thickness(0, 2, 0, 6) };
        var browse = new Button { Content = "Durchsuchen…", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(browse, Dock.Right);
        fr.Children.Add(browse); fr.Children.Add(folder);
        root.Children.Add(fr);
        browse.Click += (_, _) =>
        {
            using var d = new Forms.FolderBrowserDialog { SelectedPath = folder.Text, Description = "Speicherordner für Screenshots" };
            if (d.ShowDialog() == Forms.DialogResult.OK) folder.Text = d.SelectedPath;
        };

        root.Children.Add(Head("Verhalten"));
        foreach (var c in new[] { openEditor, saveOnClose, copy, autostart })
        {
            c.Foreground = Foreground; c.Margin = new Thickness(0, 3, 0, 3);
            root.Children.Add(c);
        }
        root.Children.Add(msg);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = "Speichern", Padding = new Thickness(18, 6, 18, 6), IsDefault = true };
        var cancel = new Button { Content = "Abbrechen", Padding = new Thickness(18, 6, 18, 6), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        Content = root;

        region.Text = s.HotkeyRegion; full.Text = s.HotkeyFullscreen; win.Text = s.HotkeyWindow; scroll.Text = s.HotkeyScroll;
        folder.Text = s.SaveFolder;
        openEditor.IsChecked = s.OpenEditor; saveOnClose.IsChecked = s.SaveOnClose; copy.IsChecked = s.CopyToClipboard;
        autostart.IsChecked = GetAutostart();

        foreach (var t in new[] { region, full, win, scroll }) t.PreviewKeyDown += HotkeyKey;
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

    void HotkeyKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var box = (TextBox)sender;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin or Key.None or Key.ImeProcessed or Key.DeadCharProcessed) return;
        if (key is Key.Delete or Key.Back) { box.Text = ""; return; }
        if (key == Key.Tab) { box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); return; }

        var vk = KeyInterop.VirtualKeyFromKey(key);
        var parts = new List<string>();
        var m = Keyboard.Modifiers;
        if (m.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (m.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (m.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        parts.Add(((Forms.Keys)vk).ToString());
        box.Text = string.Join("+", parts);
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
        var list = new[] { region.Text, full.Text, win.Text, scroll.Text };
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in list)
            if (!string.IsNullOrEmpty(h) && !used.Add(h)) { msg.Text = $"„{h}“ ist doppelt vergeben."; return; }

        var old = (s.HotkeyRegion, s.HotkeyFullscreen, s.HotkeyWindow, s.HotkeyScroll);
        s.HotkeyRegion = region.Text; s.HotkeyFullscreen = full.Text; s.HotkeyWindow = win.Text; s.HotkeyScroll = scroll.Text;
        var failed = app.RegisterAll();
        if (failed.Count > 0)
        {
            (s.HotkeyRegion, s.HotkeyFullscreen, s.HotkeyWindow, s.HotkeyScroll) = old;
            app.RegisterAll();
            msg.Text = "Nicht möglich, von einem anderen Programm belegt: " + string.Join(", ", failed);
            return;
        }
        s.SaveFolder = string.IsNullOrWhiteSpace(folder.Text) ? s.SaveFolder : folder.Text.Trim();
        s.OpenEditor = openEditor.IsChecked == true;
        s.SaveOnClose = saveOnClose.IsChecked == true;
        s.CopyToClipboard = copy.IsChecked == true;
        SetAutostart(autostart.IsChecked == true);
        s.Save();
        Close();
    }
}
