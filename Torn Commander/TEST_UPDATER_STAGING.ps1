# Windows CI smoke test. Deliberately lock the old EXE directory.
# No Torn user profiles, API keys, sessions or real Commander instances.
$ErrorActionPreference = 'Stop'
$work = Join-Path $env:RUNNER_TEMP ('Commander Updater Locked Dist Test ' + [guid]::NewGuid().ToString('N'))
$installed = Join-Path $work 'Installed Commander With Spaces\Torn Commander'
$package = Join-Path $work 'New Release\Torn Commander'
New-Item -ItemType Directory -Path (Join-Path $installed 'dist') -Force | Out-Null
New-Item -ItemType Directory -Path $package -Force | Out-Null
Set-Content -LiteralPath (Join-Path $installed 'TornCommander.csproj') -Value '<Project />'
Set-Content -LiteralPath (Join-Path $installed 'VERSION.txt') -Value '0.6.13'
Set-Content -LiteralPath (Join-Path $installed 'old-marker.txt') -Value 'keep'
Copy-Item -LiteralPath "$env:SystemRoot\System32\notepad.exe" -Destination (Join-Path $installed 'dist\TornCommander.exe')
Set-Content -LiteralPath (Join-Path $installed 'dist\VERSION.txt') -Value '0.6.13'

foreach ($item in @('TornCommander.csproj','REBUILD_AND_LAUNCH.bat','CommanderUpdater.cs','APPLY_COMMANDER_UPDATE.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $item) -Destination $package -Force
}
Set-Content -LiteralPath (Join-Path $package 'VERSION.txt') -Value '0.6.14'
$publisher = @'
@echo off
if not exist "%~dp0dist" mkdir "%~dp0dist"
copy /y "%SystemRoot%\System32\notepad.exe" "%~dp0dist\TornCommander.exe" >nul
copy /y "%~dp0VERSION.txt" "%~dp0dist\VERSION.txt" >nul
exit /b 0
'@
Set-Content -LiteralPath (Join-Path $package 'PUBLISH_EXE.bat') -Value $publisher -Encoding Ascii
$zip = Join-Path $work 'Torn_Commander_v0.6.14.zip'
Compress-Archive -Path $package -DestinationPath $zip -Force

$tokens = $null; $errors = $null
[System.Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $PSScriptRoot 'APPLY_COMMANDER_UPDATE.ps1'),
    [ref]$tokens,[ref]$errors) | Out-Null
if ($errors.Count -gt 0) { throw "Updater PowerShell parse error: $($errors[0].Message)" }

# Keep a process with its cwd INSIDE old dist for the entire update.
$lockInfo = [System.Diagnostics.ProcessStartInfo]::new('cmd.exe')
$lockInfo.UseShellExecute = $false
$lockInfo.CreateNoWindow = $true
$lockInfo.WorkingDirectory = Join-Path $installed 'dist'
[void]$lockInfo.ArgumentList.Add('/c')
[void]$lockInfo.ArgumentList.Add('ping -n 20 127.0.0.1 >nul')
$holder = [System.Diagnostics.Process]::Start($lockInfo)

try {
    Start-Sleep -Milliseconds 200
    if ($holder.HasExited) { throw 'Lock fixture exited before updater started.' }
    $psi = [System.Diagnostics.ProcessStartInfo]::new('powershell.exe')
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardInput = $true
    # Reproduce the original fault: helper inherits cwd inside locked dist.
    $psi.WorkingDirectory = Join-Path $installed 'dist'
    foreach ($arg in @('-NoProfile','-ExecutionPolicy','Bypass','-File',
        (Join-Path $PSScriptRoot 'APPLY_COMMANDER_UPDATE.ps1'),
        '-ZipPath',$zip,'-ProjectRoot',$installed,'-WaitPid','2147483647','-NoLaunch')) {
        [void]$psi.ArgumentList.Add($arg)
    }
    $proc = [System.Diagnostics.Process]::Start($psi)
    $proc.StandardInput.Close()
    $output = $proc.StandardOutput.ReadToEnd()
    $failure = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()
    Write-Host $output
    if ($proc.ExitCode -ne 0) {
        throw "Updater failed ($($proc.ExitCode)): $failure" + [Environment]::NewLine + $output
    }
    if ((Get-Content (Join-Path $installed 'VERSION.txt') -Raw).Trim() -ne '0.6.14') {
        throw 'Updated source VERSION.txt incorrect.'
    }
    $activeMarker = Join-Path $installed 'ACTIVE_DIST.txt'
    if (!(Test-Path -LiteralPath $activeMarker -PathType Leaf)) {
        throw 'Active-version pointer not written.'
    }
    $slot = (Get-Content -LiteralPath $activeMarker -Raw).Trim()
    if ($slot -notmatch '^dist-v0\.6\.14-[a-fA-F0-9]{8}$') {
        throw "Active version slot invalid: $slot"
    }
    $activeExe = Join-Path $installed (Join-Path $slot 'TornCommander.exe')
    $activeVersion = Join-Path $installed (Join-Path $slot 'VERSION.txt')
    if (!(Test-Path -LiteralPath $activeExe -PathType Leaf)) {
        throw 'New executable was not installed.'
    }
    if ((Get-Content -LiteralPath $activeVersion -Raw).Trim() -ne '0.6.14') {
        throw 'New executable version marker incorrect.'
    }
    if ((Get-Content -LiteralPath (Join-Path $installed 'dist\VERSION.txt') -Raw).Trim() -ne '0.6.13') {
        throw 'Old locked dist version was modified.'
    }
    if (!(Test-Path -LiteralPath (Join-Path $installed 'dist\TornCommander.exe'))) {
        throw 'Old locked EXE disappeared.'
    }
    if (!(Test-Path -LiteralPath (Join-Path $installed 'old-marker.txt'))) {
        throw 'Old source data was removed.'
    }
    if ($holder.HasExited) { throw 'Lock fixture exited before update validation.' }
    Write-Host 'SIDE-BY-SIDE UPDATER SMOKE TEST PASSED: old dist locked, new slot active, old EXE unchanged.'
} finally {
    if ($holder -and -not $holder.HasExited) { $holder.Kill() }
    if ($holder) { $holder.Dispose() }
}
$global:LASTEXITCODE = 0
