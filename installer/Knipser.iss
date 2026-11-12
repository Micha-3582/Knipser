; Inno-Setup-Skript für Knipser. Wird von tools\build-release.ps1 aufgerufen:
;   ISCC /DMyVersion=1.2.0 /DSourceExe=...\Knipser.exe /DOutDir=...\Versionen Knipser.iss
#ifndef MyVersion
  #define MyVersion "0.0.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\publish\Knipser.exe"
#endif
#ifndef OutDir
  #define OutDir "..\publish"
#endif

[Setup]
AppId={{6B1F2C54-8D3A-4E7B-9C21-5A0E47D3B9F1}
AppName=Knipser
AppVersion={#MyVersion}
AppVerName=Knipser {#MyVersion}
AppPublisher=Michael Arthen
AppPublisherURL=https://github.com/Micha-3582/Knipser
AppSupportURL=https://github.com/Micha-3582/Knipser/issues
DefaultDirName={autopf}\Knipser
DefaultGroupName=Knipser
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\Knipser.exe
UninstallDisplayName=Knipser
SetupIconFile=..\Knipser.ico
OutputDir={#OutDir}
OutputBaseFilename=Knipser-Setup-{#MyVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Pro Benutzer installieren: kein Administrator nötig, Updates aus dem Programm heraus funktionieren
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "autostart"; Description: "Knipser beim Anmelden automatisch starten"; GroupDescription: "Zusätzliche Optionen:"
Name: "desktopicon"; Description: "Symbol auf dem Desktop anlegen"; GroupDescription: "Zusätzliche Optionen:"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "Knipser.exe"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Knipser"; Filename: "{app}\Knipser.exe"
Name: "{autodesktop}\Knipser"; Filename: "{app}\Knipser.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Knipser"; ValueData: """{app}\Knipser.exe"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\Knipser.exe"; Description: "Knipser jetzt starten"; Flags: nowait postinstall skipifsilent

[Code]
// Laufendes Knipser vor Installation/Update/Deinstallation beenden
procedure KillKnipser();
var
  rc: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM Knipser.exe', '', SW_HIDE, ewWaitUntilTerminated, rc);
  Sleep(500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  KillKnipser();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  KillKnipser();
  Result := True;
end;
