# Knipser

Schlankes Screenshot-Programm für Windows (C#, WPF, .NET 8) – ohne Werbung, ohne Update-Nerverei.

## Funktionen
- Aufnahme per Tastenkürzel: Bereich, Vollbild (alle Monitore), Fenster, scrollende Aufnahme
- Editor: Pfeil, Linie, Rahmen, Ellipse, Stift, Marker, Text, Nummern, Verpixeln, Zuschneiden; Elemente lassen sich nachträglich verschieben und in der Größe ändern
- Farbpipette, Lupe und Lineal
- Speichern als JPG oder PNG, automatisch beim Schließen des Editors (abschaltbar)
- Alle Tastenkürzel frei einstellbar, Autostart, eigener Speicherordner
- Updates über GitHub: Eintrag im Tray-Menü bzw. Einstellungen → „Nach Updates suchen“

## Installation
Voraussetzung: [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).

Aus dem neuesten [Release](../../releases/latest) `Knipser-Setup-X.Y.Z.exe` herunterladen und ausführen. Das Setup installiert Knipser pro Benutzer (ohne Administratorrechte), legt einen Startmenü-Eintrag an und richtet auf Wunsch den Autostart ein. Die lose `Knipser.exe` im Release ist nur für das automatische Update in Knipser gedacht und läuft auf einem neuen Rechner ohne .NET 8 Desktop Runtime nicht – zum Installieren immer das Setup nehmen. Das Setup lädt die Runtime bei Bedarf nach.

Das Programm läuft im Tray. Standard: `PrintScreen` Bereich, `Strg+PrintScreen` Vollbild, `Alt+PrintScreen` Fenster, `Strg+Umschalt+PrintScreen` scrollende Aufnahme, `Strg+Umschalt+F10/F11/F12` Farbpipette/Lupe/Lineal.

## Selbst bauen
```
dotnet build -c Release
```

## Release veröffentlichen
`<Version>` in `Knipser.csproj` hochzählen, dann `tools\build-release.ps1` ausführen (Inno Setup 6 nötig). Das Skript baut die Einzeldatei und das Setup und legt beide versioniert unter `K:\Claude Projekte\Knipser-Versionen\` ab, ohne etwas zu überschreiben. Danach:
```
gh release create vX.Y.Z publish/Knipser.exe "K:\Claude Projekte\Knipser-Versionen\Knipser-Setup-X.Y.Z.exe"
```
Das Asset `Knipser.exe` braucht die eingebaute Update-Funktion.
