<# :
@echo off
rem ScheduleOne Navigator - crash info collector.
rem Double-click this file. It gathers the logs needed for a bug report and
rem puts them into one zip file on your desktop. Nothing is uploaded.
rem (Batch/PowerShell hybrid: everything below the batch part is PowerShell.)
title ScheduleOne Navigator - Crash Info Collector
set "NAV_SCRIPT=%~f0"
set "NAV_SCRIPT_DIR=%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -Command "iex ([IO.File]::ReadAllText($env:NAV_SCRIPT))"
echo.
pause
exit /b
#>

$ErrorActionPreference = 'Continue'
$AppId = '3164500'
$GameFolderName = 'Schedule I'
$GameExe = 'Schedule I.exe'

function Say($text, $color = 'Gray') { Write-Host $text -ForegroundColor $color }

Say ''
Say '=== ScheduleOne Navigator - Crash Info Collector ===' 'Cyan'
Say ''

# ---------------------------------------------------------------- game folder
function Test-GameDir($dir) {
    return $dir -and (Test-Path -LiteralPath (Join-Path $dir $GameExe))
}

function Get-SteamLibraries {
    $libs = @()
    $steamRoots = @()
    foreach ($key in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam') {
        try {
            $p = Get-ItemProperty -Path $key -ErrorAction Stop
            if ($p.SteamPath) { $steamRoots += ($p.SteamPath -replace '/', '\') }
            if ($p.InstallPath) { $steamRoots += $p.InstallPath }
        } catch { }
    }
    $steamRoots += "${env:ProgramFiles(x86)}\Steam"
    foreach ($root in ($steamRoots | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        $libs += $root
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $vdf) {
            foreach ($m in [regex]::Matches((Get-Content -LiteralPath $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $libs += ($m.Groups[1].Value -replace '\\\\', '\')
            }
        }
    }
    return $libs | Select-Object -Unique
}

$gameDir = $null
$libraryDir = $null
if ($env:NAV_GAMEDIR -and (Test-GameDir $env:NAV_GAMEDIR)) { $gameDir = $env:NAV_GAMEDIR }
if (-not $gameDir -and $env:NAV_SCRIPT_DIR) {
    # The tool may have been dropped into the game folder or one below it (Mods).
    $parent = $null
    try { $parent = [IO.Path]::GetDirectoryName($env:NAV_SCRIPT_DIR.TrimEnd('\')) } catch { }
    foreach ($d in @($env:NAV_SCRIPT_DIR, $parent)) {
        if ($d -and (Test-GameDir $d)) { $gameDir = $d; break }
    }
}
if (-not $gameDir) {
    foreach ($lib in Get-SteamLibraries) {
        $d = Join-Path $lib "steamapps\common\$GameFolderName"
        if (Test-GameDir $d) { $gameDir = $d; $libraryDir = $lib; break }
    }
}
if (-not $gameDir) {
    Say 'Could not find the Schedule I folder automatically.' 'Yellow'
    Say 'Please select it in the window that opens (the folder that contains "Schedule I.exe").' 'Yellow'
    try {
        Add-Type -AssemblyName System.Windows.Forms
        $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
        $dlg.Description = 'Select the Schedule I game folder (contains "Schedule I.exe")'
        if ($dlg.ShowDialog() -eq 'OK' -and (Test-GameDir $dlg.SelectedPath)) { $gameDir = $dlg.SelectedPath }
    } catch { }
}
if (-not $gameDir) {
    Say 'Schedule I folder not found - collecting what is available anyway.' 'Yellow'
} else {
    Say "Game folder: $gameDir" 'Green'
    if (-not $libraryDir) {
        # <library>\steamapps\common\Schedule I -> <library>
        $libraryDir = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $gameDir))
    }
}

# ---------------------------------------------------------------- output dir
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$work = Join-Path $env:TEMP "NavigatorCrashInfo-$stamp"
New-Item -ItemType Directory -Path $work -Force | Out-Null

$collected = New-Object System.Collections.ArrayList
function Copy-Into($src, $relDest) {
    if (-not (Test-Path -LiteralPath $src)) { return $false }
    $dest = Join-Path $work $relDest
    New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force | Out-Null
    try {
        # Logs can still be held open by a running game: read them shared.
        $in = [IO.File]::Open($src, 'Open', 'Read', 'ReadWrite')
        $out = [IO.File]::Create($dest)
        $in.CopyTo($out); $out.Close(); $in.Close()
        [void]$collected.Add($relDest)
        return $true
    } catch {
        Say "  could not copy $src : $($_.Exception.Message)" 'Yellow'
        return $false
    }
}

# ---------------------------------------------------------------- logs
Say ''
Say 'Collecting files...' 'Cyan'
$verbose = 'unknown'
$branch = 'default'
$buildId = '?'
$fileList = @()
if ($gameDir) {
    $mlDir = Join-Path $gameDir 'MelonLoader'
    [void](Copy-Into (Join-Path $mlDir 'Latest.log') 'MelonLoader\Latest.log')
    $logsDir = Join-Path $mlDir 'Logs'
    if (Test-Path -LiteralPath $logsDir) {
        Get-ChildItem -LiteralPath $logsDir -File | Sort-Object LastWriteTime -Descending | Select-Object -First 3 |
            ForEach-Object { [void](Copy-Into $_.FullName "MelonLoader\Logs\$($_.Name)") }
    }

    $cfg = Join-Path $gameDir 'UserData\MelonPreferences.cfg'
    if (Copy-Into $cfg 'UserData\MelonPreferences.cfg') {
        $m = [regex]::Match((Get-Content -LiteralPath $cfg -Raw), '(?ms)^\[ScheduleOneNavigator\].*?^\s*VerboseLogging\s*=\s*(\w+)')
        if ($m.Success) { $verbose = $m.Groups[1].Value }
    }

    foreach ($sub in 'Mods', 'Plugins', 'UserLibs') {
        $d = Join-Path $gameDir $sub
        if (Test-Path -LiteralPath $d) {
            $fileList += Get-ChildItem -LiteralPath $d -File -Recurse | ForEach-Object {
                $v = ''
                try { $v = $_.VersionInfo.FileVersion } catch { }
                '{0,-60} {1,10:N0} bytes  {2:yyyy-MM-dd HH:mm}  {3}' -f ($_.FullName.Substring($gameDir.Length + 1)), $_.Length, $_.LastWriteTime, $v
            }
        }
    }
    $fileList += Get-ChildItem -LiteralPath $gameDir -File -Filter '*.dll' | ForEach-Object {
        '{0,-60} {1,10:N0} bytes  {2:yyyy-MM-dd HH:mm}' -f $_.Name, $_.Length, $_.LastWriteTime
    }

    $acf = Join-Path $libraryDir "steamapps\appmanifest_$AppId.acf"
    if (Test-Path -LiteralPath $acf) {
        $acfText = Get-Content -LiteralPath $acf -Raw
        $m = [regex]::Match($acfText, '"buildid"\s+"(\d+)"'); if ($m.Success) { $buildId = $m.Groups[1].Value }
        $m = [regex]::Match($acfText, '"BetaKey"\s+"([^"]*)"'); if ($m.Success -and $m.Groups[1].Value) { $branch = $m.Groups[1].Value }
    }
}

# Unity's own logs and crash reports
$unityDir = Join-Path $env:USERPROFILE "AppData\LocalLow\TVGS\$GameFolderName"
[void](Copy-Into (Join-Path $unityDir 'Player.log') 'Unity\Player.log')
[void](Copy-Into (Join-Path $unityDir 'Player-prev.log') 'Unity\Player-prev.log')

$crashDirs = @()
foreach ($root in (Join-Path $env:TEMP "TVGS\$GameFolderName\Crashes"), (Join-Path $unityDir 'Crashes')) {
    if (Test-Path -LiteralPath $root) { $crashDirs += Get-ChildItem -LiteralPath $root -Directory }
}
$crashDirs | Where-Object { $_.LastWriteTime -gt (Get-Date).AddDays(-7) } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 2 | ForEach-Object {
        $cd = $_
        Get-ChildItem -LiteralPath $cd.FullName -File | Where-Object { $_.Length -lt 50MB } |
            ForEach-Object { [void](Copy-Into $_.FullName "Unity\Crashes\$($cd.Name)\$($_.Name)") }
    }

# Windows' own record of the crash (names the module that crashed)
$events = @()
try {
    $events = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = 'Application Error', 'Windows Error Reporting'; StartTime = (Get-Date).AddDays(-7) } -ErrorAction Stop |
        Where-Object { $_.Message -match 'Schedule I' } | Select-Object -First 10
} catch { }

# ---------------------------------------------------------------- summary
function Find-InLog($pattern) {
    $log = Join-Path $work 'MelonLoader\Latest.log'
    if (-not (Test-Path -LiteralPath $log)) { return '?' }
    $m = Select-String -LiteralPath $log -Pattern $pattern | Select-Object -First 1
    if ($m) { return $m.Matches[0].Groups[1].Value.Trim() } else { return '?' }
}

$os = $null; $cpu = $null; $gpus = @(); $ram = '?'
try { $os = Get-CimInstance Win32_OperatingSystem } catch { }
try { $cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name } catch { }
try { $gpus = Get-CimInstance Win32_VideoController | ForEach-Object { "$($_.Name) (driver $($_.DriverVersion))" } } catch { }
try { $ram = '{0:N1} GB' -f ((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB) } catch { }
$isWine = $false
try { if (Get-ItemProperty -Path 'HKCU:\Software\Wine' -ErrorAction Stop) { $isWine = $true } } catch { }

$summary = @()
$summary += 'ScheduleOne Navigator - crash info'
$summary += "Collected:          $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
$summary += ''
$summary += "Navigator version:  $(Find-InLog 'ScheduleOne Navigator v?([\d\.]+)')"
$summary += "MelonLoader:        $(Find-InLog 'MelonLoader v?([\d\.]+\S*)')"
$summary += "Game version:       $(Find-InLog 'Game Version:\s*(.+)')"
$summary += "Steam branch:       $branch (build $buildId)"
$summary += "VerboseLogging:     $verbose"
$summary += ''
if ($os) { $summary += "OS:                 $($os.Caption) $($os.Version)" }
if ($isWine) { $summary += 'Runs under:         Wine/Proton' }
$summary += "CPU:                $cpu"
$summary += "RAM:                $ram"
foreach ($g in $gpus) { $summary += "GPU:                $g" }
$summary += ''
$summary += '--- Files in Mods / Plugins / UserLibs / game root (dll) ---'
$summary += $fileList
$summary += ''
$summary += '--- Windows crash events (last 7 days) ---'
if ($events.Count -eq 0) { $summary += '(none found)' }
foreach ($e in $events) {
    $summary += "[$($e.TimeCreated)] $($e.ProviderName)"
    $summary += $e.Message
    $summary += ''
}
$summary += ''
$summary += '--- Collected files ---'
$summary += $collected
Set-Content -LiteralPath (Join-Path $work 'summary.txt') -Value $summary -Encoding UTF8

# ---------------------------------------------------------------- privacy
# Replace the Windows user name in all text files (paths like C:\Users\<name>).
if ($env:USERNAME) {
    $pattern = [regex]::Escape($env:USERNAME)
    Get-ChildItem -LiteralPath $work -File -Recurse | Where-Object { $_.Extension -in '.log', '.txt', '.cfg', '.json', '.xml' } | ForEach-Object {
        $t = [IO.File]::ReadAllText($_.FullName)
        $t2 = [regex]::Replace($t, "(?i)(\\Users\\|/Users/|/home/)$pattern", '${1}<user>')
        if ($t2 -ne $t) { [IO.File]::WriteAllText($_.FullName, $t2) }
    }
}

# ---------------------------------------------------------------- zip
$desktop = [Environment]::GetFolderPath('Desktop')
if (-not $desktop -or -not (Test-Path -LiteralPath $desktop)) { $desktop = $env:USERPROFILE }
$zip = Join-Path $desktop "ScheduleOneNavigator-CrashInfo-$stamp.zip"
try {
    Compress-Archive -Path (Join-Path $work '*') -DestinationPath $zip -Force -ErrorAction Stop
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
} catch {
    Say "Could not create the zip ($($_.Exception.Message)). The files are in: $work" 'Red'
    $zip = $null
}

Say ''
if (-not ($collected -contains 'MelonLoader\Latest.log')) {
    Say 'WARNING: no MelonLoader log found. Is MelonLoader installed and was the game started with it?' 'Yellow'
}
if ($zip) {
    Say 'Done! Your crash info is here:' 'Green'
    Say "  $zip" 'White'
    Say ''
    Say 'Please attach this zip to your bug report on Nexus (or GitHub).' 'Green'
    Say 'Your Windows user name has been removed from the logs.' 'Gray'
    try { Start-Process explorer.exe -ArgumentList "/select,`"$zip`"" } catch { }
}
