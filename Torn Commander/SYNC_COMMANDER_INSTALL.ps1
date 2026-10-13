param([Parameter(Mandatory=$true)][string]$ProjectRoot,[string]$DataRoot='',[string]$DesktopPath='',[string]$PinnedPath='',[switch]$SkipCleanup,[switch]$CompactAllPrevious)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
$project=[IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\')
$outer=Split-Path -Parent $project
$base=[IO.Path]::GetFullPath((Split-Path -Parent $outer)).TrimEnd('\')
$version=(Get-Content -LiteralPath (Join-Path $project 'VERSION.txt') -Raw).Trim()
if($version -notmatch '^\d+\.\d+\.\d+$' -or (Split-Path -Leaf $outer) -ne ('Torn_Commander_v'+$version)){throw 'Versioned installation required.'}
$dist='dist'
$pointer=Join-Path $project 'ACTIVE_DIST.txt'
if(Test-Path -LiteralPath $pointer){$dist=(Get-Content -LiteralPath $pointer -Raw).Trim();if($dist -notmatch '^dist-v\d+\.\d+\.\d+-[a-fA-F0-9]{8}$'){throw 'Invalid executable pointer.'}}
$exe=Join-Path (Join-Path $project $dist) 'TornCommander.exe'
if(!(Test-Path -LiteralPath $exe -PathType Leaf)){throw 'Executable missing.'}
if((Get-Content -LiteralPath (Join-Path (Split-Path -Parent $exe) 'VERSION.txt') -Raw).Trim() -ne $version){throw 'Executable/source version mismatch.'}
if(!$DataRoot){$DataRoot=Join-Path $env:LOCALAPPDATA 'TornCommander'}
$DataRoot=[IO.Path]::GetFullPath($DataRoot)
$launcherFolder=Join-Path $DataRoot 'Launcher'
$vault=Join-Path $DataRoot 'VersionBackups'
New-Item -ItemType Directory -Path $launcherFolder,$vault -Force | Out-Null
$launcher=Join-Path $launcherFolder 'TornCommanderLauncher.exe'
if(!(Test-Path -LiteralPath $launcher -PathType Leaf)){
 $code=@'
using System;using System.IO;using System.Diagnostics;using System.Windows.Forms;
public static class CommanderLauncher {
 [STAThread] public static void Main(){
  try {
   string root=Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
   string path=File.ReadAllText(Path.Combine(root,"ActiveCommander.txt")).Trim();
   if(!Path.IsPathRooted(path)||!File.Exists(path)||!string.Equals(Path.GetFileName(path),"TornCommander.exe",StringComparison.OrdinalIgnoreCase))throw new Exception("The active Commander installation is unavailable. Open Commander directly to repair the launcher.");
   Process.Start(new ProcessStartInfo(path){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(path)});
  }catch(Exception e){MessageBox.Show(e.Message,"Torn Commander launcher",MessageBoxButtons.OK,MessageBoxIcon.Error);}
 }
}
'@
 $temp=Join-Path $launcherFolder ('Launcher-'+[guid]::NewGuid().ToString('N')+'.exe')
 try {Add-Type -TypeDefinition $code -Language CSharp -ReferencedAssemblies 'System.dll','System.Windows.Forms.dll' -OutputAssembly $temp -OutputType WindowsApplication;Move-Item -LiteralPath $temp -Destination $launcher}
 finally {if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp -Force}}
}
$active=Join-Path $launcherFolder 'ActiveCommander.txt'
$temp=Join-Path $launcherFolder ('active-'+[guid]::NewGuid().ToString('N')+'.tmp')
try{[IO.File]::WriteAllText($temp,$exe,[Text.UTF8Encoding]::new($false));if(Test-Path -LiteralPath $active){[IO.File]::Replace($temp,$active,($active+'.previous'))}else{[IO.File]::Move($temp,$active)}}
finally{if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp -Force}}
$icon=Join-Path $launcherFolder 'TornCommander_T_Icon.ico'
Copy-Item -LiteralPath (Join-Path $project 'Assets\TornCommander_T_Icon.ico') -Destination $icon -Force
if(!$DesktopPath){$DesktopPath=[Environment]::GetFolderPath('Desktop')}
if(!$PinnedPath){$PinnedPath=Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar'}
$wsh=New-Object -ComObject WScript.Shell
function OwnsShortcut([string]$target){
 if([string]::Equals($target,$launcher,[StringComparison]::OrdinalIgnoreCase)){return $true}
 if(!$target){return $false}
 $pattern='^'+[regex]::Escape($base+'\')+'Torn_Commander_v\d+\.\d+\.\d+\\Torn Commander\\dist(?:-v\d+\.\d+\.\d+-[a-fA-F0-9]{8})?\\TornCommander\.exe$'
 return [IO.Path]::GetFullPath($target) -match $pattern
}
$links=@()
if(Test-Path -LiteralPath $DesktopPath -PathType Container){$links+=Join-Path $DesktopPath 'Torn Commander.lnk'}
if(Test-Path -LiteralPath $PinnedPath -PathType Container){$links+=@(Get-ChildItem -LiteralPath $PinnedPath -Filter '*.lnk' -File | ForEach-Object {$_.FullName})}
$shortcutFailure=$false
foreach($link in $links){
 try {
  $sc=$wsh.CreateShortcut($link)
  if((Test-Path -LiteralPath $link) -and !(OwnsShortcut $sc.TargetPath)){continue}
  $sc.TargetPath=$launcher;$sc.WorkingDirectory=$launcherFolder;$sc.IconLocation=$icon+',0';$sc.Description='Torn Commander - active version';$sc.WindowStyle=1;$sc.Save()
  if(![string]::Equals($wsh.CreateShortcut($link).TargetPath,$launcher,[StringComparison]::OrdinalIgnoreCase)){throw 'Shortcut verification failed.'}
  Write-Output ('Retargeted Commander shortcut: '+[IO.Path]::GetFileName($link))
 }catch{$shortcutFailure=$true;Write-Warning 'Shortcut repair failed; old folders will be retained.'}
}
Add-Type -TypeDefinition 'using System;using System.Runtime.InteropServices;public static class CommanderShellRefresh{[DllImport("shell32.dll")]public static extern void SHChangeNotify(uint e,uint f,IntPtr a,IntPtr b);}'
[CommanderShellRefresh]::SHChangeNotify(0x08000000,0,[IntPtr]::Zero,[IntPtr]::Zero)
function ValidArchive([string]$path,[string]$expected){
 if(!(Test-Path -LiteralPath $path -PathType Leaf)){return $false}
 $zip=$null
 try {
  $zip=[IO.Compression.ZipFile]::OpenRead($path)
  $entry=$zip.Entries | Where-Object {$_.FullName.Replace('\','/') -eq 'Torn Commander/VERSION.txt'} | Select-Object -First 1
  $csproj=$zip.Entries | Where-Object {$_.FullName.Replace('\','/') -eq 'Torn Commander/TornCommander.csproj'} | Select-Object -First 1
  if(!$entry -or !$csproj){return $false}
  $reader=[IO.StreamReader]::new($entry.Open());try{$actual=$reader.ReadToEnd().Trim()}finally{$reader.Dispose()}
  if($actual -ne $expected){return $false}
  $reader=[IO.StreamReader]::new($csproj.Open());try{[xml]$xml=$reader.ReadToEnd()}finally{$reader.Dispose()}
  if($xml.Project.PropertyGroup.Version -ne $expected){return $false}
  foreach($file in @('Program.cs','MainForm.cs','PUBLISH_EXE.bat','REBUILD_AND_LAUNCH.bat','CommanderUpdater.cs','APPLY_COMMANDER_UPDATE.ps1','CREATE_DESKTOP_SHORTCUT.ps1')){
   if(!($zip.Entries | Where-Object {$_.FullName.Replace('\','/') -eq ('Torn Commander/'+$file)})){return $false}
  }
  foreach($file in $zip.Entries){
   if($file.FullName.Replace('\','/').Split('/') -contains '..'){return $false}
   if(!$file.Name){continue}
   $stream=$file.Open();try{$stream.CopyTo([IO.Stream]::Null)}finally{$stream.Dispose()}
  }
  return $true
 }catch{Write-Warning ('Archive validation: '+$_.Exception.Message);return $false}finally{if($zip){$zip.Dispose()}}
}
$others=@(Get-ChildItem -LiteralPath $base -Directory | Where-Object {$_.Name -match '^Torn_Commander_v\d+\.\d+\.\d+$' -and $_.FullName -ne $outer} | Sort-Object {[version]($_.Name -replace '^Torn_Commander_v','')} -Descending)
$previous=if($others.Count){$others[0].FullName}else{''}
foreach($folder in $others){
 if($folder.Attributes -band [IO.FileAttributes]::ReparsePoint){Write-Warning 'Linked version folder retained.';continue}
 $candidate=[IO.Path]::GetFullPath($folder.FullName).TrimEnd('\')
 if((Split-Path -Parent $candidate) -ne $base -or (Split-Path -Leaf $candidate) -notmatch '^Torn_Commander_v\d+\.\d+\.\d+$'){continue}
 $oldProject=Join-Path $candidate 'Torn Commander';$oldVersion=$folder.Name -replace '^Torn_Commander_v',''
 if(!(Test-Path -LiteralPath (Join-Path $oldProject 'TornCommander.csproj'))){continue}
 if((Get-Content -LiteralPath (Join-Path $oldProject 'VERSION.txt') -Raw).Trim() -ne $oldVersion){continue}
 if(@(Get-ChildItem -LiteralPath $oldProject -Force | Where-Object {$_.Name -in @('settings.json','Profiles','WebView2Profile','.git','User Data')}).Count){Write-Warning 'Old folder contains local/private data; retained.';continue}
 $archive=Join-Path $vault ($folder.Name+'.zip')
 if(!(ValidArchive $archive $oldVersion)){
  if(Test-Path -LiteralPath $archive){Write-Warning 'Existing rollback archive is invalid; folder retained.';continue}
  $stage=Join-Path $DataRoot ('MaintenanceStaging\'+[guid]::NewGuid().ToString('N'))
  $backup=Join-Path $stage 'Torn Commander';$temp=Join-Path $vault ('backup-'+[guid]::NewGuid().ToString('N')+'.zip')
  try {
   New-Item -ItemType Directory -Path $backup -Force | Out-Null
   foreach($item in Get-ChildItem -LiteralPath $oldProject -Force){
    if($item.Name -in @('bin','obj','dist','.vs','ACTIVE_DIST.txt','FIRST_RUN_REBUILD.flag','VersionBackups','UpdateBackups') -or $item.Name -like 'dist-*' -or $item.Name -like '*.log' -or $item.Name -like '*Backup*.zip' -or $item.Name -like '*.env'){continue}
    Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $backup $item.Name) -Recurse -Force
   }
   Compress-Archive -Path $backup -DestinationPath $temp -CompressionLevel Optimal
   if(!(ValidArchive $temp $oldVersion)){throw 'Backup validation failed.'}
   Move-Item -LiteralPath $temp -Destination $archive
   Write-Output ('Saved rollback source: '+$folder.Name)
  }finally{
   if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp -Force}
   $stageFull=[IO.Path]::GetFullPath($stage)
   if($stageFull.StartsWith(([IO.Path]::GetFullPath((Join-Path $DataRoot 'MaintenanceStaging'))+'\'),[StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $stageFull)){Remove-Item -LiteralPath $stageFull -Recurse -Force}
  }
 }
 $visibleArchive=Join-Path $base ($folder.Name+'.zip')
 if(!(Test-Path -LiteralPath $visibleArchive)){
  Copy-Item -LiteralPath $archive -Destination $visibleArchive
 }
 if(!(ValidArchive $visibleArchive $oldVersion)){Write-Warning 'Visible rollback ZIP failed verification; folder retained.';continue}
 if($SkipCleanup -or $shortcutFailure -or (-not $CompactAllPrevious -and $candidate -eq $previous)){continue}
 $running=@(Get-Process TornCommander -ErrorAction SilentlyContinue | Where-Object {try{$_.Path.StartsWith($candidate+'\',[StringComparison]::OrdinalIgnoreCase)}catch{$true}})
 if($running.Count){Write-Warning 'Older Commander is still running; retained.';continue}
 if(ValidArchive $archive $oldVersion){try{Remove-Item -LiteralPath $candidate -Recurse -Force -ErrorAction Stop;Write-Output ('Removed archived older folder: '+$folder.Name)}catch{Write-Warning 'Old folder remains locked; cleanup deferred.'}}
}
Write-Output ('Active Commander version: '+$version)
