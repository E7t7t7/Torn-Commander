param(
    [Parameter(Mandatory=$true)][string]$ExePath,
    [Parameter(Mandatory=$true)][string]$SourceVersion
)
$ErrorActionPreference = 'Stop'
$logDir = Join-Path $env:LOCALAPPDATA 'TornCommander'
New-Item -Path $logDir -ItemType Directory -Force | Out-Null
$log = Join-Path $logDir 'launcher-last.log'
function Write-Log([string]$text) {
    $line = "[$(Get-Date -Format o)] $text"
    Add-Content -LiteralPath $log -Value $line
    Write-Host $text
}
try {
    Write-Log "Launcher v$SourceVersion checking: $ExePath"
    if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) { throw "Compiled EXE is missing: $ExePath" }
    $proc = Start-Process -FilePath $ExePath -WorkingDirectory (Split-Path -Parent $ExePath) -PassThru -ErrorAction Stop
    Write-Log "Started PID=$($proc.Id); waiting up to 12 seconds for GUI."
    for ($n=0; $n -lt 12; $n++) {
        Start-Sleep -Seconds 1
        $proc.Refresh()
        if ($proc.HasExited) {
            Write-Log "ERROR: Process ended before main window appeared. Exit code: $($proc.ExitCode)"
            Write-Log "Read startup-diagnostic.log and startup-crash.log in $logDir"
            exit 22
        }
        if ($proc.MainWindowHandle -ne [IntPtr]::Zero) {
            Write-Log "GUI detected. PID=$($proc.Id), Handle=$($proc.MainWindowHandle)."
            exit 0
        }
    }
    Write-Log "WARNING: EXE still running, but no visible main window detected after 12 seconds."
    Write-Log "Read startup-diagnostic.log in $logDir or run DIAGNOSE_STARTUP.bat"
    exit 23
}
catch {
    Write-Log "LAUNCH FAILURE: $($_.Exception.GetType().Name): $($_.Exception.Message)"
    exit 24
}
