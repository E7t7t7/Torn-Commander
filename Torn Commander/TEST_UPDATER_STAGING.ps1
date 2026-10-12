# Windows release gate: an actual update must work with old dist locked,
# and install into the new versioned sibling folder without touching old EXE.
$ErrorActionPreference = 'Stop'
$work = Join-Path $env:RUNNER_TEMP ('Commander Versioned Update Test ' + [guid]::NewGuid().ToString('N'))
$installed = Join-Path $work 'Torn_Commander_v0.6.15\Torn Commander'
$package = Join-Path $work 'Update ZIP Source\Torn Commander'
$newInstall = Join-Path $work 'Torn_Commander_v0.6.16\Torn Commander'
New-Item -ItemType Directory -Path (Join-Path $installed 'dist') -Force | Out-Null
New-Item -ItemType Directory -Path $package -Force | Out-Null
Set-Content -LiteralPath (Join-Path $installed 'TornCommander.csproj') -Value '<Project />'
Set-Content -LiteralPath (Join-Path $installed 'VERSION.txt') -Value '0.6.15'
Set-Content -LiteralPath (Join-Path $installed 'old-marker.txt') -Value 'keep'
Copy-Item -LiteralPath "$env:SystemRoot\System32\notepad.exe" -Destination (Join-Path $installed 'dist\TornCommander.exe')
Set-Content -LiteralPath (Join-Path $installed 'dist\VERSION.txt') -Value '0.6.15'
foreach ($name in @('TornCommander.csproj','REBUILD_AND_LAUNCH.bat','CommanderUpdater.cs',
                    'APPLY_COMMANDER_UPDATE.ps1','CREATE_DESKTOP_SHORTCUT.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $package -Force
}
Set-Content -LiteralPath (Join-Path $package 'VERSION.txt') -Value '0.6.16'
$publisher = @'
@echo off
if not exist "%~dp0dist" mkdir "%~dp0dist"
copy /y "%SystemRoot%\System32\notepad.exe" "%~dp0dist\TornCommander.exe" >nul
copy /y "%~dp0VERSION.txt" "%~dp0dist\VERSION.txt" >nul
exit /b 0
'@
Set-Content -LiteralPath (Join-Path $package 'PUBLISH_EXE.bat') -Value $publisher -Encoding Ascii
$zip = Join-Path $work 'Torn_Commander_v0.6.16.zip'
Compress-Archive -Path $package -DestinationPath $zip -Force
foreach ($script in @('APPLY_COMMANDER_UPDATE.ps1','CREATE_DESKTOP_SHORTCUT.ps1')) {
    $tokens = $null; $errors = $null
    [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $script),
        [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors.Count -gt 0) { throw "$script has syntax errors: $($errors[0].Message)" }
}

# Deliberately leave a process in old dist to reproduce the original file lock.
$lockInfo = [Diagnostics.ProcessStartInfo]::new('cmd.exe')
$lockInfo.UseShellExecute = $false
$lockInfo.CreateNoWindow = $true
$lockInfo.WorkingDirectory = Join-Path $installed 'dist'
[void]$lockInfo.ArgumentList.Add('/c')
[void]$lockInfo.ArgumentList.Add('ping -n 25 127.0.0.1 >nul')
$holder = [Diagnostics.Process]::Start($lockInfo)
try {
    Start-Sleep -Milliseconds 250
    if ($holder.HasExited) { throw 'Lock fixture did not start.' }
    $psi = [Diagnostics.ProcessStartInfo]::new('powershell.exe')
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardInput = $true
    $psi.WorkingDirectory = Join-Path $installed 'dist'
    foreach ($arg in @('-NoProfile','-ExecutionPolicy','Bypass','-File',
        (Join-Path $PSScriptRoot 'APPLY_COMMANDER_UPDATE.ps1'),
        '-ZipPath',$zip,'-ProjectRoot',$installed,'-WaitPid','2147483647','-NoLaunch')) {
        [void]$psi.ArgumentList.Add($arg)
    }
    $proc = [Diagnostics.Process]::Start($psi)
    $proc.StandardInput.Close()
    $output = $proc.StandardOutput.ReadToEnd()
    $errorOutput = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()
    Write-Host $output
    if ($proc.ExitCode -ne 0) {
        throw "New updater failed: $errorOutput" + [Environment]::NewLine + $output
    }
    foreach ($required in @('TornCommander.csproj','VERSION.txt','dist\TornCommander.exe',
        'dist\VERSION.txt','CREATE_DESKTOP_SHORTCUT.ps1')) {
        if (!(Test-Path -LiteralPath (Join-Path $newInstall $required) -PathType Leaf)) {
            throw "New version installation missing: $required"
        }
    }
    if ((Get-Content -LiteralPath (Join-Path $newInstall 'VERSION.txt') -Raw).Trim() -ne '0.6.16') {
        throw 'New version folder has wrong source version.'
    }
    if ((Get-Content -LiteralPath (Join-Path $newInstall 'dist\VERSION.txt') -Raw).Trim() -ne '0.6.16') {
        throw 'New version folder has wrong binary version.'
    }
    if ((Get-Content -LiteralPath (Join-Path $installed 'dist\VERSION.txt') -Raw).Trim() -ne '0.6.15') {
        throw 'Old locked dist was modified.'
    }
    if ((Get-Content -LiteralPath (Join-Path $installed 'VERSION.txt') -Raw).Trim() -ne '0.6.15') {
        throw 'Old installation source was modified.'
    }
    if (!(Test-Path -LiteralPath (Join-Path $installed 'old-marker.txt'))) {
        throw 'Old local source marker disappeared.'
    }
    if ($holder.HasExited) { throw 'Lock fixture exited before validation.' }
    Write-Host 'VERSIONED UPDATE PASSED: new sibling folder, old locked dist and source untouched.'
}
finally {
    if ($holder -and -not $holder.HasExited) { $holder.Kill() }
    if ($holder) { $holder.Dispose() }
}
$global:LASTEXITCODE = 0
