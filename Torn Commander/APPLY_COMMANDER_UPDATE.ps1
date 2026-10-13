param(
    [Parameter(Mandatory=$true)][string]$ZipPath,
    [Parameter(Mandatory=$true)][string]$ProjectRoot,
    [Parameter(Mandatory=$true)][int]$WaitPid,
    [switch]$NoLaunch
)
# v0.6.17: one active version folder, compact local ZIP rollback library.
# Never rename/overwrite a live EXE; old install cleanup is strictly best effort.
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:LOCALAPPDATA 'TornCommander'
$vault = Join-Path $root 'VersionBackups'
$log = Join-Path $root 'update-last.log'
$buildLog = Join-Path $root 'update-build-last.log'
$staging = $null
$newOuter = $null
$installed = $false
New-Item -ItemType Directory -Path $vault -Force | Out-Null
function Log([string]$message) {
    $line = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $message"
    Write-Host $line
    Add-Content -LiteralPath $log -Value $line
}
function CopySources([string]$from,[string]$to) {
    $excluded = @('dist','bin','obj','.git','.vs','ACTIVE_DIST.txt','FIRST_RUN_REBUILD.flag',
                  'settings.json','WebView2Profile','Profiles','VersionBackups','UpdateBackups')
    New-Item -ItemType Directory -Path $to -Force | Out-Null
    foreach($item in (Get-ChildItem -LiteralPath $from -Force)) {
        if ($item.Name -in $excluded -or $item.Name -like 'dist-v*' -or
            $item.Name -like 'dist-prior-*' -or $item.Name -like 'dist-failed-*' -or
            $item.Name -like '*.log' -or $item.Name -like '*.env') { continue }
        Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $to $item.Name) -Recurse -Force -ErrorAction Stop
    }
}
function CheckVersion([string]$project,[string]$expected) {
    foreach($name in @('VERSION.txt','TornCommander.csproj','APPLY_COMMANDER_UPDATE.ps1','CREATE_DESKTOP_SHORTCUT.ps1')) {
        if (!(Test-Path -LiteralPath (Join-Path $project $name) -PathType Leaf)) {
            throw "Installation missing required file $name"
        }
    }
    if ((Get-Content -LiteralPath (Join-Path $project 'VERSION.txt') -Raw).Trim() -ne $expected) {
        throw 'Installation source version does not match the target.'
    }
    if (!(Test-Path -LiteralPath (Join-Path $project 'dist\TornCommander.exe') -PathType Leaf) -or
        (Get-Content -LiteralPath (Join-Path $project 'dist\VERSION.txt') -Raw).Trim() -ne $expected) {
        throw 'Installation compiled EXE/version verification failed.'
    }
}
function UpdateDesktopShortcut([string]$oldProject,[string]$newProject,[string]$exe,[string]$version) {
    $desktop = [Environment]::GetFolderPath('Desktop')
    if (-not (Test-Path -LiteralPath $desktop -PathType Container)) {
        Log 'Desktop folder unavailable; shortcut update skipped.'
        return
    }
    $link = Join-Path $desktop 'Torn Commander.lnk'
    $icon = Join-Path $newProject 'Assets\TornCommander_T_Icon.ico'
    if (!(Test-Path -LiteralPath $icon -PathType Leaf)) {
        Log 'Custom T icon not found. Existing shortcut preserved.'
        return
    }
    $wsh = New-Object -ComObject WScript.Shell
    if (Test-Path -LiteralPath $link -PathType Leaf) {
        $prior = $wsh.CreateShortcut($link).TargetPath
        if ($prior -and -not $prior.StartsWith(($oldProject + '\'),[StringComparison]::OrdinalIgnoreCase) -and
            -not $prior.StartsWith(($newProject + '\'),[StringComparison]::OrdinalIgnoreCase)) {
            Log "Another installation owns the desktop shortcut; it was not changed: $prior"
            return
        }
    }
    $sc = $wsh.CreateShortcut($link)
    $sc.TargetPath = $exe
    $sc.WorkingDirectory = Split-Path -Parent $exe
    $sc.IconLocation = $icon + ',0'
    $sc.Description = "Torn Commander v$version"
    $sc.WindowStyle = 1
    $sc.Save()
    if (-not [string]::Equals($wsh.CreateShortcut($link).TargetPath,$exe,[StringComparison]::OrdinalIgnoreCase)) {
        throw 'T desktop shortcut target failed verification.'
    }
    Log "Desktop shortcut updated to $exe"
}
function PruneArchives([string]$activeVersion) {
    # Keep a maximum of five other locally saved versions.
    $archives = @(Get-ChildItem -LiteralPath $vault -Filter 'Torn_Commander_v*.zip' -File |
        Where-Object { $_.BaseName -match '^Torn_Commander_v\d+\.\d+\.\d+$' } |
        Sort-Object LastWriteTimeUtc -Descending)
    $old = @($archives | Where-Object { $_.BaseName -ne ('Torn_Commander_v'+$activeVersion) })
    foreach($archive in ($old | Select-Object -Skip 5)) {
        try { Remove-Item -LiteralPath $archive.FullName -Force -ErrorAction Stop }
        catch { Log "Could not prune old rollback archive: $($archive.Name)" }
    }
}
try {
    [IO.File]::WriteAllText($log,'')
    [IO.File]::WriteAllText($buildLog,'')
    Set-Location -LiteralPath $root
    # Clean binaries previously retired from locked installation folders.
    $retiredStore = Join-Path $root 'RetiredInstallations'
    if (Test-Path -LiteralPath $retiredStore -PathType Container) {
        foreach($folder in (Get-ChildItem -LiteralPath $retiredStore -Directory)) {
            if ($folder.Name -notmatch '^Torn_Commander_v[0-9]+[.][0-9]+[.][0-9]+-[a-f0-9]{8}$') { continue }
            try { Remove-Item -LiteralPath $folder.FullName -Recurse -Force -ErrorAction Stop }
            catch { Log ("Retired installation remains locked: " + $folder.Name) }
        }
    }
    $oldProject = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\')
    $oldOuter = Split-Path -Parent $oldProject
    $oldVersion = (Get-Content -LiteralPath (Join-Path $oldProject 'VERSION.txt') -Raw).Trim()
    if ($oldVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'The installed version marker is invalid.' }
    if (!(Test-Path -LiteralPath $ZipPath -PathType Leaf)) { throw 'Target ZIP is missing.' }
    Log "Version switch requested from $oldProject"
    Log "Waiting for old Commander process $WaitPid."
    try { Wait-Process -Id $WaitPid -Timeout 60 -ErrorAction SilentlyContinue } catch {}
    if (Get-Process -Id $WaitPid -ErrorAction SilentlyContinue) { throw 'Old Commander is still running.' }
    $staging = Join-Path $root ('UpdateStaging\' + [guid]::NewGuid().ToString('N'))
    $unpacked = Join-Path $staging 'unpacked'
    New-Item -ItemType Directory -Path $unpacked -Force | Out-Null
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $unpacked -Force
    $source = Join-Path $unpacked 'Torn Commander'
    foreach ($required in @('VERSION.txt','TornCommander.csproj','PUBLISH_EXE.bat',
        'REBUILD_AND_LAUNCH.bat','CommanderUpdater.cs','APPLY_COMMANDER_UPDATE.ps1',
        'CREATE_DESKTOP_SHORTCUT.ps1')) {
        if (!(Test-Path -LiteralPath (Join-Path $source $required) -PathType Leaf)) {
            throw "Target package missing $required"
        }
    }
    $targetVersion = (Get-Content -LiteralPath (Join-Path $source 'VERSION.txt') -Raw).Trim()
    if ($targetVersion -notmatch '^\d+\.\d+\.\d+$' -or $targetVersion -eq $oldVersion) {
        throw 'Target must have a different valid version number.'
    }
    # Build in a private staging directory. No installed EXE is overwritten.
    Log "Building v$targetVersion in isolated staging."
    & (Join-Path $source 'PUBLISH_EXE.bat') *> $buildLog
    if ($LASTEXITCODE -ne 0) { throw "Staged build failed with exit code $LASTEXITCODE; active installation preserved." }
    CheckVersion $source $targetVersion
    Log "Staged EXE for v$targetVersion verified."

    # Save a restorable source package for the version being left before activation.
    $oldArchive = Join-Path $vault ('Torn_Commander_v' + $oldVersion + '.zip')
    if (!(Test-Path -LiteralPath $oldArchive -PathType Leaf)) {
        $backup = Join-Path $staging 'backup'
        $backupSource = Join-Path $backup 'Torn Commander'
        CopySources $oldProject $backupSource
        if ((Get-Content -LiteralPath (Join-Path $backupSource 'VERSION.txt') -Raw).Trim() -ne $oldVersion) {
            throw 'Could not produce verified previous-version backup.'
        }
        Compress-Archive -Path $backupSource -DestinationPath $oldArchive -CompressionLevel Optimal -Force
        Log "Saved compact rollback source: $oldArchive"
    }
    $targetArchive = Join-Path $vault ('Torn_Commander_v' + $targetVersion + '.zip')
    if (!(Test-Path -LiteralPath $targetArchive -PathType Leaf)) {
        Copy-Item -LiteralPath $ZipPath -Destination $targetArchive -ErrorAction Stop
    }

    # Single active installation: install into the correct version-named folder
    # and retire the old one only after the new executable is verified.
    $installBase = if ((Split-Path -Leaf $oldOuter) -match '^Torn_Commander_v\d+\.\d+\.\d+$') {
        Split-Path -Parent $oldOuter
    } else {
        Split-Path -Parent $oldOuter
    }
    $newOuter = Join-Path $installBase ('Torn_Commander_v' + $targetVersion)
    $newProject = Join-Path $newOuter 'Torn Commander'
    if (Test-Path -LiteralPath $newOuter) {
        # A prior version may remain because OneDrive held its executable
        # open. Reuse only if it already passes exact-version verification.
        CheckVersion $newProject $targetVersion
        Log "Found verified existing v$targetVersion installation; reusing it without changing files."
    } else {
        New-Item -ItemType Directory -Path $newProject -Force | Out-Null
        CopySources $source $newProject
        $newDist = Join-Path $newProject 'dist'
        Copy-Item -LiteralPath (Join-Path $source 'dist') -Destination $newDist -Recurse -Force -ErrorAction Stop
        CheckVersion $newProject $targetVersion
    }
    $newDist = Join-Path $newProject 'dist'
    $newExe = Join-Path $newDist 'TornCommander.exe'
    $installed = $true
    Log "Verified active installation at $newOuter"
    if (-not $NoLaunch) {
        try { UpdateDesktopShortcut $oldProject $newProject $newExe $targetVersion }
        catch { Log "Shortcut warning: $($_.Exception.Message). New executable is available in $newDist." }
    }

    # Never move/delete the installation we just left. Windows can partially
    # move a directory before reporting a lock, leaving an unusable fallback.
    # Keep one immediate previous installation, and clean EARLIER versions
    # only on a later switch, when a source ZIP is already secured in the vault.
    $pendingFile = Join-Path $root 'PendingVersionFolders.txt'
    $pending = @()
    if (Test-Path -LiteralPath $pendingFile -PathType Leaf) {
        $pending = @(Get-Content -LiteralPath $pendingFile | Where-Object { $_ })
    }
    if ((Split-Path -Leaf $oldOuter) -match '^Torn_Commander_v[0-9]+[.][0-9]+[.][0-9]+$' -and
        $pending -notcontains $oldOuter) {
        $pending += $oldOuter
        Log "Previous installation retained for immediate recovery: $oldOuter"
    }
    $keepPending = @()
    foreach($candidate in ($pending | Select-Object -Unique)) {
        try {
            $full = [IO.Path]::GetFullPath($candidate).TrimEnd('\')
            $leaf = Split-Path -Leaf $full
            $candidateVersion = $leaf -replace '^Torn_Commander_v',''
            if ($leaf -notmatch '^Torn_Commander_v[0-9]+[.][0-9]+[.][0-9]+$' -or
                -not [string]::Equals((Split-Path -Parent $full),$installBase,[StringComparison]::OrdinalIgnoreCase)) { continue }
            if ([string]::Equals($full,$oldOuter,[StringComparison]::OrdinalIgnoreCase) -or
                [string]::Equals($full,$newOuter,[StringComparison]::OrdinalIgnoreCase)) {
                $keepPending += $full
                continue
            }
            if (-not (Test-Path -LiteralPath $full -PathType Container)) { continue }
            $archive = Join-Path $vault ('Torn_Commander_v' + $candidateVersion + '.zip')
            if (-not (Test-Path -LiteralPath $archive -PathType Leaf) -or
                -not (Test-Path -LiteralPath (Join-Path $full 'Torn Commander\TornCommander.csproj') -PathType Leaf)) {
                $keepPending += $full
                Log "Old installation was not pruned because backup/source verification is missing: $full"
                continue
            }
            try {
                Remove-Item -LiteralPath $full -Recurse -Force -ErrorAction Stop
                if (Test-Path -LiteralPath $full) { $keepPending += $full }
                else { Log "Pruned older version folder after preserving rollback ZIP: $leaf" }
            }
            catch {
                $keepPending += $full
                Log "Older version cleanup delayed by Windows file lock: $leaf"
            }
        } catch { Log "Could not evaluate a queued old version path; leaving cleanup for later." }
    }
    Set-Content -LiteralPath $pendingFile -Value @($keepPending | Select-Object -Unique) -Encoding UTF8
    PruneArchives $targetVersion
    if (-not $NoLaunch) {
        Start-Process -FilePath $newExe -WorkingDirectory $newDist -ErrorAction Stop
        Log "Launched Torn Commander v$targetVersion."
    }
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue }
    exit 0
}
catch {
    Write-Host "VERSION SWITCH FAILED: $($_.Exception.Message)" -ForegroundColor Red
    try { Log "ERROR: $($_.Exception.Message)" } catch {}
    if (-not $installed -and $newOuter -and (Test-Path -LiteralPath $newOuter)) {
        Log "Incomplete target directory retained for inspection: $newOuter. Old installation was preserved."
    }
    Write-Host "The prior application is preserved. Logs: $log and $buildLog" -ForegroundColor Yellow
    if (-not $NoLaunch) { Write-Host 'Press Enter to close diagnostic window.'; [void][Console]::ReadLine() }
    exit 1
}
