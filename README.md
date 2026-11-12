# Knipser

Schlankes Screenshot-Programm fÃ¼r Windows (C#, WPF, .NET 8) â€“ ohne Werbung, ohne Update-Nerverei.

## Funktionen
- Aufnahme per TastenkÃ¼rzel: Bereich, Vollbild (alle Monitore), Fenster, scrollende Aufnahme
- Editor: Pfeil, Linie, Rahmen, Ellipse, Stift, Marker, Text, Nummern, Verpixeln, Zuschneiden
- Speichern als JPG oder PNG, automatisch beim SchlieÃŸen des Editors (abschaltbar)
- Alle TastenkÃ¼rzel frei einstellbar, Autostart, eigener Speicherordner
- Updates Ã¼ber GitHub: Eintrag im Tray-MenÃ¼ bzw. Einstellungen â†’ â€žNach Updates suchenâ€œ

## Installation
Voraussetzung: [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).
Aus dem neuesten [Release](../../releases/latest) `Knipser-Setup-X.Y.Z.exe` herunterladen und ausführen. Das Setup installiert Knipser pro Benutzer (ohne Administratorrechte), legt einen Startmenü-Eintrag an und richtet auf Wunsch den Autostart ein. Alternativ gibt es `Knipser.exe` als portable Einzeldatei.
Das Programm läuft im Tray. Standard: `PrintScreen` Bereich, `Strg+PrintScreen` Vollbild, `Alt+PrintScreen` Fenster, `Strg+Umschalt+PrintScreen` scrollende Aufnahme, `Strg+Umschalt+F10/F11/F12` Farbpipette/Lupe/Lineal.

## Selbst bauen
```
dotnet build -c Release
```

## Release verÃ¶ffentlichen
```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
gh release create vX.Y.Z publish/Knipser.exe --title "Knipser X.Y.Z" --notes "..."
```
Vorher `<Version>` in `Knipser.csproj` hochzÃ¤hlen.

