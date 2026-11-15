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
Compression=lzma2/max
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
Name: "contextmenu"; Description: "Im Rechtsklickmenü von Bildern „Mit Knipser bearbeiten“ anzeigen (Windows fragt dafür einmal nach der Administrator-Erlaubnis)"; GroupDescription: "Zusätzliche Optionen:"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "Knipser.exe"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Knipser"; Filename: "{app}\Knipser.exe"
Name: "{autodesktop}\Knipser"; Filename: "{app}\Knipser.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Knipser"; ValueData: """{app}\Knipser.exe"""; Flags: uninsdeletevalue; Tasks: autostart


[Run]
; Rechtsklick-Eintrag im Explorer: braucht einmalig Administratorrechte (nur wenn angehakt und noch nicht gesetzt, daher fragt ein späteres Update nicht erneut)
Filename: "{app}\Knipser.exe"; Parameters: "--menu-on"; Verb: "runas"; Flags: shellexec waituntilterminated; Tasks: contextmenu; Check: MenuNotSet; StatusMsg: "Rechtsklick-Eintrag wird eingerichtet..."
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

const MenuKey = 'Software\Classes\SystemFileAssociations\.jpg\shell\Knipser\command';

function MenuNotSet(): Boolean;
begin
  Result := not RegKeyExists(HKLM, MenuKey);
end;

// Bei der Deinstallation den Rechtsklick-Eintrag wieder entfernen (nur wenn vorhanden)
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  rc: Integer;
begin
  if (CurUninstallStep = usUninstall) and RegKeyExists(HKLM, MenuKey) then
    ShellExec('runas', ExpandConstant('{app}\Knipser.exe'), '--menu-off', '', SW_HIDE, ewWaitUntilTerminated, rc);
end;
