param([switch]$Force)
$ErrorActionPreference = 'Stop'

try {
    $project = Split-Path -Parent $MyInvocation.MyCommand.Path
    $exe = [System.IO.Path]::GetFullPath((Join-Path $project 'dist\TornCommander.exe'))
    $icon = [System.IO.Path]::GetFullPath((Join-Path $project 'Assets\TornCommander_T_Icon.ico'))
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        throw "TornCommander.exe is not built yet. Run REBUILD_AND_LAUNCH.bat once, then run CREATE_DESKTOP_SHORTCUT.bat again. Expected: $exe"
    }
    if (-not (Test-Path -LiteralPath $icon -PathType Leaf)) {
        throw "The custom T icon is missing from: $icon"
    }

    # Desktop resolves OneDrive-redirected desktops as well as ordinary local desktops.
    $desktop = [Environment]::GetFolderPath('Desktop')
    if ([string]::IsNullOrWhiteSpace($desktop) -or -not (Test-Path -LiteralPath $desktop -PathType Container)) {
        throw 'Could not locate your Windows Desktop folder.'
    }

    $shortcutPath = Join-Path $desktop 'Torn Commander.lnk'
    $wsh = New-Object -ComObject WScript.Shell
    if (Test-Path -LiteralPath $shortcutPath -PathType Leaf) {
        $previous = $wsh.CreateShortcut($shortcutPath)
        $existingTarget = $previous.TargetPath
        if ((-not $Force) -and $existingTarget -and
            (-not [string]::Equals($existingTarget,$exe,[StringComparison]::OrdinalIgnoreCase))) {
            throw "An existing Torn Commander shortcut points to another folder: $existingTarget`nRun CREATE_DESKTOP_SHORTCUT.bat and confirm replacement when prompted."
        }
    }

    $shortcut = $wsh.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = Split-Path -Parent $exe
    # Use the actual ICO file: the shortcut is branded even while Windows caches EXE thumbnails.
    $shortcut.IconLocation = $icon + ',0'
    $shortcut.Description = 'Torn Commander v0.6.3 - Launch Windows desktop app (no command window)'
    $shortcut.WindowStyle = 1
    $shortcut.Save()

    $check = $wsh.CreateShortcut($shortcutPath)
    if (-not [string]::Equals($check.TargetPath,$exe,[StringComparison]::OrdinalIgnoreCase)) {
        throw 'Shortcut was saved, but its target did not verify.'
    }
    Write-Host ''
    Write-Host 'SUCCESS: T-branded Windows shortcut created.' -ForegroundColor Green
    Write-Host ('Shortcut: ' + $shortcutPath)
    Write-Host ('Target:   ' + $check.TargetPath)
    Write-Host ('Icon:     ' + $icon)
    Write-Host ''
    Write-Host 'Open the new T icon shortcut, not Torn Commander.bat.'
    Write-Host 'If you pinned the old CMD/BAT shortcut to the taskbar, unpin it and pin the new shortcut instead.'
    exit 0
}
catch {
    Write-Host ''
    Write-Host ('ERROR: ' + $_.Exception.Message) -ForegroundColor Red
    exit 1
}
