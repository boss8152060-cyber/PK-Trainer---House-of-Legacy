# Build PK Trainer and create the release zip in dist\
# Usage: powershell -ExecutionPolicy Bypass -File tools\package.ps1 [-GameDir "<game folder>"] [-Version 1.2.1]
#
# BepInEx files are copied from your own game folder (it must already have BepInEx 5.x installed),
# or from -BepInExDir if you point to an extracted BepInEx_x64 release.
param(
    [string]$GameDir = '',
    [string]$BepInExDir = '',
    [string]$Version = '1.2.1'
)
$ErrorActionPreference = 'Stop'
if (-not $GameDir) {
    $hit = Get-ChildItem "${env:ProgramFiles(x86)}\Steam\steamapps\common\*\House of Legacy.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $hit) { throw 'Game folder not found. Pass -GameDir "<folder containing House of Legacy.exe>"' }
    $GameDir = $hit.DirectoryName
}
$root = Split-Path $PSScriptRoot -Parent
$name = "PK Trainer - House of Legacy"
$stage = Join-Path $root "dist\stage\$name"
$zip = Join-Path $root "dist\$name v$Version.zip"
if (-not $BepInExDir) { $BepInExDir = $GameDir }

foreach ($f in 'winhttp.dll', 'doorstop_config.ini', 'BepInEx\core\BepInEx.dll') {
    if (-not (Test-Path (Join-Path $BepInExDir $f))) { throw "BepInEx file not found: $BepInExDir\$f" }
}

Write-Host '== Build plugin'
$out = Join-Path $root 'dist\build'
dotnet build (Join-Path $root 'src\PKTrainer\PKTrainer.csproj') -c Release -o $out "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

Write-Host '== Stage files'
if (Test-Path (Join-Path $root 'dist\stage')) { Remove-Item (Join-Path $root 'dist\stage') -Recurse -Force }
$files = Join-Path $stage 'files'
New-Item -ItemType Directory -Force -Path (Join-Path $files 'BepInEx\core'), (Join-Path $files 'BepInEx\plugins\PKTrainer') | Out-Null
Copy-Item (Join-Path $BepInExDir 'winhttp.dll'), (Join-Path $BepInExDir 'doorstop_config.ini') $files
Copy-Item (Join-Path $BepInExDir 'BepInEx\core\*') (Join-Path $files 'BepInEx\core')
Copy-Item (Join-Path $out 'PKTrainer.dll') (Join-Path $files 'BepInEx\plugins\PKTrainer')
Copy-Item (Join-Path $root 'installer\*') $stage
Copy-Item (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD-PARTY.md') $stage

Write-Host '== Zip'
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $stage -DestinationPath $zip
Remove-Item (Join-Path $root 'dist\stage'), $out -Recurse -Force
Get-Item $zip | Select-Object Name, Length
