param(
    [Parameter(Mandatory = $true)][string]$ZipPath,
    [Parameter(Mandatory = $true)][string]$ProjectRoot,
    [Parameter(Mandatory = $true)][int]$WaitPid
)

# Torn Commander update helper v0.6.8.
# All paths are passed as individual PowerShell arguments. Do NOT assemble a cmd /k
# string out of an untrusted path: paths such as "Desktop\Torn Commander" contain spaces.
$ErrorActionPreference = 'Stop'
$logRoot = Join-Path $env:LOCALAPPDATA 'TornCommander'
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
$updateLog = Join-Path $logRoot 'update-last.log'
$buildLog = Join-Path $logRoot 'update-build-last.log'
$script:backedUp = $false
$script:copyStarted = $false
$script:backup = $null
$script:root = $null
$script:stage = $null

function Write-UpdateLog([string]$message) {
    $line = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $message"
    Write-Host $line
    [IO.File]::AppendAllText($updateLog, "$line`r`n")
}

function Restore-PreviousBuild {
    if (-not $script:backedUp -or -not $script:copyStarted -or -not (Test-Path -LiteralPath $script:backup)) { return }
    Write-UpdateLog 'Restoring previous source files and compiled executable from the backup.'
    $previous = Join-Path $script:backup 'source'
    if (Test-Path -LiteralPath $previous) {
        Get-ChildItem -LiteralPath $previous -Force | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $script:root -Recurse -Force
        }
    }
    $priorDist = Join-Path $script:backup 'dist'
    $currentDist = Join-Path $script:root 'dist'
    if (Test-Path -LiteralPath $priorDist) {
        if (Test-Path -LiteralPath $currentDist) { Remove-Item -LiteralPath $currentDist -Recurse -Force }
        Copy-Item -LiteralPath $priorDist -Destination $currentDist -Recurse -Force
    }
}

try {
    [IO.File]::WriteAllText($updateLog, '')
    $script:root = [IO.Path]::GetFullPath($ProjectRoot)
    $project = Join-Path $script:root 'TornCommander.csproj'
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw 'Commander source folder was not found.' }
    if (-not (Test-Path -LiteralPath $ZipPath -PathType Leaf)) { throw 'Downloaded update ZIP was not found.' }

    Write-UpdateLog "Updating Commander in: $script:root"
    Write-UpdateLog 'Waiting for the old Commander process to exit.'
    try { Wait-Process -Id $WaitPid -Timeout 45 -ErrorAction SilentlyContinue } catch {}
    if (Get-Process -Id $WaitPid -ErrorAction SilentlyContinue) {
        throw 'Commander is still running. Close it completely and try again.'
    }

    $script:stage = Join-Path $env:TEMP ('tc-update-stage-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $script:stage -Force | Out-Null
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $script:stage -Force
    $source = Join-Path $script:stage 'Torn Commander'
    foreach ($required in @('TornCommander.csproj', 'VERSION.txt', 'REBUILD_AND_LAUNCH.bat', 'PUBLISH_EXE.bat', 'CommanderUpdater.cs', 'APPLY_COMMANDER_UPDATE.ps1')) {
        if (-not (Test-Path -LiteralPath (Join-Path $source $required) -PathType Leaf)) {
            throw "Update ZIP is incomplete (missing $required)."
        }
    }
    $expectedVersion = (Get-Content -LiteralPath (Join-Path $source 'VERSION.txt') -Raw).Trim()
    if ($expectedVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Update ZIP has an invalid VERSION.txt.' }
    Write-UpdateLog "Preparing Commander v$expectedVersion."

    $backupBase = Join-Path ([IO.Path]::GetDirectoryName($script:root)) 'TornCommander-UpdateBackups'
    New-Item -ItemType Directory -Path $backupBase -Force | Out-Null
    $script:backup = Join-Path $backupBase ('UpdateBackup-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
    $sourceBackup = Join-Path $script:backup 'source'
    New-Item -ItemType Directory -Path $sourceBackup -Force | Out-Null
    Get-ChildItem -LiteralPath $script:root -Force | Where-Object {
        $_.Name -notin @('dist', 'bin', 'obj') -and $_.Name -notlike 'UpdateBackup-*'
    } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $sourceBackup -Recurse -Force }

    $oldDist = Join-Path $script:root 'dist'
    if (Test-Path -LiteralPath $oldDist) {
        Copy-Item -LiteralPath $oldDist -Destination (Join-Path $script:backup 'dist') -Recurse -Force
    }
    $script:backedUp = $true
    Write-UpdateLog "Backup created: $script:backup"

    $script:copyStarted = $true
    Get-ChildItem -LiteralPath $source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $script:root -Recurse -Force
    }
    Write-UpdateLog 'New source copied. Building with existing publisher.'

    # Direct invocation through PowerShell's call operator quotes the full batch
    # path correctly, even when the project lives under OneDrive/Desktop/Torn Commander.
    $publisher = Join-Path $script:root 'PUBLISH_EXE.bat'
    & $publisher 2>&1 | Out-File -FilePath $buildLog -Encoding UTF8
    $buildExitCode = $LASTEXITCODE
    if ($buildExitCode -ne 0) {
        Get-Content -LiteralPath $buildLog -Tail 35 | ForEach-Object { Write-Host $_ }
        throw "Rebuild failed (exit $buildExitCode). See $buildLog"
    }

    $newExe = Join-Path $script:root 'dist\TornCommander.exe'
    $newVersion = Join-Path $script:root 'dist\VERSION.txt'
    if (-not (Test-Path -LiteralPath $newExe -PathType Leaf)) { throw 'Publisher finished but TornCommander.exe is missing.' }
    if (-not (Test-Path -LiteralPath $newVersion -PathType Leaf) -or
        (Get-Content -LiteralPath $newVersion -Raw).Trim() -ne $expectedVersion) {
        throw 'Publisher finished but the compiled version marker does not match the new source.'
    }

    Write-UpdateLog "Build v$expectedVersion completed. Starting the EXE directly (without cmd /k)."
    Start-Process -FilePath $newExe -WorkingDirectory (Split-Path $newExe -Parent) -ErrorAction Stop
    Write-UpdateLog 'Update completed; Commander launch request succeeded.'
    if (Test-Path -LiteralPath $script:stage) { Remove-Item -LiteralPath $script:stage -Recurse -Force -ErrorAction SilentlyContinue }
    exit 0
}
catch {
    $errorText = $_.Exception.Message
    Write-Host "UPDATE FAILED: $errorText" -ForegroundColor Red
    try { Write-UpdateLog "ERROR: $errorText" } catch {}
    try { Restore-PreviousBuild } catch { Write-UpdateLog "ROLLBACK ERROR: $($_.Exception.Message)" }
    Write-Host "Logs: $updateLog and $buildLog" -ForegroundColor Yellow
    Write-Host 'Previous local login cookies and account credentials were not touched.' -ForegroundColor Yellow
    Write-Host 'Press Enter to close this diagnostic window.'
    [void][Console]::ReadLine()
    exit 1
}