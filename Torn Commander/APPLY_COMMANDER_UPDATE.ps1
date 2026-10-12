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
        $_.Name -notlike 'dist-prior-*' -and $_.Name -notlike 'dist-failed-*' -and
        $_.Name -notlike 'dist-v*' -and $_.Name -ne 'ACTIVE_DIST.txt'
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
    # Ensure this updater never keeps the currently running binary directory as its cwd.
    Set-Location -LiteralPath $logRoot
    Write-UpdateLog "Update requested in: $script:root; updater cwd: $(Get-Location)"
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

    # Install side by side. Never rename/delete a current or old dist directory:
    # a WebView child, cmd cwd, antivirus, or OneDrive can lock it indefinitely.
    $slotName = 'dist-v' + $expectedVersion + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
    $liveDist = Join-Path $script:root $slotName
    New-Item -ItemType Directory -Path $liveDist -ErrorAction Stop | Out-Null
    foreach ($item in (Get-ChildItem -LiteralPath $stagedDist -Force)) {
        $entry = $item
        Retry-UpdateIO "Copy new version $($entry.Name)" {
            Copy-Item -LiteralPath $entry.FullName -Destination $liveDist -Recurse -Force -ErrorAction Stop
        } 8
    }
    $installedExe = Join-Path $liveDist 'TornCommander.exe'
    $installedMarker = Join-Path $liveDist 'VERSION.txt'
    if (-not (Test-Path -LiteralPath $installedExe -PathType Leaf) -or
        -not (Test-Path -LiteralPath $installedMarker -PathType Leaf) -or
        (Get-Content -LiteralPath $installedMarker -Raw).Trim() -ne $expectedVersion) {
        throw 'Side-by-side EXE/version verification failed; original dist remains untouched.'
    }
    Write-UpdateLog "New executable staged at $liveDist; original dist untouched."

    $script:sourceTouched = $true
    Copy-SourceFiles $source $script:root
    $firstRun = Join-Path $script:root 'FIRST_RUN_REBUILD.flag'
    if (Test-Path -LiteralPath $firstRun) { Remove-Item -LiteralPath $firstRun -Force -ErrorAction Stop }
    if ((Get-Content -LiteralPath (Join-Path $script:root 'VERSION.txt') -Raw).Trim() -ne $expectedVersion) {
        throw 'Installed source VERSION.txt mismatch; old EXE remains untouched.'
    }

    # Atomically switch a tiny launch pointer after the new binary and source
    # have both been verified. Never modify the old binary or its directory.
    $activeMarker = Join-Path $script:root 'ACTIVE_DIST.txt'
    $tempMarker = Join-Path $script:root ('active-' + [guid]::NewGuid().ToString('N') + '.tmp')
    [IO.File]::WriteAllText($tempMarker, $slotName)
    try {
        if (Test-Path -LiteralPath $activeMarker -PathType Leaf) {
            Retry-UpdateIO 'Activate version pointer' {
                [IO.File]::Replace($tempMarker, $activeMarker, $null, $true)
            } 6
        } else {
            Retry-UpdateIO 'Create version pointer' {
                [IO.File]::Move($tempMarker, $activeMarker)
            } 6
        }
    } finally {
        if (Test-Path -LiteralPath $tempMarker) {
            Remove-Item -LiteralPath $tempMarker -Force -ErrorAction SilentlyContinue
        }
    }
    $script:swapComplete = $true
    Write-UpdateLog "Activated $slotName. Old EXE directories remain available for recovery."

    if (-not $NoLaunch) {
        # Refresh only the standard Desktop Commander shortcut. Refuse to
        # overwrite unrelated shortcuts which point to another installation.
        try {
            $desktop = [Environment]::GetFolderPath('Desktop')
            $shortcutPath = Join-Path $desktop 'Torn Commander.lnk'
            if ((Test-Path -LiteralPath $desktop -PathType Container) -and
                (Test-Path -LiteralPath (Join-Path $script:root 'Assets\TornCommander_T_Icon.ico') -PathType Leaf)) {
                $wsh = New-Object -ComObject WScript.Shell
                $safeToEdit = $true
                if (Test-Path -LiteralPath $shortcutPath -PathType Leaf) {
                    $existingTarget = $wsh.CreateShortcut($shortcutPath).TargetPath
                    if ($existingTarget -and -not $existingTarget.StartsWith(($script:root + '\'), [StringComparison]::OrdinalIgnoreCase)) {
                        $safeToEdit = $false
                        Write-UpdateLog "Another installation owns Desktop shortcut: $existingTarget"
                    }
                }
                if ($safeToEdit) {
                    $shortcut = $wsh.CreateShortcut($shortcutPath)
                    $shortcut.TargetPath = $installedExe
                    $shortcut.WorkingDirectory = $liveDist
                    $shortcut.IconLocation = (Join-Path $script:root 'Assets\TornCommander_T_Icon.ico') + ',0'
                    $shortcut.Description = 'Torn Commander v' + $expectedVersion
                    $shortcut.Save()
                    Write-UpdateLog "Desktop shortcut updated to $installedExe"
                }
            }
        } catch { Write-UpdateLog "Desktop shortcut warning: $($_.Exception.Message). Run CREATE_DESKTOP_SHORTCUT.bat if needed." }
        Start-Process -FilePath $installedExe -WorkingDirectory $liveDist -ErrorAction Stop
        Write-UpdateLog "Launched v$expectedVersion from $liveDist"
    } else { Write-UpdateLog 'Update installed; launch skipped in CI simulation.' }
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
