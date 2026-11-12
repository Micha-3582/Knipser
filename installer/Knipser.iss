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

// .NET 8 Desktop Runtime vorhanden? (Knipser ist ein kleines Programm, das die Runtime nutzt)
function HasDotNet8(): Boolean;
var
  r: TFindRec;
begin
  // Ordner der Desktop Runtime 8.x suchen (die Registry-Prüfung war unzuverlässig)
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), r) then
  try
    Result := True;
  finally
    FindClose(r);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  dl: TDownloadWizardPage;
  rc: Integer;
  f: String;
begin
  KillKnipser();
  Result := '';
  if HasDotNet8() then Exit;
  if MsgBox('Knipser benötigt die kostenlose Microsoft-Komponente ".NET 8 Desktop Runtime" (ca. 55 MB). Sie fehlt auf diesem Rechner.' + #13#10 + #13#10 +
            'Jetzt herunterladen und installieren? (Windows fragt kurz nach Administrator-Rechten.)', mbConfirmation, MB_YESNO) <> IDYES then
  begin
    Result := 'Ohne die .NET 8 Desktop Runtime läuft Knipser nicht. Die Installation wurde abgebrochen.';
    Exit;
  end;
  dl := CreateDownloadPage('Komponente laden', '.NET 8 Desktop Runtime wird heruntergeladen …', nil);
  dl.Clear;
  dl.Add('https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe', 'dotnet8-desktop-runtime.exe', '');
  dl.Show;
  try
    try
      dl.Download;
    except
      Result := 'Der Download der .NET 8 Desktop Runtime ist fehlgeschlagen: ' + GetExceptionMessage + #13#10 + 'Bitte von https://dotnet.microsoft.com/download/dotnet/8.0 ("Desktop Runtime", x64) manuell installieren.';
      Exit;
    end;
  finally
    dl.Hide;
  end;
  f := ExpandConstant('{tmp}\dotnet8-desktop-runtime.exe');
  if not ShellExec('runas', f, '/install /quiet /norestart', '', SW_SHOWNORMAL, ewWaitUntilTerminated, rc) or ((rc <> 0) and (rc <> 3010)) then
    Result := 'Die Installation der .NET 8 Desktop Runtime ist fehlgeschlagen (Code ' + IntToStr(rc) + ').';
end;

function InitializeUninstall(): Boolean;
begin
  KillKnipser();
  Result := True;
end;
