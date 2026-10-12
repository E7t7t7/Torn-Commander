param(
    [switch]$Force,
    [string]$DestinationPath = ''
)
$ErrorActionPreference = 'Stop'

try {
    $project = Split-Path -Parent $MyInvocation.MyCommand.Path
    $activeName = 'dist'
    $activePointer = Join-Path $project 'ACTIVE_DIST.txt'
    if (Test-Path -LiteralPath $activePointer -PathType Leaf) {
        $candidate = (Get-Content -LiteralPath $activePointer -Raw).Trim()
        if ($candidate -notmatch '^dist-v\d+\.\d+\.\d+-[a-fA-F0-9]{8}$') {
            throw "Active version pointer is invalid: $candidate"
        }
        $activeName = $candidate
    }
    $exe = [IO.Path]::GetFullPath((Join-Path (Join-Path $project $activeName) 'TornCommander.exe'))
    $icon = [IO.Path]::GetFullPath((Join-Path $project 'Assets\TornCommander_T_Icon.ico'))
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        throw "TornCommander.exe was not found: $exe"
    }
    if (-not (Test-Path -LiteralPath $icon -PathType Leaf)) {
        throw "The custom T icon was not found: $icon"
    }

    if ([string]::IsNullOrWhiteSpace($DestinationPath)) {
        $desktop = [Environment]::GetFolderPath('Desktop')
        if ([string]::IsNullOrWhiteSpace($desktop) -or -not (Test-Path -LiteralPath $desktop -PathType Container)) {
            throw 'Windows Desktop folder is unavailable.'
        }
        $shortcutPath = Join-Path $desktop 'Torn Commander.lnk'
    } else {
        $shortcutPath = [IO.Path]::GetFullPath($DestinationPath)
        if ([IO.Path]::GetExtension($shortcutPath) -ne '.lnk') {
            throw 'DestinationPath must end with .lnk'
        }
        $parent = Split-Path -Parent $shortcutPath
        if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
            throw "Shortcut destination directory does not exist: $parent"
        }
    }

    $wsh = New-Object -ComObject WScript.Shell
    if (Test-Path -LiteralPath $shortcutPath -PathType Leaf) {
        $previous = $wsh.CreateShortcut($shortcutPath)
        $existingTarget = $previous.TargetPath
        if (-not $Force -and $existingTarget -and
            -not [string]::Equals($existingTarget, $exe, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Existing shortcut targets a different EXE: $existingTarget. Run CREATE_DESKTOP_SHORTCUT.bat to replace it."
        }
    }

    $shortcut = $wsh.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = Split-Path -Parent $exe
    $shortcut.IconLocation = $icon + ',0'
    $shortcut.Description = 'Torn Commander - launch the current desktop application'
    $shortcut.WindowStyle = 1
    $shortcut.Save()

    $verified = $wsh.CreateShortcut($shortcutPath)
    if (-not [string]::Equals($verified.TargetPath, $exe, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The shortcut was created but its target did not verify.'
    }
    Write-Host 'SUCCESS: T-branded Torn Commander shortcut created.' -ForegroundColor Green
    Write-Host ('Shortcut: ' + $shortcutPath)
    Write-Host ('Target: ' + $verified.TargetPath)
    Write-Host ('Icon: ' + $icon)
    exit 0
}
catch {
    Write-Host ('Shortcut creation failed: ' + $_.Exception.Message) -ForegroundColor Red
    exit 1
}
