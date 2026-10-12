param(
    [Parameter(Mandatory = $true)][string]$ZipPath,
    [Parameter(Mandatory = $true)][string]$ProjectRoot,
    [Parameter(Mandatory = $true)][int]$WaitPid,
    [switch]$NoLaunch
)

# Torn Commander staged updater v0.6.11.
# Build entirely away from the live executable, then install only after verification.
# A locked current dist is never removed or overwritten: updates fail without
# touching the installation if a safe directory swap cannot be obtained.
$ErrorActionPreference = 'Stop'
$logRoot = Join-Path $env:LOCALAPPDATA 'TornCommander'
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
$updateLog = Join-Path $logRoot 'update-last.log'
$buildLog = Join-Path $logRoot 'update-build-last.log'
$script:root = $null
$script:stage = $null
$script:backup = $null
$script:oldDistMovedTo = $null
$script:sourceTouched = $false
$script:newDistCopied = $false
$script:swapComplete = $false

function Write-UpdateLog([string]$message) {
    $line = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $message"
    Write-Host $line
    [IO.File]::AppendAllText($updateLog, "$line`r`n")
}

function Retry-UpdateIO([string]$description, [scriptblock]$action, [int]$attempts = 10) {
    for ($n = 1; $n -le $attempts; $n++) {
        try {
            & $action
            return
        }
        catch {
            if ($n -eq $attempts) { throw "$description failed after $attempts attempts: $($_.Exception.Message)" }
            Write-UpdateLog "$description blocked ($n/$attempts). Retrying after a short pause: $($_.Exception.Message)"
            Start-Sleep -Seconds 2
        }
    }
}

function Copy-SourceFiles([string]$from, [string]$to) {
    # Never copy generated outputs into source. Browser sessions and credentials
    # are held under LOCALAPPDATA, completely outside the installation tree.
    Get-ChildItem -LiteralPath $from -Force | Where-Object {
        $_.Name -notin @('dist', 'bin', 'obj', '.git', 'TornCommander-UpdateBackups') -and
        $_.Name -notlike 'dist-prior-*' -and $_.Name -notlike 'dist-failed-*'
    } | ForEach-Object {
        $item = $_
        $target = Join-Path $to $item.Name
        if ($item.PSIsContainer) {
            New-Item -ItemType Directory -Path $target -Force | Out-Null
            Get-ChildItem -LiteralPath $item.FullName -Force | ForEach-Object {
                $child = $_
                Retry-UpdateIO "Copy source $($item.Name)/$($child.Name)" {
                    Copy-Item -LiteralPath $child.FullName -Destination $target -Recurse -Force -ErrorAction Stop
                } 6
            }
        } else {
            Retry-UpdateIO "Copy source $($item.Name)" {
                Copy-Item -LiteralPath $item.FullName -Destination $target -Force -ErrorAction Stop
            } 6
        }
    }
}

