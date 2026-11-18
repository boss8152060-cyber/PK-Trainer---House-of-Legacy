# PK Trainer - House of Legacy : installer / uninstaller
# Usage: double-click Install.bat or Uninstall.bat (this script is called by them).
param(
    [switch]$Uninstall,
    [string]$GameDir = ''   # optional: skip auto-detection and use this folder
)

$ErrorActionPreference = 'Stop'
$GameExe = 'House of Legacy.exe'
$PluginName = 'PKTrainer'

function Write-Info($m) { Write-Host "[PK Trainer] $m" }

function Get-SteamLibraries {
    $roots = @()
    foreach ($key in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam') {
        try {
            $p = (Get-ItemProperty -Path $key -ErrorAction Stop)
            if ($p.SteamPath) { $roots += ($p.SteamPath -replace '/', '\') }
            if ($p.InstallPath) { $roots += $p.InstallPath }
        } catch { }
    }
    $roots += "${env:ProgramFiles(x86)}\Steam", "$env:ProgramFiles\Steam"
    $libs = @()
    foreach ($r in ($roots | Where-Object { $_ } | Select-Object -Unique)) {
        if (-not (Test-Path $r)) { continue }
        $libs += $r
        $vdf = Join-Path $r 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            foreach ($line in Get-Content -LiteralPath $vdf -Encoding UTF8) {
                if ($line -match '"path"\s+"([^"]+)"') { $libs += ($Matches[1] -replace '\\\\', '\') }
            }
        }
    }
    return $libs | Select-Object -Unique
}

function Find-GameDir {
    foreach ($lib in Get-SteamLibraries) {
        $common = Join-Path $lib 'steamapps\common'
        if (-not (Test-Path $common)) { continue }
        foreach ($d in Get-ChildItem -LiteralPath $common -Directory -ErrorAction SilentlyContinue) {
            if (Test-Path -LiteralPath (Join-Path $d.FullName $GameExe)) { return $d.FullName }
        }
    }
    return $null
}

function Ask-GameDir {
    Write-Info 'Could not find the game automatically.'
    try {
        Add-Type -AssemblyName System.Windows.Forms
        $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
        $dlg.Description = "Select the House of Legacy game folder (the one containing '$GameExe')"
        if ($dlg.ShowDialog() -eq 'OK') { return $dlg.SelectedPath }
    } catch { }
    $p = Read-Host "Paste the game folder path (containing '$GameExe')"
    return $p.Trim('"').Trim()
}

try {
    $game = $GameDir
    if (-not $game) { $game = Find-GameDir }
    if ($game) { Write-Info "Found game: $game" } else { $game = Ask-GameDir }
    if (-not $game -or -not (Test-Path -LiteralPath (Join-Path $game $GameExe))) {
        throw "'$GameExe' not found in: $game"
    }
    if (Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($GameExe)) -ErrorAction SilentlyContinue) {
        throw 'The game is running. Close it completely and run this again.'
    }

    $pluginDir = Join-Path $game "BepInEx\plugins\$PluginName"

    if ($Uninstall) {
        if (Test-Path -LiteralPath $pluginDir) {
            Remove-Item -LiteralPath $pluginDir -Recurse -Force
            Write-Info 'PK Trainer removed.'
        } else {
            Write-Info 'PK Trainer is not installed.'
        }
        Write-Info 'BepInEx was left in place (other mods may use it). Delete the BepInEx folder, winhttp.dll and doorstop_config.ini to remove it too.'
        return
    }

    $files = Join-Path $PSScriptRoot 'files'
    if (-not (Test-Path -LiteralPath $files)) { throw "Folder 'files' not found next to the installer. Extract the whole zip first." }

    $hasBepInEx = Test-Path -LiteralPath (Join-Path $game 'BepInEx\core\BepInEx.dll')
    if ($hasBepInEx) {
        Write-Info 'BepInEx already installed - installing the plugin only.'
        New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
        Copy-Item -Path (Join-Path $files "BepInEx\plugins\$PluginName\*") -Destination $pluginDir -Recurse -Force
    } else {
        Write-Info 'Installing BepInEx + plugin.'
        Copy-Item -Path (Join-Path $files '*') -Destination $game -Recurse -Force
    }
    Get-ChildItem -LiteralPath $pluginDir -Recurse -File | Unblock-File -ErrorAction SilentlyContinue

    Write-Info 'Done! Start the game, load a save, then press Insert / F9 or click the PK Trainer button at the bottom-left.'
}
catch {
    Write-Host ''
    Write-Host "[PK Trainer] ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
