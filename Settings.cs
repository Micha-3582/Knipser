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
    /// <summary>Beim Start still nach neuer Version suchen (kein Popup, nur Eintrag im Tray-Menü).</summary>
    public bool CheckUpdates { get; set; } = true;
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
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { }
        var s = new Settings();
        s.Save();
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
