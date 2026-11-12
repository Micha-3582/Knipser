using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

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
    /// <summary>Fester Bereich: Größe in Pixeln.</summary>
    public int FixedWidth { get; set; } = 800;
    public int FixedHeight { get; set; } = 600;
    public string HotkeyFixed { get; set; } = "";
    public string HotkeyRepeat { get; set; } = "";
    public string HotkeyFreehand { get; set; } = "";
    /// <summary>Dateiname ohne Endung; Datum/Uhrzeit in geschweiften Klammern, z. B. Knipser_{yyyy-MM-dd_HH-mm-ss}.</summary>
    public string FileNamePattern { get; set; } = "Knipser_{yyyy-MM-dd_HH-mm-ss}";
    /// <summary>Farbpipette kopiert: hex, rgb oder cpp.</summary>
    public string ColorFormat { get; set; } = "hex";
    /// <summary>Zuletzt gewählter Bildrahmen (Reiter Bild).</summary>
    public int FrameWidth { get; set; } = 6;
    public int FrameRadius { get; set; } = 0;
    public string FrameColor { get; set; } = "#E53935";
    /// <summary>Zuletzt gewählter Bild-Schatten.</summary>
    public int ShadowOffset { get; set; } = 6;
    public int ShadowBlur { get; set; } = 7;
    public int ShadowOpacity { get; set; } = 50;
    public int ShadowRadius { get; set; } = 0;
    /// <summary>Form für Verpixeln / Weichzeichnen: 0 Rechteck, 1 Ellipse, 2 Freihand (Lasso).</summary>
    public int BlurShape { get; set; } = 0;
    public int SoftenShape { get; set; } = 0;
    /// <summary>Kante von Verpixeln / Weichzeichnen: 0 hart, 1 weich, 2 sehr weich.</summary>
    public int BlurEdge { get; set; } = 0;
    public int SoftenEdge { get; set; } = 0;
    /// <summary>Eigene Farbfelder im Farbwähler (8 Plätze, leer = frei), als #RRGGBB.</summary>
    public List<string> CustomColors { get; set; } = new();
    /// <summary>Pipette: Aufnahmebereich in Pixeln (1 = ein Pixel, 3/5/11/31 = Durchschnitt über n × n Pixel).</summary>
    public int PickSample { get; set; } = 1;
    /// <summary>Lupe der Pipette: sichtbarer Ausschnitt in Pixeln (Mausrad ändert ihn).</summary>
    public int LoupePixels { get; set; } = 15;
    /// <summary>Editor-Fenster vor der Aufnahme ausblenden (damit sie nicht mit fotografiert werden).</summary>
    public bool HideEditorOnCapture { get; set; } = true;
    /// <summary>Bildschirm außerhalb der Auswahl abdunkeln.</summary>
    public bool DimOutside { get; set; } = true;
    /// <summary>Scrollende Aufnahme: Wartezeit nach jedem Mausrad-Schritt in Millisekunden.</summary>
    public int ScrollDelayMs { get; set; } = 300;
    /// <summary>Laufende Nummer für den Platzhalter {n} im Dateinamen.</summary>
    public int FileCounter { get; set; } = 0;
    /// <summary>Externes Programm zum Öffnen der Aufnahme (Editor: Datei → Öffnen mit), Parameter mit %f für den Dateipfad.</summary>
    public string ExternalProgram { get; set; } = "";
    public string ExternalArgs { get; set; } = "";
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

    /// <summary>Dateiname aus einem Muster: {n} laufende Nummer, {user} Benutzer, {pc} Computer, sonst Datum/Uhrzeit in { }.</summary>
    public static string Expand(string pattern, int counter)
    {
        var pat = string.IsNullOrWhiteSpace(pattern) ? "Knipser_{yyyy-MM-dd_HH-mm-ss}" : pattern;
        pat = pat.Replace("{n}", counter.ToString("000")).Replace("{user}", Environment.UserName).Replace("{pc}", Environment.MachineName);
        var name = Regex.Replace(pat, @"\{([^{}]+)\}", m =>
        {
            try { return DateTime.Now.ToString(m.Groups[1].Value); } catch { return m.Value; }
        });
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c.ToString(), "");
        name = name.Trim().TrimEnd('.');
        return name.Length == 0 ? "Knipser" : name;
    }

    /// <summary>Dateiname (ohne Endung) aus dem Muster, ungültige Zeichen entfernt.</summary>
    public string MakeFileName() => Expand(FileNamePattern, FileCounter + 1);

    /// <summary>Freien Dateipfad im Speicherordner (hängt bei Bedarf _2, _3 … an).</summary>
    public string UniquePath(string ext)
    {
        var baseName = MakeFileName();
        var p = Path.Combine(SaveFolder, baseName + "." + ext);
        for (int i = 2; File.Exists(p); i++) p = Path.Combine(SaveFolder, $"{baseName}_{i}.{ext}");
        if (FileNamePattern.Contains("{n}")) { FileCounter++; Save(); }
        return p;
    }

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
