param(
    [Parameter(Mandatory=$true)][string]$ZipPath,
    [Parameter(Mandatory=$true)][string]$ProjectRoot,
    [Parameter(Mandatory=$true)][int]$WaitPid
)
$ErrorActionPreference = 'Stop'
try {
    # The main application validated the archive. Verify again after staging.
    $root = [IO.Path]::GetFullPath($ProjectRoot)
    if (-not (Test-Path -LiteralPath (Join-Path $root 'TornCommander.csproj'))) { throw 'Commander source folder was not found.' }
    if (-not (Test-Path -LiteralPath $ZipPath -PathType Leaf)) { throw 'Update ZIP is missing.' }
    Write-Host 'Waiting for Torn Commander to close...' -ForegroundColor Cyan
    try { Wait-Process -Id $WaitPid -Timeout 35 -ErrorAction SilentlyContinue } catch {}
    $proc = Get-Process -Id $WaitPid -ErrorAction SilentlyContinue
    if ($null -ne $proc) { throw 'Commander is still running. Close it and retry.' }
    $stage = Join-Path $env:TEMP ('tc-stage-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $stage -Force
    $source = Join-Path $stage 'Torn Commander'
    foreach ($required in @('TornCommander.csproj','VERSION.txt','REBUILD_AND_LAUNCH.bat','CommanderUpdater.cs')) {
        if (-not (Test-Path -LiteralPath (Join-Path $source $required))) { throw "Update missing $required" }
    }
    $backupBase = Join-Path ([IO.Path]::GetDirectoryName($root)) 'TornCommander-UpdateBackups'
    New-Item -ItemType Directory -Force -Path $backupBase | Out-Null
    $backup = Join-Path $backupBase ('UpdateBackup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    # Preserve only source tree in place; never touch %LOCALAPPDATA%/TornCommander.
    Get-ChildItem -LiteralPath $root -Force | Where-Object {
        $_.Name -notin @('dist','bin','obj') -and $_.Name -notlike 'UpdateBackup-*'
    } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $backup -Recurse -Force }
    Get-ChildItem -LiteralPath $source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $root -Recurse -Force
    }
    Write-Host "Source update staged. Backup: $backup" -ForegroundColor Green
    $launcher = Join-Path $root 'REBUILD_AND_LAUNCH.bat'
    Start-Process -FilePath 'cmd.exe' -ArgumentList @('/k', '"' + $launcher + '"') -WorkingDirectory $root
    Write-Host 'Started the clean rebuild launcher.' -ForegroundColor Green
} catch {
    Write-Host ('Update failed: ' + $_.Exception.Message) -ForegroundColor Red
    Write-Host 'Your existing account data was not changed. Press Enter to close.'
    [void][Console]::ReadLine()
    exit 1
}
