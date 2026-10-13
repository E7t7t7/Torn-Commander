# All paths, credentials roots and shortcuts are isolated synthetic fixtures.
$ErrorActionPreference='Stop'
$work=Join-Path $env:RUNNER_TEMP ('Commander Installation Test '+[guid]::NewGuid().ToString('N'))
$base=Join-Path $work 'Installations'
$data=Join-Path $work 'Data'
$desktop=Join-Path $work 'Desktop'
$pins=Join-Path $work 'Pins'
New-Item -ItemType Directory -Path $base,$data,$desktop,$pins -Force | Out-Null
foreach($version in @('0.6.16','0.6.17','0.6.18','0.6.19')){
 $project=Join-Path $base ('Torn_Commander_v'+$version+'\Torn Commander')
 New-Item -ItemType Directory -Path (Join-Path $project 'dist'),(Join-Path $project 'Assets') -Force | Out-Null
 Set-Content -LiteralPath (Join-Path $project 'VERSION.txt') -Value $version
 Set-Content -LiteralPath (Join-Path $project 'dist\VERSION.txt') -Value $version
 Set-Content -LiteralPath (Join-Path $project 'TornCommander.csproj') -Value ('<Project><PropertyGroup><Version>'+$version+'</Version></PropertyGroup></Project>')
 foreach($file in @('Program.cs','MainForm.cs','PUBLISH_EXE.bat','REBUILD_AND_LAUNCH.bat','CommanderUpdater.cs','APPLY_COMMANDER_UPDATE.ps1','CREATE_DESKTOP_SHORTCUT.ps1')){Set-Content -LiteralPath (Join-Path $project $file) -Value 'SYNTHETIC TEST SOURCE'}
 Copy-Item -LiteralPath "$env:SystemRoot\System32\notepad.exe" -Destination (Join-Path $project 'dist\TornCommander.exe')
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Assets\TornCommander_T_Icon.ico') -Destination (Join-Path $project 'Assets')
}
$active=Join-Path $base 'Torn_Commander_v0.6.19\Torn Commander'
$wsh=New-Object -ComObject WScript.Shell
$desktopLink=Join-Path $desktop 'Torn Commander.lnk'
$pin=Join-Path $pins 'Commander pinned.lnk'
$other=Join-Path $pins 'Unrelated.lnk'
foreach($pair in @(@($desktopLink,'0.6.17'),@($pin,'0.6.16'))){
 $sc=$wsh.CreateShortcut($pair[0]);$sc.TargetPath=Join-Path $base ('Torn_Commander_v'+$pair[1]+'\Torn Commander\dist\TornCommander.exe');$sc.Save()
}
$sc=$wsh.CreateShortcut($other);$sc.TargetPath="$env:SystemRoot\System32\notepad.exe";$sc.Save()
$otherHash=(Get-FileHash -LiteralPath $other).Hash
function SyncInstall([string]$project,[switch]$compact){
 $arguments=@('-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $PSScriptRoot 'SYNC_COMMANDER_INSTALL.ps1'),'-ProjectRoot',$project,'-DataRoot',$data,'-DesktopPath',$desktop,'-PinnedPath',$pins)
 if($compact){$arguments+='-CompactAllPrevious'}
 & powershell.exe @arguments
 if($LASTEXITCODE -ne 0){throw 'Install maintenance failed.'}
}
SyncInstall $active
$launcher=Join-Path $data 'Launcher\TornCommanderLauncher.exe'
if(!(Test-Path -LiteralPath $launcher)){throw 'Stable launcher missing.'}
foreach($link in @($desktopLink,$pin)){if($wsh.CreateShortcut($link).TargetPath -ne $launcher){throw 'Shortcut not migrated to stable launcher.'}}
if((Get-FileHash -LiteralPath $other).Hash -ne $otherHash){throw 'Unrelated shortcut was modified.'}
foreach($version in @('0.6.16','0.6.17','0.6.18')){
 foreach($folder in @($base,(Join-Path $data 'VersionBackups'))){if(!(Test-Path -LiteralPath (Join-Path $folder ('Torn_Commander_v'+$version+'.zip')))){throw 'Rollback archive missing.'}}
}
if(Test-Path -LiteralPath (Join-Path $base 'Torn_Commander_v0.6.16')){throw 'Old v16 folder not compacted.'}
if(!(Test-Path -LiteralPath (Join-Path $base 'Torn_Commander_v0.6.18'))){throw 'Immediate recovery folder was removed.'}
$launcherHash=(Get-FileHash -LiteralPath $launcher).Hash
SyncInstall (Join-Path $base 'Torn_Commander_v0.6.18\Torn Commander')
if((Get-Content -LiteralPath (Join-Path $data 'Launcher\ActiveCommander.txt') -Raw).Trim() -notlike '*v0.6.18*'){throw 'Rollback active pointer incorrect.'}
if((Get-FileHash -LiteralPath $launcher).Hash -ne $launcherHash){throw 'Stable launcher was recreated on rollback.'}
SyncInstall $active -compact
if(Test-Path -LiteralPath (Join-Path $base 'Torn_Commander_v0.6.18')){throw 'Explicit complete compaction failed.'}
if(!(Test-Path -LiteralPath (Join-Path $active 'dist\TornCommander.exe'))){throw 'Active executable lost.'}
Write-Output 'INSTALL MAINTENANCE PASSED: desktop/pin migration, unrelated shortcut preservation, verified ZIPs, compaction, rollback pointer.'
