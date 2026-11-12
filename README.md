# Knipser

Schlankes Screenshot-Programm für Windows (C#, WPF, .NET 8) – ohne Werbung, ohne Update-Nerverei.

## Funktionen
- Aufnahme per Tastenkürzel: Bereich, Vollbild (alle Monitore), Fenster, scrollende Aufnahme
- Editor: Pfeil, Linie, Rahmen, Ellipse, Stift, Marker, Text, Nummern, Verpixeln, Zuschneiden
- Speichern als JPG oder PNG, automatisch beim Schließen des Editors (abschaltbar)
- Alle Tastenkürzel frei einstellbar, Autostart, eigener Speicherordner
- Updates über GitHub: Eintrag im Tray-Menü bzw. Einstellungen → „Nach Updates suchen“

## Installation
Voraussetzung: [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).
`Knipser.exe` aus dem neuesten [Release](../../releases/latest) herunterladen und starten.
Das Programm läuft im Tray. Standard: `PrintScreen` Bereich, `Strg+PrintScreen` Vollbild,
`Alt+PrintScreen` Fenster, `Strg+Umschalt+PrintScreen` scrollende Aufnahme.

## Selbst bauen
```
dotnet build -c Release
```

## Release veröffentlichen
```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
gh release create vX.Y.Z publish/Knipser.exe --title "Knipser X.Y.Z" --notes "..."
```
Vorher `<Version>` in `Knipser.csproj` hochzählen.
