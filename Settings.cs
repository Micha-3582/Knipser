using System;
using System.IO;
using System.Text.Json;

namespace Knipser;

public class Settings
{
    public string SaveFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Knipser");
    /// <summary>Standard-Bildformat beim Speichern: "jpg" oder "png".</summary>
    public string ImageFormat { get; set; } = "jpg";
    /// <summary>JPG-Qualität 1–100 (100 = beste Qualität, größte Datei).</summary>
    public int JpgQuality { get; set; } = 92;
    /// <summary>Beim Start still nach neuer Version suchen (kein Popup, nur Eintrag im Tray-Menü).</summary>
    public bool CheckUpdates { get; set; } = true;
    /// <summary>Sekunden Wartezeit vor der Aufnahme (0 = sofort).</summary>
    public int CaptureDelay { get; set; } = 0;
    /// <summary>Mauszeiger im Bild mitfotografieren.</summary>
    public bool IncludeCursor { get; set; } = false;
    /// <summary>Pfeilspitze im Editor: 0 Klassisch, 1 Spitz, 2 Offen, 3 Modern (Standard), 4 Punkt.</summary>
    public int ArrowStyle { get; set; } = 3;
    public bool OpenEditor { get; set; } = true;
    public bool CopyToClipboard { get; set; } = true;
    /// <summary>Editor schließen speichert das Bild automatisch (wie bei PicPick).</summary>
    public bool SaveOnClose { get; set; } = true;
    public string HotkeyRegion { get; set; } = "PrintScreen";
    public string HotkeyFullscreen { get; set; } = "Ctrl+PrintScreen";
    public string HotkeyWindow { get; set; } = "Alt+PrintScreen";
    public string HotkeyScroll { get; set; } = "Ctrl+Shift+PrintScreen";
    public string HotkeyPicker { get; set; } = "Ctrl+Shift+F10";
    public string HotkeyMagnifier { get; set; } = "Ctrl+Shift+F11";
    public string HotkeyRuler { get; set; } = "Ctrl+Shift+F12";

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Knipser", "settings.json");

    public static Settings Load()
    {
        if (File.Exists(FilePath))
        {
            try
            {
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
            }
            catch
            {
                // Datei kaputt: nie stillschweigend überschreiben, sondern sichern
                try { File.Copy(FilePath, FilePath + ".defekt", true); } catch { }
                return new Settings();
            }
        }
        var s = new Settings();
        s.Save();
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, FilePath, true);   // atomar: nie eine halb geschriebene Datei
        }
        catch { }
    }
}
