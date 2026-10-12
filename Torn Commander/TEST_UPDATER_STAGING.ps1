# Windows-only CI test for transactional updater. No user profiles, API keys or
# running Commander processes are involved; this builds a tiny disposable fixture.
$ErrorActionPreference = 'Stop'
$work = Join-Path $env:RUNNER_TEMP ('Commander Updater Test ' + [guid]::NewGuid().ToString('N'))
$installed = Join-Path $work 'Installed Commander With Spaces\Torn Commander'
$package = Join-Path $work 'New Release\Torn Commander'
New-Item -ItemType Directory -Path (Join-Path $installed 'dist') -Force | Out-Null
New-Item -ItemType Directory -Path $package -Force | Out-Null
Set-Content -LiteralPath (Join-Path $installed 'TornCommander.csproj') -Value '<Project />'
Set-Content -LiteralPath (Join-Path $installed 'VERSION.txt') -Value '0.6.9'
Set-Content -LiteralPath (Join-Path $installed 'old-marker.txt') -Value 'keep'
Copy-Item -LiteralPath "$env:SystemRoot\System32\notepad.exe" -Destination (Join-Path $installed 'dist\TornCommander.exe')
Set-Content -LiteralPath (Join-Path $installed 'dist\VERSION.txt') -Value '0.6.9'
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
[System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'APPLY_COMMANDER_UPDATE.ps1'),[ref]$tokens,[ref]$errors) | Out-Null
if ($errors.Count -gt 0) { throw "PowerShell parse error: $($errors[0].Message)" }
$psi = [System.Diagnostics.ProcessStartInfo]::new('powershell.exe')
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.RedirectStandardInput = $true
foreach ($arg in @('-NoProfile','-ExecutionPolicy','Bypass','-File',
    (Join-Path $PSScriptRoot 'APPLY_COMMANDER_UPDATE.ps1'),
    '-ZipPath',$zip,'-ProjectRoot',$installed,'-WaitPid','2147483647','-NoLaunch')) {
    [void]$psi.ArgumentList.Add($arg)
}
# Simulate an unrelated process holding the old dist as its working
# directory; the updater must work without any old dist directory swap.
$lockInfo = [System.Diagnostics.ProcessStartInfo]::new('cmd.exe')
$lockInfo.UseShellExecute = $false
$lockInfo.CreateNoWindow = $true
$lockInfo.WorkingDirectory = Join-Path $installed 'dist'
$lockInfo.ArgumentList.Add('/c')
$lockInfo.ArgumentList.Add('ping -n 14 127.0.0.1 >nul')
$holder = [System.Diagnostics.Process]::Start($lockInfo)
try {
    $psi.WorkingDirectory = (Join-Path $installed 'dist')
    $proc = [System.Diagnostics.Process]::Start($psi)
$proc.StandardInput.Close()
$output = $proc.StandardOutput.ReadToEnd()
$failure = $proc.StandardError.ReadToEnd()
$proc.WaitForExit()
Write-Host $output
if ($proc.ExitCode -ne 0) { throw "Staging updater failed ($($proc.ExitCode)): $failure`n$output" }
if ((Get-Content (Join-Path $installed 'VERSION.txt') -Raw).Trim() -ne '0.6.14') { throw 'Updated source marker incorrect.' }
$activeMarker = Join-Path $installed 'ACTIVE_DIST.txt'
if (!(Test-Path -LiteralPath $activeMarker)) { throw 'Active-version pointer missing.' }
$slot = (Get-Content -LiteralPath $activeMarker -Raw).Trim()
if ($slot -notmatch '^dist-v0\.6\.14-[a-fA-F0-9]{8}
if (!(Test-Path (Join-Path $installed 'old-marker.txt'))) { throw 'Old source files unexpectedly removed.' }
if (!(Test-Path -LiteralPath (Join-Path $installed 'dist\TornCommander.exe'))) { throw 'Old dist EXE was removed.' }
    Write-Host 'SIDE-BY-SIDE UPDATE SMOKE TEST PASSED: old dist locked, new slot installed, active pointer verified.'
} finally {
    if ($holder -and -not $holder.HasExited) { $holder.Kill() }
    if ($holder) { $holder.Dispose() }
}
$global:LASTEXITCODE = 0
) { throw "Invalid active slot: $slot" }
if ((Get-Content (Join-Path $installed (Join-Path $slot 'VERSION.txt')) -Raw).Trim() -ne '0.6.14') {
    throw 'New active slot has wrong VERSION.txt.'
}
if ((Get-Content (Join-Path $installed 'dist\VERSION.txt') -Raw).Trim() -ne '0.6.9') {
    throw 'The locked old dist directory was modified.'
}
if (!(Test-Path (Join-Path $installed 'old-marker.txt'))) { throw 'Old source files unexpectedly removed.' }
if (@(Get-ChildItem -LiteralPath $installed -Filter 'dist-prior-*' -Directory).Count -lt 1) { throw 'Previous binary backup not retained.' }
Write-Host 'TRANSACTIONAL UPDATE SMOKE TEST PASSED: spaced paths, staging build, dist swap, old-dist retention.'
$global:LASTEXITCODE = 0
