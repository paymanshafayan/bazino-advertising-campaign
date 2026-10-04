# Bazino Marketing Studio Windows launcher. Built locally or by Actions via ps2exe.
# This is a tiny native launcher: the bundled Node runtime serves the local
# browser app on 127.0.0.1. No Electron/Chromium binary is downloaded or run.
param([switch]$SelfTest)

$ErrorActionPreference = 'Stop'
$packed = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName -like '*BazinoMarketing.exe'
$root = if ($packed) { Split-Path -Parent ([Diagnostics.Process]::GetCurrentProcess().MainModule.FileName) } else { $PSScriptRoot }
$server = if ($packed) { Join-Path $root 'app\src\server.cjs' } else { Join-Path $root 'src\server.cjs' }
$restart = if ($packed) { Join-Path $root 'app\src\restart-existing.cjs' } else { Join-Path $root 'src\restart-existing.cjs' }
$node = if ($packed) { Join-Path $root 'node.exe' } else { 'node.exe' }
$testing = $SelfTest -or $env:BAZINO_SELFTEST -eq '1' -or ([Environment]::GetCommandLineArgs() -contains '-SelfTest')

function Exit-SelfTest([int]$result) {
    try { [Environment]::Exit($result) } catch { }
    try { (Get-Process -Id $PID).Kill() } catch { }
    exit $result
}

if ($testing) {
    $success = (Test-Path $server) -and (Test-Path $restart)
    if ($packed) { $success = $success -and (Test-Path $node) }
    $result = if ($success) { 'self-test OK' } else { 'self-test FAILED: missing packaged files' }
    Set-Content -Path (Join-Path $env:TEMP 'bazino-marketing-selftest.log') -Value $result
    Exit-SelfTest $(if ($success) { 0 } else { 1 })
}

$running = $null
$problem = $null
try {
    if (-not (Test-Path $server) -or -not (Test-Path $restart)) {
        throw 'Studio files missing; extract the COMPLETE archive first.'
    }
    if ($packed -and -not (Test-Path $node)) { throw 'Bundled Node runtime missing; extract the COMPLETE archive first.' }
    $appData = Join-Path $env:APPDATA 'BazinoMarketingBrowser'
    New-Item -ItemType Directory -Path $appData -Force | Out-Null

    # Serialize check/stop/start for two launchers opened at once. Hold the lock
    # until our NEW server answers, so the second launcher cannot miss both.
    $startupLock = [System.Threading.Mutex]::new($false, 'Local\BazinoMarketingStudioStartup')
    $lockHeld = $false
    $started = $false
    try {
        try { $lockHeld = $startupLock.WaitOne(30000) }
        catch [System.Threading.AbandonedMutexException] { $lockHeld = $true }
        if (-not $lockHeld) { throw 'Another studio launcher is still starting. Try again in a moment.' }

        # Uses ONLY 127.0.0.1. It identifies the old app and sends its existing
        # authenticated app:stop command; it never terminates a port owner's PID.
        $preflight = & $node $restart | Out-String
        if ($LASTEXITCODE -ne 0) {
            $reason = $preflight.Trim()
            if (-not $reason) { $reason = 'Could not safely stop the server on port 59670.' }
            throw $reason
        }
        $arguments = '"' + $server + '" --bazino-launcher-pid=' + $PID
        $running = Start-Process -FilePath $node -WorkingDirectory $root -WindowStyle Hidden `
            -ArgumentList $arguments -PassThru
        $ready = & $node $restart --wait-ready | Out-String
        if ($LASTEXITCODE -ne 0) {
            $reason = $ready.Trim()
            if (-not $reason) { $reason = 'The new studio did not become ready on port 59670.' }
            throw $reason
        }
        if ($running.HasExited) { throw 'The new studio server exited during startup; check the Windows event log.' }
        $started = $true
    } finally {
        # On a failed start, release only after our newly spawned child is gone.
        if (-not $started -and $running -and -not $running.HasExited) {
            try { $running.Kill(); [void]$running.WaitForExit(5000) } catch { }
        }
        if ($lockHeld) { $startupLock.ReleaseMutex() }
        $startupLock.Dispose()
    }
    $running.WaitForExit() # wait ONLY for our local server, not a browser it opens
    if ($running.ExitCode -ne 0) { throw 'The local server exited unexpectedly; check the Windows event log.' }
} catch {
    $problem = $_.Exception.Message
} finally {
    # Ctrl+C / a normal launcher exit must not orphan OUR Node.
    if ($running -and -not $running.HasExited) {
        try { $running.Kill(); [void]$running.WaitForExit(5000) } catch { }
    }
}
if ($problem) {
    Add-Type -AssemblyName PresentationFramework
    [void][System.Windows.MessageBox]::Show($problem,'Bazino Marketing Studio','OK','Error')
    exit 1
}
