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
Set-Content -LiteralPath (Join-Path $package 'VERSION.txt') -Value '0.6.10'
$publisher = @'
@echo off
if not exist "%~dp0dist" mkdir "%~dp0dist"
copy /y "%SystemRoot%\System32\notepad.exe" "%~dp0dist\TornCommander.exe" >nul
copy /y "%~dp0VERSION.txt" "%~dp0dist\VERSION.txt" >nul
exit /b 0
'@
Set-Content -LiteralPath (Join-Path $package 'PUBLISH_EXE.bat') -Value $publisher -Encoding Ascii
$zip = Join-Path $work 'Torn_Commander_v0.6.10.zip'
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
$proc = [System.Diagnostics.Process]::Start($psi)
$proc.StandardInput.Close()
$output = $proc.StandardOutput.ReadToEnd()
$failure = $proc.StandardError.ReadToEnd()
$proc.WaitForExit()
Write-Host $output
if ($proc.ExitCode -ne 0) { throw "Staging updater failed ($($proc.ExitCode)): $failure`n$output" }
if ((Get-Content (Join-Path $installed 'VERSION.txt') -Raw).Trim() -ne '0.6.10') { throw 'Updated source marker incorrect.' }
if ((Get-Content (Join-Path $installed 'dist\VERSION.txt') -Raw).Trim() -ne '0.6.10') { throw 'Updated executable marker incorrect.' }
if (!(Test-Path (Join-Path $installed 'old-marker.txt'))) { throw 'Old source files unexpectedly removed.' }
if (@(Get-ChildItem -LiteralPath $installed -Filter 'dist-prior-*' -Directory).Count -lt 1) { throw 'Previous binary backup not retained.' }
Write-Host 'TRANSACTIONAL UPDATE SMOKE TEST PASSED: spaced paths, staging build, dist swap, old-dist retention.'
$global:LASTEXITCODE = 0
