# Windows CI: versioned upgrade, saved rollback, folder cleanup and old-dist lock.
# All fixtures are fake Commander projects. Never reads actual player/account data.
$ErrorActionPreference='Stop'
$work=Join-Path $env:RUNNER_TEMP ('Commander Rollback Test '+[guid]::NewGuid().ToString('N'))
$installed=Join-Path $work 'Torn_Commander_v0.6.16\Torn Commander'
$next=Join-Path $work 'Torn_Commander_v0.6.17\Torn Commander'
$source=Join-Path $work 'Release\Torn Commander'
New-Item -ItemType Directory -Path (Join-Path $installed 'dist') -Force | Out-Null
New-Item -ItemType Directory -Path $source -Force | Out-Null
Set-Content -LiteralPath (Join-Path $installed 'VERSION.txt') -Value '0.6.16'
Set-Content -LiteralPath (Join-Path $installed 'TornCommander.csproj') -Value '<Project />'
Set-Content -LiteralPath (Join-Path $installed 'old-marker.txt') -Value 'keep'
Copy-Item "$env:SystemRoot\System32\notepad.exe" (Join-Path $installed 'dist\TornCommander.exe')
Set-Content -LiteralPath (Join-Path $installed 'dist\VERSION.txt') -Value '0.6.16'
foreach($scriptName in @('APPLY_COMMANDER_UPDATE.ps1','CREATE_DESKTOP_SHORTCUT.ps1','CommanderUpdater.cs','REBUILD_AND_LAUNCH.bat')){
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot $scriptName) -Destination $installed -Force
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot $scriptName) -Destination $source -Force
}
Set-Content -LiteralPath (Join-Path $source 'VERSION.txt') -Value '0.6.17'
Set-Content -LiteralPath (Join-Path $source 'TornCommander.csproj') -Value '<Project />'
New-Item -ItemType Directory -Path (Join-Path $source 'Assets') -Force | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'Assets\TornCommander_T_Icon.ico') (Join-Path $source 'Assets')
$publisher=@'
@echo off
if not exist "%~dp0dist" mkdir "%~dp0dist"
copy /y "%SystemRoot%\System32\notepad.exe" "%~dp0dist\TornCommander.exe" >nul
copy /y "%~dp0VERSION.txt" "%~dp0dist\VERSION.txt" >nul
exit /b 0
'@
Set-Content -LiteralPath (Join-Path $source 'PUBLISH_EXE.bat') -Value $publisher -Encoding Ascii
Set-Content -LiteralPath (Join-Path $installed 'PUBLISH_EXE.bat') -Value $publisher -Encoding Ascii
$zip=Join-Path $work 'Torn_Commander_v0.6.17.zip'
Compress-Archive -Path $source -DestinationPath $zip -Force

foreach($name in @('APPLY_COMMANDER_UPDATE.ps1','CREATE_DESKTOP_SHORTCUT.ps1')){
 $tokens=$null;$errors=$null
 [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $name),[ref]$tokens,[ref]$errors)|Out-Null
 if($errors.Count -gt 0){throw "$name parse failure: $($errors[0].Message)"}
}

function Invoke-Updater([string]$archive,[string]$origin){
 $psi=[Diagnostics.ProcessStartInfo]::new('powershell.exe')
 $psi.UseShellExecute=$false
 $psi.RedirectStandardOutput=$true
 $psi.RedirectStandardError=$true
 $psi.RedirectStandardInput=$true
 $psi.WorkingDirectory=Join-Path $origin 'dist'
 foreach($arg in @('-NoProfile','-ExecutionPolicy','Bypass','-File',
     (Join-Path $PSScriptRoot 'APPLY_COMMANDER_UPDATE.ps1'),
     '-ZipPath',$archive,'-ProjectRoot',$origin,'-WaitPid','2147483647','-NoLaunch')){
  [void]$psi.ArgumentList.Add($arg)
 }
 $proc=[Diagnostics.Process]::Start($psi)
 $proc.StandardInput.Close()
 $stdout=$proc.StandardOutput.ReadToEnd()
 $stderr=$proc.StandardError.ReadToEnd()
 $proc.WaitForExit()
 Write-Host $stdout
 if($proc.ExitCode -ne 0){throw "Updater exit $($proc.ExitCode): $stderr $stdout"}
}

# Hold old installation open to simulate a real OneDrive/WebView2 directory lock.
$lock=[Diagnostics.ProcessStartInfo]::new('cmd.exe')
$lock.UseShellExecute=$false
$lock.CreateNoWindow=$true
$lock.WorkingDirectory=Join-Path $installed 'dist'
[void]$lock.ArgumentList.Add('/c')
[void]$lock.ArgumentList.Add('ping -n 50 127.0.0.1 >nul')
$holder=[Diagnostics.Process]::Start($lock)
try{
 Start-Sleep -Milliseconds 150
 if($holder.HasExited){throw 'Lock fixture exited too soon.'}
 Invoke-Updater $zip $installed
 if(!(Test-Path (Join-Path $next 'dist\TornCommander.exe'))){throw 'New executable not installed.'}
 if((Get-Content (Join-Path $next 'VERSION.txt') -Raw).Trim() -ne '0.6.17'){throw 'New folder VERSION mismatch.'}
 if((Get-Content (Join-Path $installed 'VERSION.txt') -Raw).Trim() -ne '0.6.16'){throw 'Old locked installation altered.'}
 if(!(Test-Path (Join-Path $installed 'old-marker.txt'))){throw 'Old local file missing.'}
 $vault=Join-Path $env:LOCALAPPDATA 'TornCommander\VersionBackups'
 $rollback=Join-Path $vault 'Torn_Commander_v0.6.16.zip'
 if(!(Test-Path $rollback)){throw 'Old version was not saved for rollback.'}
 # Revert from 17 -> 16 while old v16 folder still exists/locked.
 # The updater must verify and activate that exact folder, never overwrite it.
 Invoke-Updater $rollback $next
 if((Get-Content (Join-Path $installed 'VERSION.txt') -Raw).Trim() -ne '0.6.16'){throw 'Rollback did not activate old source.'}
 if(!(Test-Path (Join-Path $installed 'dist\TornCommander.exe'))){throw 'Rollback executable missing.'}
 $future=Join-Path $vault 'Torn_Commander_v0.6.17.zip'
 if(!(Test-Path $future)){throw 'Current release was not saved for later switching.'}
 if($holder.HasExited){throw 'The old directory lock was not maintained.'}
 Write-Host 'ROLLBACK/UPGRADE SMOKE TEST PASSED: v16 to v17 to v16; rollback ZIPs and folder naming checked.'
}finally{
 if($holder -and -not $holder.HasExited){$holder.Kill()}
 if($holder){$holder.Dispose()}
}
$global:LASTEXITCODE=0
