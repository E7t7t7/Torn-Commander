param(
    [Parameter(Mandatory = $true)][string]$ZipPath,
    [Parameter(Mandatory = $true)][string]$ProjectRoot,
    [Parameter(Mandatory = $true)][int]$WaitPid,
    [switch]$NoLaunch
)
# Release v0.6.16+: install a complete validated version in a NEW versioned
# sibling directory. Never rename, delete or write inside the previous install.
$ErrorActionPreference = 'Stop'
$logRoot = Join-Path $env:LOCALAPPDATA 'TornCommander'
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
$updateLog = Join-Path $logRoot 'update-last.log'
$buildLog = Join-Path $logRoot 'update-build-last.log'
$script:destination = $null
$script:completed = $false
$script:staging = $null

function Log([string]$message) {
    $line = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $message"
    Write-Host $line
    [IO.File]::AppendAllText($updateLog, $line + [Environment]::NewLine)
}
function RetryCopy([string]$name, [scriptblock]$action) {
    for ($i = 1; $i -le 5; $i++) {
        try { & $action; return }
        catch {
            if ($i -eq 5) { throw "$name failed after 5 attempts: $($_.Exception.Message)" }
            Log "$name blocked ($i/5): $($_.Exception.Message)"
            Start-Sleep -Milliseconds 800
        }
    }
}
function CopySource([string]$source, [string]$destination) {
    $excluded = @('dist','bin','obj','.git','ACTIVE_DIST.txt','FIRST_RUN_REBUILD.flag')
    foreach ($item in (Get-ChildItem -LiteralPath $source -Force)) {
        if ($item.Name -in $excluded -or $item.Name -like 'dist-v*' -or
            $item.Name -like 'dist-prior-*' -or $item.Name -like 'dist-failed-*') { continue }
        $target = Join-Path $destination $item.Name
        RetryCopy ("Copy " + $item.Name) {
            Copy-Item -LiteralPath $item.FullName -Destination $target -Recurse -Force -ErrorAction Stop
        }
    }
}
function RepointShortcut([string]$oldRoot,[string]$newRoot,[string]$exe,[string]$version) {
    $desktop = [Environment]::GetFolderPath('Desktop')
    if (-not $desktop -or -not (Test-Path -LiteralPath $desktop -PathType Container)) {
        Log 'Desktop not found; new EXE can be launched directly.'
        return
    }
    $shortcutPath = Join-Path $desktop 'Torn Commander.lnk'
    $icon = Join-Path $newRoot 'Assets\TornCommander_T_Icon.ico'
    if (-not (Test-Path -LiteralPath $icon -PathType Leaf)) {
        Log 'T icon missing; leaving existing desktop shortcut unchanged.'
        return
    }
    $wsh = New-Object -ComObject WScript.Shell
    if (Test-Path -LiteralPath $shortcutPath -PathType Leaf) {
        $existing = $wsh.CreateShortcut($shortcutPath).TargetPath
        # Never hijack the shortcut for a different Commander installation.
        if ($existing -and -not $existing.StartsWith(($oldRoot + '\'),[StringComparison]::OrdinalIgnoreCase)) {
            Log "Desktop shortcut targets another installation; left untouched: $existing"
            return
        }
    }
    $sc = $wsh.CreateShortcut($shortcutPath)
    $sc.TargetPath = $exe
    $sc.WorkingDirectory = Split-Path -Parent $exe
    $sc.IconLocation = $icon + ',0'
    $sc.Description = "Torn Commander v$version"
    $sc.WindowStyle = 1
    $sc.Save()
    $verified = $wsh.CreateShortcut($shortcutPath)
    if (-not [string]::Equals($verified.TargetPath,$exe,[StringComparison]::OrdinalIgnoreCase)) {
        throw 'Desktop shortcut target failed verification.'
    }
    Log "Desktop shortcut targets: $exe"
}

try {
    [IO.File]::WriteAllText($updateLog, '')
    [IO.File]::WriteAllText($buildLog, '')
    Set-Location -LiteralPath $logRoot
    $oldRoot = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\')
    if (-not (Test-Path -LiteralPath (Join-Path $oldRoot 'TornCommander.csproj') -PathType Leaf)) {
        throw "Existing source directory was not found: $oldRoot"
    }
    if (-not (Test-Path -LiteralPath $ZipPath -PathType Leaf)) {
        throw 'Update ZIP does not exist.'
    }
    Log "Update requested from: $oldRoot"
    Log 'Waiting for the running Commander process to exit.'
    try { Wait-Process -Id $WaitPid -Timeout 60 -ErrorAction SilentlyContinue } catch {}
    if (Get-Process -Id $WaitPid -ErrorAction SilentlyContinue) {
        throw 'The old Commander process did not exit. Existing installation untouched.'
    }
    $script:staging = Join-Path $logRoot ('UpdateStaging\' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $script:staging -Force | Out-Null
    $unpacked = Join-Path $script:staging 'unpacked'
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $unpacked -Force
    $source = Join-Path $unpacked 'Torn Commander'
    foreach ($name in @('TornCommander.csproj','VERSION.txt','PUBLISH_EXE.bat',
        'REBUILD_AND_LAUNCH.bat','CommanderUpdater.cs','APPLY_COMMANDER_UPDATE.ps1',
        'CREATE_DESKTOP_SHORTCUT.ps1')) {
        if (-not (Test-Path -LiteralPath (Join-Path $source $name) -PathType Leaf)) {
            throw "Release ZIP is incomplete: $name"
        }
    }
    $version = (Get-Content -LiteralPath (Join-Path $source 'VERSION.txt') -Raw).Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid VERSION.txt in update.' }

    # Build first, in a staging directory outside the installed EXE folders.
    Log "Building v$version in staging; all installed files remain untouched."
    & (Join-Path $source 'PUBLISH_EXE.bat') *> $buildLog
    $buildResult = $LASTEXITCODE
    if ($buildResult -ne 0) {
        Get-Content -LiteralPath $buildLog -Tail 25 | ForEach-Object { Write-Host $_ }
        throw "Staged Windows build failed with exit code $buildResult."
    }
    $published = Join-Path $source 'dist'
    $publishedExe = Join-Path $published 'TornCommander.exe'
    $publishedVersion = Join-Path $published 'VERSION.txt'
    if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf) -or
        -not (Test-Path -LiteralPath $publishedVersion -PathType Leaf) -or
        (Get-Content -LiteralPath $publishedVersion -Raw).Trim() -ne $version) {
        throw 'The staged EXE and VERSION marker did not verify.'
    }

    # A path installed as Torn_Commander_vOLD\Torn Commander gets a
    # versioned sibling. An unversioned install receives a new versioned child.
    $oldContainer = Split-Path -Parent $oldRoot
    if ((Split-Path -Leaf $oldContainer) -match '^Torn_Commander_v\d+\.\d+\.\d+$') {
        $installBase = Split-Path -Parent $oldContainer
    } else { $installBase = $oldContainer }
    $newContainer = Join-Path $installBase ('Torn_Commander_v' + $version)
    $script:destination = Join-Path $newContainer 'Torn Commander'
    if (Test-Path -LiteralPath $newContainer) {
        throw "Version folder already exists; will not overwrite it: $newContainer. The older installation remains unchanged."
    }

    Log "Installing verified v$version into: $script:destination"
    New-Item -ItemType Directory -Path $script:destination -Force | Out-Null
    CopySource $source $script:destination
    $newDist = Join-Path $script:destination 'dist'
    New-Item -ItemType Directory -Path $newDist -Force | Out-Null
    foreach ($item in (Get-ChildItem -LiteralPath $published -Force)) {
        $entry = $item
        RetryCopy ("Install executable " + $entry.Name) {
            Copy-Item -LiteralPath $entry.FullName -Destination $newDist -Recurse -Force -ErrorAction Stop
        }
    }
    $newExe = Join-Path $newDist 'TornCommander.exe'
    if (-not (Test-Path -LiteralPath $newExe -PathType Leaf) -or
        (Get-Content -LiteralPath (Join-Path $newDist 'VERSION.txt') -Raw).Trim() -ne $version -or
        (Get-Content -LiteralPath (Join-Path $script:destination 'VERSION.txt') -Raw).Trim() -ne $version) {
        throw 'New version folder verification failed; old installation untouched.'
    }
    $script:completed = $true
    Log "Installed v$version successfully. Prior version remains at $oldRoot."
    if (-not $NoLaunch) {
        try { RepointShortcut $oldRoot $script:destination $newExe $version }
        catch { Log "Desktop shortcut could not be updated: $($_.Exception.Message). Run CREATE_DESKTOP_SHORTCUT.bat from the new folder." }
        Start-Process -FilePath $newExe -WorkingDirectory $newDist -ErrorAction Stop
        Log 'Launched the new EXE. Close old pinned shortcuts to avoid opening the old version.'
    } else {
        Log 'Update installed; app launch skipped for Windows CI test.'
    }
    if (Test-Path -LiteralPath $script:staging) {
        Remove-Item -LiteralPath $script:staging -Recurse -Force -ErrorAction SilentlyContinue
    }
    exit 0
}
catch {
    Write-Host "UPDATE FAILED: $($_.Exception.Message)" -ForegroundColor Red
    try { Log "ERROR: $($_.Exception.Message)" } catch {}
    if (-not $script:completed -and $script:destination -and
        (Test-Path -LiteralPath $script:destination)) {
        try { Log "Incomplete new version left for inspection: $script:destination. Old installation was not modified." } catch {}
    }
    Write-Host "Old installation is preserved. Update log: $updateLog" -ForegroundColor Yellow
    Write-Host "Build log: $buildLog" -ForegroundColor Yellow
    if (-not $NoLaunch) {
        Write-Host 'Press Enter to close this window.'
        [void][Console]::ReadLine()
    }
    exit 1
}