function Restore-PreviousBuild {
    if (-not $script:backup -or -not (Test-Path -LiteralPath $script:backup)) { return }
    Write-UpdateLog 'Rollback requested. Preserving rather than deleting locked directories.'
    $sourceBackup = Join-Path $script:backup 'source'
    if ($script:sourceTouched -and (Test-Path -LiteralPath $sourceBackup)) {
        try { Copy-SourceFiles $sourceBackup $script:root; Write-UpdateLog 'Previous source restored.' }
        catch { Write-UpdateLog "SOURCE RESTORE BLOCKED: $($_.Exception.Message). Source backup kept at $sourceBackup" }
    }
    $liveDist = Join-Path $script:root 'dist'
    if ($script:oldDistMovedTo -and (Test-Path -LiteralPath $script:oldDistMovedTo)) {
        if (Test-Path -LiteralPath $liveDist) {
            $failedDist = Join-Path $script:root ('dist-failed-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
            try {
                Retry-UpdateIO 'Quarantine new dist' { Move-Item -LiteralPath $liveDist -Destination $failedDist -ErrorAction Stop } 8
                Write-UpdateLog "New dist quarantined at $failedDist"
            }
            catch {
                Write-UpdateLog "ROLLBACK BLOCKED by live dist: $($_.Exception.Message)"
                Write-UpdateLog "Previous binary is preserved at $script:oldDistMovedTo. Close Commander and copy it back as 'dist' manually if necessary."
                return
            }
        }
        try {
            Retry-UpdateIO 'Restore prior dist' { Move-Item -LiteralPath $script:oldDistMovedTo -Destination $liveDist -ErrorAction Stop } 8
            Write-UpdateLog 'Previous executable restored.'
            $script:oldDistMovedTo = $null
        }
        catch { Write-UpdateLog "DIST RESTORE BLOCKED: $($_.Exception.Message). Previous binary preserved at $script:oldDistMovedTo" }
    }
}

try {
    [IO.File]::WriteAllText($updateLog, '')
    [IO.File]::WriteAllText($buildLog, '')
    $script:root = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\')
    if (-not (Test-Path -LiteralPath (Join-Path $script:root 'TornCommander.csproj') -PathType Leaf)) {
        throw 'Installed Commander source folder was not found.'
    }
    if (-not (Test-Path -LiteralPath $ZipPath -PathType Leaf)) { throw 'Downloaded update ZIP is missing.' }
    Write-UpdateLog "Update requested in: $script:root"
    Write-UpdateLog 'Waiting for the old Commander process to exit.'
    try { Wait-Process -Id $WaitPid -Timeout 60 -ErrorAction SilentlyContinue } catch {}
    if (Get-Process -Id $WaitPid -ErrorAction SilentlyContinue) { throw 'Commander is still running. Close it completely before updating.' }

    $script:stage = Join-Path $logRoot ('UpdateStaging\' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $script:stage -Force | Out-Null
    $unpacked = Join-Path $script:stage 'unpacked'
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $unpacked -Force
    $source = Join-Path $unpacked 'Torn Commander'
    foreach ($required in @('TornCommander.csproj', 'VERSION.txt', 'PUBLISH_EXE.bat', 'REBUILD_AND_LAUNCH.bat', 'CommanderUpdater.cs', 'APPLY_COMMANDER_UPDATE.ps1')) {
        if (-not (Test-Path -LiteralPath (Join-Path $source $required) -PathType Leaf)) { throw "Release missing $required" }
    }
    $expectedVersion = (Get-Content -LiteralPath (Join-Path $source 'VERSION.txt') -Raw).Trim()
    if ($expectedVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Release has an invalid VERSION.txt.' }
    Write-UpdateLog "Building v$expectedVersion in a private staging folder; installed files are untouched."

    # The publisher removes its local dist before publishing. This is safe ONLY
    # because $source lives in staging, not inside the running installation.
    $publisher = Join-Path $source 'PUBLISH_EXE.bat'
    & $publisher *> $buildLog
    $buildExitCode = $LASTEXITCODE
    if ($buildExitCode -ne 0) {
        Get-Content -LiteralPath $buildLog -Tail 35 | ForEach-Object { Write-Host $_ }
        throw "Staged build failed (exit $buildExitCode). Installed Commander has not been changed."
    }
    $stagedDist = Join-Path $source 'dist'
    $stagedExe = Join-Path $stagedDist 'TornCommander.exe'
    $stagedMarker = Join-Path $stagedDist 'VERSION.txt'
    if (-not (Test-Path -LiteralPath $stagedExe -PathType Leaf) -or
        -not (Test-Path -LiteralPath $stagedMarker -PathType Leaf) -or
        (Get-Content -LiteralPath $stagedMarker -Raw).Trim() -ne $expectedVersion) {
        throw 'Staged publisher did not produce the expected EXE and version marker. Installed Commander has not been changed.'
    }
    Write-UpdateLog 'Staged build validated. Checking for other Commander instances from this folder.'

    # Any additional instance still using this installation could lock dist.
    $others = @(Get-Process -Name 'TornCommander' -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and $_.Path.StartsWith(($script:root + '\'), [StringComparison]::OrdinalIgnoreCase) } catch { $false }
    })
    if ($others.Count -gt 0) { throw 'Another Commander EXE from this installation is still running. Close every Commander window and retry. Installed files were not changed.' }

    $backupBase = Join-Path $logRoot 'UpdateBackups'
    New-Item -ItemType Directory -Path $backupBase -Force | Out-Null
    $script:backup = Join-Path $backupBase ('v' + $expectedVersion + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
    New-Item -ItemType Directory -Path (Join-Path $script:backup 'source') -Force | Out-Null
    Copy-SourceFiles $script:root (Join-Path $script:backup 'source')
    Write-UpdateLog "Previous source backed up to $script:backup"

    $oldDist = Join-Path $script:root 'dist'
    if (Test-Path -LiteralPath $oldDist) {
        $savedDist = Join-Path $script:root ('dist-prior-' + [guid]::NewGuid().ToString('N').Substring(0, 10))
        # IMPORTANT: Never Remove-Item the active executable. A locked folder
        # aborts the install BEFORE any source is replaced.
        Retry-UpdateIO 'Move old executable directory safely' {
            Move-Item -LiteralPath $oldDist -Destination $savedDist -ErrorAction Stop
        } 12
        $script:oldDistMovedTo = $savedDist
        Write-UpdateLog "Previous executable preserved at $savedDist"
    }

    $liveDist = Join-Path $script:root 'dist'
    $script:newDistCopied = $true
    New-Item -ItemType Directory -Path $liveDist -Force | Out-Null
    Get-ChildItem -LiteralPath $stagedDist -Force | ForEach-Object {
        $item = $_
        Retry-UpdateIO "Copy executable $($item.Name)" {
            Copy-Item -LiteralPath $item.FullName -Destination $liveDist -Recurse -Force -ErrorAction Stop
        } 10
    }
    $script:sourceTouched = $true
    Copy-SourceFiles $source $script:root

    # An old marker must not force the next startup to rebuild the live EXE.
    $firstRun = Join-Path $script:root 'FIRST_RUN_REBUILD.flag'
    if (Test-Path -LiteralPath $firstRun) { Remove-Item -LiteralPath $firstRun -Force -ErrorAction Stop }
    $newVersion = (Get-Content -LiteralPath (Join-Path $script:root 'VERSION.txt') -Raw).Trim()
    $installedMarker = (Get-Content -LiteralPath (Join-Path $liveDist 'VERSION.txt') -Raw).Trim()
    if ($newVersion -ne $expectedVersion -or $installedMarker -ne $expectedVersion -or
        -not (Test-Path -LiteralPath (Join-Path $liveDist 'TornCommander.exe') -PathType Leaf)) {
        throw 'Installed version verification failed.'
    }

    $script:swapComplete = $true
    Write-UpdateLog "Installed v$expectedVersion. Starting its EXE. Previous binaries kept at $script:oldDistMovedTo"
    if (-not $NoLaunch) {
        Start-Process -FilePath (Join-Path $liveDist 'TornCommander.exe') -WorkingDirectory $liveDist -ErrorAction Stop
        Write-UpdateLog 'EXE launch requested successfully. Update complete.'
    } else { Write-UpdateLog 'Update installed; launch intentionally skipped in CI simulation.' }
    if (Test-Path -LiteralPath $script:stage) { Remove-Item -LiteralPath $script:stage -Recurse -Force -ErrorAction SilentlyContinue }
    exit 0
}
catch {
    $errorText = $_.Exception.Message
    Write-Host "UPDATE FAILED: $errorText" -ForegroundColor Red
    try { Write-UpdateLog "ERROR: $errorText" } catch {}
    if (-not $script:swapComplete) {
        try { Restore-PreviousBuild } catch { Write-UpdateLog "ROLLBACK ERROR: $($_.Exception.Message)" }
    }
    else { Write-UpdateLog 'The new version was installed, but its launch returned an error. Backup is preserved for manual recovery.' }
    Write-Host "Logs: $updateLog and $buildLog" -ForegroundColor Yellow
    Write-Host 'Old account sessions, cookies and keys in LOCALAPPDATA/Credential Manager were not intentionally changed.' -ForegroundColor Yellow
    Write-Host 'Press Enter to close this diagnostic window.'
    [void][Console]::ReadLine()
    exit 1
}
