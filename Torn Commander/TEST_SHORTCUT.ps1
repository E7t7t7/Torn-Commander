# Validate the T shortcut script against an isolated fake Commander installation.
# Never writes to the GitHub runner Desktop or the real user's desktop.
$ErrorActionPreference = 'Stop'
$work = Join-Path $env:RUNNER_TEMP ('Commander Shortcut Test ' + [guid]::NewGuid().ToString('N'))
$root = Join-Path $work 'Torn_Commander_v0.6.16\Torn Commander'
$dist = Join-Path $root 'dist'
$assets = Join-Path $root 'Assets'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
New-Item -ItemType Directory -Path $assets -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CREATE_DESKTOP_SHORTCUT.ps1') -Destination $root
Copy-Item -LiteralPath "$env:SystemRoot\System32\notepad.exe" -Destination (Join-Path $dist 'TornCommander.exe')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Assets\TornCommander_T_Icon.ico') -Destination $assets
$target = Join-Path $dist 'TornCommander.exe'
$shortcutPath = Join-Path $work 'Torn Commander Test.lnk'
$script = Join-Path $root 'CREATE_DESKTOP_SHORTCUT.ps1'
$tokens = $null; $errors = $null
[Management.Automation.Language.Parser]::ParseFile($script, [ref]$tokens, [ref]$errors) | Out-Null
if ($errors.Count -gt 0) { throw "Shortcut syntax error: $($errors[0].Message)" }
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script -Force -DestinationPath $shortcutPath
if ($LASTEXITCODE -ne 0) { throw 'Shortcut creator returned a failure.' }
$wsh = New-Object -ComObject WScript.Shell
$sc = $wsh.CreateShortcut($shortcutPath)
if (-not [string]::Equals($sc.TargetPath,$target,[StringComparison]::OrdinalIgnoreCase)) {
    throw "Shortcut target wrong: $($sc.TargetPath) expected $target"
}
if ($sc.IconLocation -notlike '*TornCommander_T_Icon.ico*') {
    throw 'Shortcut did not use the T icon.'
}
# Also verify resolving an active dist-v pointer from a prior in-place upgrade.
$active = 'dist-v0.6.16-abcdef12'
$activeDir = Join-Path $root $active
New-Item -ItemType Directory -Path $activeDir -Force | Out-Null
Copy-Item -LiteralPath $target -Destination (Join-Path $activeDir 'TornCommander.exe')
Set-Content -LiteralPath (Join-Path $root 'ACTIVE_DIST.txt') -Value $active
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script -Force -DestinationPath $shortcutPath
if ($LASTEXITCODE -ne 0) { throw 'Shortcut creator failed on active version pointer.' }
$sc = $wsh.CreateShortcut($shortcutPath)
if ($sc.TargetPath -notlike "*$active\TornCommander.exe") {
    throw "Active dist shortcut target wrong: $($sc.TargetPath)"
}
Write-Host 'SHORTCUT SMOKE TEST PASSED: T icon, paths with spaces, version pointer.'
$global:LASTEXITCODE = 0
