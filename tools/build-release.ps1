# Baut Knipser als Einzeldatei + Setup und legt BEIDE versioniert ab (nie überschreiben):
#   K:\Claude Projekte\Knipser-Versionen\Knipser-<Version>.exe
#   K:\Claude Projekte\Knipser-Versionen\Knipser-Setup-<Version>.exe
# Vorher <Version> in Knipser.csproj hochzählen.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
$archive = 'K:\Claude Projekte\Knipser-Versionen'

$csproj = Get-Content (Join-Path $root 'Knipser.csproj') -Raw
$version = [regex]::Match($csproj, '<Version>([^<]+)</Version>').Groups[1].Value
if (-not $version) { throw 'Version nicht gefunden.' }

New-Item -ItemType Directory -Force $archive | Out-Null
$exeOut = Join-Path $archive "Knipser-$version.exe"
$setupOut = Join-Path $archive "Knipser-Setup-$version.exe"
if ((Test-Path $exeOut) -or (Test-Path $setupOut)) { throw "Version $version existiert schon im Archiv. Bitte Version hochzählen." }

$pub = Join-Path $root 'publish'
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
& $dotnet publish (Join-Path $root 'Knipser.csproj') -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $pub | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish fehlgeschlagen.' }

Copy-Item (Join-Path $pub 'Knipser.exe') $exeOut
& $iscc "/DMyVersion=$version" "/DSourceExe=$(Join-Path $pub 'Knipser.exe')" "/DOutDir=$archive" (Join-Path $root 'installer\Knipser.iss') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup fehlgeschlagen.' }

"Fertig: Version $version"
Get-Item $exeOut, $setupOut | Select-Object Name, @{n = 'MB'; e = { [math]::Round($_.Length / 1MB, 2) } }
