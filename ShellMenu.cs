using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace Knipser;

/// <summary>Eintrag „Mit Knipser bearbeiten“ im Explorer-Rechtsklickmenü.
/// Der Explorer zeigt Einträge nur aus HKLM zuverlässig an, deshalb braucht das Setzen einmalig Administratorrechte.</summary>
static class ShellMenu
{
    static readonly string[] Exts = { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".gif" };
    const string Base = @"Software\Classes\SystemFileAssociations\";

    public static bool IsOn()
    {
        try { using var k = Registry.LocalMachine.OpenSubKey(Base + @".jpg\shell\Knipser\command"); return k != null; }
        catch { return false; }
    }

    /// <summary>Startet Knipser mit Administratorrechten, um den Eintrag zu setzen/entfernen. Liefert false, wenn abgebrochen.</summary>
    public static bool Set(bool on)
    {
        try
        {
            var psi = new ProcessStartInfo(Environment.ProcessPath!, on ? "--menu-on" : "--menu-off") { UseShellExecute = true, Verb = "runas" };
            using var p = Process.Start(psi);
            p?.WaitForExit();
            return p?.ExitCode == 0;
        }
        catch { return false; }   // UAC abgelehnt
    }

    /// <summary>Wird im erhöhten Prozess ausgeführt (Kommandozeile --menu-on / --menu-off).</summary>
    public static int Apply(bool on)
    {
        try
        {
            foreach (var e in Exts)
            {
                var path = Base + e + @"\shell\Knipser";
                if (on)
                {
                    using var k = Registry.LocalMachine.CreateSubKey(path);
                    k.SetValue("", "Mit Knipser bearbeiten");
                    k.SetValue("Icon", "\"" + Environment.ProcessPath + "\"");
                    using var c = Registry.LocalMachine.CreateSubKey(path + @"\command");
                    c.SetValue("", "\"" + Environment.ProcessPath + "\" \"%1\"");
                }
                else Registry.LocalMachine.DeleteSubKeyTree(path, false);
                // Reste aus Version 1.33.0 (pro Benutzer, wirkungslos) aufräumen
                Registry.CurrentUser.DeleteSubKeyTree(path, false);
            }
            return 0;
        }
        catch { return 1; }
    }
}
