<#
.SYNOPSIS
  Opens the dedicated agent Chrome (CDP) visibly and keeps it alive.

.DESCRIPTION
  1. If CDP is already running on the port, it skips launching and only brings the window back.
  2. Otherwise it starts Chrome through WMI (Win32_Process.Create), so Chrome is NOT a child
     of the current terminal and stays open after the terminal/agent task exits.
  3. Waits until http://127.0.0.1:<Port>/json/version answers.
  4. Through CDP (Browser.setWindowBounds) un-minimizes the window, puts it on screen and activates it.
  Never uses the owner's personal profile.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File ".\scripts\open-chrome-cdp.ps1"
  powershell -ExecutionPolicy Bypass -File ".\scripts\open-chrome-cdp.ps1" -Url "https://example.com"
#>
param(
    [int]$Port = 9335,
    [string]$ProfileDir = "C:\Users\payman\.gemini\chrome-agent",
    [string]$ProfileDirectory = "Profile 1", # fartashvarahram@gmail.com
    [string]$Url = "about:blank",
    [string]$ChromePath = "C:\Program Files\Google\Chrome\Application\chrome.exe",
    [int]$Left = 100, [int]$Top = 100, [int]$Width = 1280, [int]$Height = 900,
    [int]$StartTimeoutSec = 20
)
$ErrorActionPreference = 'Stop'
$base = "http://127.0.0.1:$Port"

function Get-CdpVersion {
    try { return Invoke-RestMethod -Uri "$base/json/version" -TimeoutSec 2 } catch { return $null }
}

# ---------- 1) Launch (detached) if not already running ----------
$ver = Get-CdpVersion
if ($ver) {
    Write-Host "[CDP] Already running on port $Port ($($ver.Browser)). Skipping launch."
} else {
    if (-not (Test-Path $ChromePath)) { throw "Chrome not found: $ChromePath" }

    # Stale instance: window closed but a chrome.exe still holds this profile without CDP.
    # Chrome's single-instance lock would hand our launch to it and the new process would exit.
    # Only processes using THIS agent profile are stopped (never the owner's or Jinus' Chrome).
    $profileNorm = $ProfileDir.TrimEnd('\').ToLower()
    $stale = Get-CimInstance Win32_Process -Filter "Name='chrome.exe'" |
             Where-Object { $_.CommandLine -and $_.CommandLine.ToLower().Replace('"','').Contains("--user-data-dir=$profileNorm") }
    if ($stale) {
        Write-Host "[CDP] Stale Chrome holding agent profile without CDP: PIDs $(($stale.ProcessId) -join ', '). Stopping them."
        $stale | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        $wait = (Get-Date).AddSeconds(10)
        while ((Get-CimInstance Win32_Process -Filter "Name='chrome.exe'" |
                Where-Object { $_.CommandLine -and $_.CommandLine.ToLower().Replace('"','').Contains("--user-data-dir=$profileNorm") }) -and (Get-Date) -lt $wait) {
            Start-Sleep -Milliseconds 300
        }
    }
    $profArg = if ($ProfileDirectory) { "--profile-directory=`"$ProfileDirectory`" " } else { "" }
    $cmd = "`"$ChromePath`" --remote-debugging-port=$Port --user-data-dir=`"$ProfileDir`" $profArg" +
           "--new-window --window-position=$Left,$Top --window-size=$Width,$Height " +
           "--no-first-run --no-default-browser-check `"$Url`""
    $r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $cmd }
    if ($r.ReturnValue -ne 0) { throw "Win32_Process.Create failed, ReturnValue=$($r.ReturnValue)" }
    Write-Host "[CDP] Chrome started detached. PID=$($r.ProcessId)"

    $deadline = (Get-Date).AddSeconds($StartTimeoutSec)
    while (-not ($ver = Get-CdpVersion)) {
        if ((Get-Date) -gt $deadline) { throw "CDP did not answer on port $Port within $StartTimeoutSec s." }
        Start-Sleep -Milliseconds 500
    }
    Write-Host "[CDP] Up: $($ver.Browser)"
}

# ---------- 2) Pick a page target (open one if none) ----------
$page = (Invoke-RestMethod -Uri "$base/json" -TimeoutSec 5) | Where-Object { $_.type -eq 'page' } | Select-Object -First 1
if (-not $page) {
    $page = Invoke-RestMethod -Method Put -Uri "$base/json/new?$Url" -TimeoutSec 5
}
elseif ($Url -ne 'about:blank' -and $page.url -ne $Url -and $ver) {
    # Already-running case with an explicit URL: open it in the existing tab
    $navigate = $true
}

# ---------- 3) CDP: restore window to a visible normal state ----------
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$ws.ConnectAsync([Uri]$ver.webSocketDebuggerUrl, [Threading.CancellationToken]::None).Wait()
$script:msgId = 0
function Invoke-Cdp([string]$Method, [hashtable]$Params = @{}) {
    $script:msgId++
    $json  = @{ id = $script:msgId; method = $Method; params = $Params } | ConvertTo-Json -Depth 6 -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($json)
    $ws.SendAsync([ArraySegment[byte]]$bytes, 'Text', $true, [Threading.CancellationToken]::None).Wait()
    $cts = New-Object Threading.CancellationTokenSource 10000   # 10 s guard against hangs
    while ($true) {
        $buf = New-Object byte[] 65536; $sb = New-Object Text.StringBuilder
        do {
            $res = $ws.ReceiveAsync([ArraySegment[byte]]$buf, $cts.Token).Result
            [void]$sb.Append([Text.Encoding]::UTF8.GetString($buf, 0, $res.Count))
        } while (-not $res.EndOfMessage)
        $obj = $sb.ToString() | ConvertFrom-Json
        if ($obj.id -eq $script:msgId) {
            if ($obj.error) { throw "CDP $Method error: $($obj.error.message)" }
            return $obj.result
        }
    }
}

try {
    $win = Invoke-Cdp 'Browser.getWindowForTarget' @{ targetId = $page.id }
    Write-Host "[CDP] Before: state=$($win.bounds.windowState) pos=$($win.bounds.left),$($win.bounds.top) size=$($win.bounds.width)x$($win.bounds.height)"

    # windowState must be 'normal' before setting position/size
    [void](Invoke-Cdp 'Browser.setWindowBounds' @{ windowId = $win.windowId; bounds = @{ windowState = 'normal' } })
    [void](Invoke-Cdp 'Browser.setWindowBounds' @{ windowId = $win.windowId; bounds = @{ left = $Left; top = $Top; width = $Width; height = $Height } })
    [void](Invoke-Cdp 'Target.activateTarget' @{ targetId = $page.id })

    $after = Invoke-Cdp 'Browser.getWindowForTarget' @{ targetId = $page.id }
    Write-Host "[CDP] After:  state=$($after.bounds.windowState) pos=$($after.bounds.left),$($after.bounds.top) size=$($after.bounds.width)x$($after.bounds.height)"

    if ($navigate) {
        # Open requested URL in a new tab, then close the old one (single-tab rule)
        $null = Invoke-RestMethod -Method Put -Uri "$base/json/new?$Url" -TimeoutSec 5
        Invoke-RestMethod -Uri "$base/json/close/$($page.id)" -TimeoutSec 5 | Out-Null
        Write-Host "[CDP] Opened: $Url"
    }
}
finally {
    $ws.Dispose()
}
Write-Host "[CDP] Ready -> $base  (ws: $($ver.webSocketDebuggerUrl))"
