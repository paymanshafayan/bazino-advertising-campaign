<#
    Bazino Bridge
    -------------
    Connects the Bazino sandbox agent to the owner's real Chrome.

    Transport: a dedicated orphan branch in the private GitHub repo acts as a
    message bus. The agent pushes commands, this app polls for them, executes
    them against Chrome over CDP, and pushes the results back.

    Why GitHub: the sandbox reaches api.github.com, and so does the owner's
    network without a VPN. The old sbx-*.arena.site host is blocked locally,
    which is the only reason a VPN was ever needed.

    Run with -SelfTest to load every function and exit - used by CI.
#>

param(
    [switch]$SelfTest
)

# ps2exe does not reliably bind switch parameters in a -noConsole build, so the
# self-test is also detectable from the raw command line and an env var. CI sets
# BAZINO_SELFTEST=1, which needs no parameter binding at all.
if (-not $SelfTest) {
    if ($env:BAZINO_SELFTEST -eq '1') { $SelfTest = $true }
    elseif ([Environment]::GetCommandLineArgs() -contains '-SelfTest') { $SelfTest = $true }
}

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# ============================================================ configuration

$script:Cfg = @{
    Owner        = 'paymanshafayan'
    Repo         = 'bazino-advertising-campaign'
    BusBranch    = 'cdp-bus'
    ChromePort   = 9333
    ProfileDir   = Join-Path $env:USERPROFILE 'chrome-bazino'
    PollMs       = 2000
    MaxRetries   = 5
    SettingsPath = Join-Path $env:APPDATA 'BazinoBridge\settings.json'
}

$script:State = @{
    Running      = $false
    GithubOk     = $false
    ChromeOk     = $false
    BridgeUp     = $false
    Sent         = 0
    Received     = 0
    Errors       = 0
    ChromeWs     = $null
    Socket       = $null
    LastSeq      = 0
    Token        = $null
    ConsecFails  = 0
    ChromeVer    = ''
}

# ============================================================ logging

$script:LogLines = New-Object System.Collections.ArrayList

function Write-Log {
    param(
        [string]$Message,
        [ValidateSet('info','ok','warn','error')]
        [string]$Level = 'info'
    )
    $stamp = (Get-Date).ToString('HH:mm:ss')
    $icon  = switch ($Level) {
        'ok'    { 'OK  ' }
        'warn'  { 'WARN' }
        'error' { 'ERR ' }
        default { '    ' }
    }
    $line = "$stamp  $icon  $Message"
    [void]$script:LogLines.Add($line)

    if ($script:UI -and $script:UI.LogBox) {
        $script:UI.LogBox.AppendText($line + [Environment]::NewLine)
    } else {
        Write-Host $line
    }
}

# ============================================================ settings

function Get-Settings {
    if (Test-Path $script:Cfg.SettingsPath) {
        try {
            return Get-Content $script:Cfg.SettingsPath -Raw | ConvertFrom-Json
        } catch {
            Write-Log "Settings file unreadable, starting fresh" 'warn'
        }
    }
    return $null
}

function Save-Token {
    param([string]$Token)
    $dir = Split-Path $script:Cfg.SettingsPath -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

    # DPAPI: only this Windows user on this machine can read it back.
    $secure    = ConvertTo-SecureString $Token -AsPlainText -Force
    $encrypted = ConvertFrom-SecureString $secure

    @{ token = $encrypted } | ConvertTo-Json |
        Set-Content $script:Cfg.SettingsPath -Encoding UTF8
    Write-Log "Token saved (encrypted for this Windows account)" 'ok'
}

function Read-Token {
    $s = Get-Settings
    if (-not $s -or -not $s.token) { return $null }
    try {
        $secure = ConvertTo-SecureString $s.token
        $bstr   = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
        try   { return [Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr) }
        finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
    } catch {
        Write-Log "Stored token could not be decrypted - re-enter it" 'error'
        return $null
    }
}

# ============================================================ github bus

function Invoke-GitHub {
    param(
        [string]$Path,
        [string]$Method = 'GET',
        [object]$Body   = $null
    )
    $headers = @{
        Authorization          = "Bearer $($script:State.Token)"
        Accept                 = 'application/vnd.github+json'
        'X-GitHub-Api-Version' = '2022-11-28'
        'User-Agent'           = 'BazinoBridge/1.0'
    }
    $uri = "https://api.github.com/repos/$($script:Cfg.Owner)/$($script:Cfg.Repo)$Path"
    $p   = @{ Uri = $uri; Headers = $headers; Method = $Method; TimeoutSec = 30 }
    if ($Body) {
        $p['Body']        = ($Body | ConvertTo-Json -Depth 10 -Compress)
        $p['ContentType'] = 'application/json'
    }
    return Invoke-RestMethod @p
}

function Test-GitHub {
    try {
        $r = Invoke-GitHub -Path ''
        $script:State.GithubOk = $true
        Write-Log "GitHub reachable - repo is $(if ($r.private) {'private'} else {'PUBLIC'})" 'ok'
        if (-not $r.private) {
            Write-Log "Repo is public - browser commands would be visible. Stop and make it private." 'error'
            return $false
        }
        return $true
    } catch {
        $script:State.GithubOk = $false
        Write-Log "GitHub unreachable: $($_.Exception.Message)" 'error'
        return $false
    }
}

function Get-BusCommands {
    # Returns command files newer than the last processed sequence number.
    try {
        $items = Invoke-GitHub -Path "/contents/cmd?ref=$($script:Cfg.BusBranch)"
    } catch {
        if ($_.Exception.Response.StatusCode.value__ -eq 404) { return @() }  # empty bus
        throw
    }

    $out = @()
    foreach ($f in $items) {
        if ($f.name -notmatch '^(\d+)\.json$') { continue }
        $seq = [int]$Matches[1]
        if ($seq -le $script:State.LastSeq) { continue }
        $body = Invoke-GitHub -Path "/contents/cmd/$($f.name)?ref=$($script:Cfg.BusBranch)"
        $json = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($body.content))
        $out += [pscustomobject]@{ Seq = $seq; Payload = $json }
    }
    return $out | Sort-Object Seq
}

function Push-BusResult {
    param([int]$Seq, [string]$Json)
    $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($Json))
    $path    = "res/$Seq.json"

    $sha = $null
    try {
        $existing = Invoke-GitHub -Path "/contents/$path`?ref=$($script:Cfg.BusBranch)"
        $sha = $existing.sha
    } catch { }   # not there yet, which is the normal case

    $body = @{
        message = "res $Seq"
        content = $encoded
        branch  = $script:Cfg.BusBranch
    }
    if ($sha) { $body['sha'] = $sha }

    Invoke-GitHub -Path "/contents/$path" -Method 'PUT' -Body $body | Out-Null
    $script:State.Sent++
}

# ============================================================ chrome

function Find-ChromePath {
    $candidates = @(
        "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
        "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
        "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }

    try {
        $reg = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe' -ErrorAction Stop
        if ($reg.'(default)' -and (Test-Path $reg.'(default)')) { return $reg.'(default)' }
    } catch { }

    return $null
}

function Test-ChromeDebug {
    try {
        $v = Invoke-RestMethod "http://127.0.0.1:$($script:Cfg.ChromePort)/json/version" -TimeoutSec 4
        $script:State.ChromeWs  = $v.webSocketDebuggerUrl
        $script:State.ChromeVer = $v.Browser
        $script:State.ChromeOk  = $true
        return $true
    } catch {
        $script:State.ChromeOk = $false
        return $false
    }
}

function Test-ProfileInUse {
    # True when a Chrome process is already running against our profile folder.
    # Chrome keeps a lock file there for the lifetime of the instance.
    $lock = Join-Path $script:Cfg.ProfileDir 'lockfile'
    if (Test-Path $lock) { return $true }

    try {
        $esc = [regex]::Escape($script:Cfg.ProfileDir)
        $hit = Get-CimInstance Win32_Process -Filter "Name='chrome.exe'" -ErrorAction Stop |
               Where-Object { $_.CommandLine -and $_.CommandLine -match $esc }
        return [bool]$hit
    } catch {
        return $false
    }
}

function Start-AgentChrome {
    # Never open a second window against the same profile: Chrome would either
    # refuse the lock or hand the tabs to the existing instance, and the owner
    # would have to sign in to Google again.

    if (Test-ChromeDebug) {
        Write-Log "Chrome is already listening - reusing it" 'ok'
        return $true
    }

    if (Test-ProfileInUse) {
        Write-Log "A Chrome instance already owns this profile - waiting for its debug port" 'warn'
        for ($i = 1; $i -le 15; $i++) {
            Start-Sleep -Milliseconds 700
            if (Test-ChromeDebug) {
                Write-Log "Chrome ready - $($script:State.ChromeVer)" 'ok'
                return $true
            }
        }
        Write-Log "That Chrome was started without the debug flag." 'error'
        Write-Log "Close every Chrome window using the Bazino profile, then press Connect again." 'error'
        return $false
    }

    $exe = Find-ChromePath
    if (-not $exe) {
        Write-Log "Chrome not found on this machine" 'error'
        return $false
    }

    $firstRun = -not (Test-Path (Join-Path $script:Cfg.ProfileDir 'Default'))
    if ($firstRun) {
        Write-Log "First run - a fresh profile is being created" 'info'
        Write-Log "Sign in to Bazinopro@gmail.com in that window. It is remembered from then on." 'warn'
    }
    Write-Log "Launching Chrome with the debug port open" 'info'

    # Same flags every time, so the profile stays valid and the login persists.
    $args = @(
        "--remote-debugging-port=$($script:Cfg.ChromePort)"
        "--remote-allow-origins=*"
        "--user-data-dir=`"$($script:Cfg.ProfileDir)`""
        "--no-first-run"
        "--no-default-browser-check"
        "--restore-last-session"
    )
    if ($firstRun) { $args += 'https://accounts.google.com' } else { $args += 'about:blank' }

    Start-Process -FilePath $exe -ArgumentList $args | Out-Null

    for ($i = 1; $i -le 20; $i++) {
        Start-Sleep -Milliseconds 700
        if (Test-ChromeDebug) {
            Write-Log "Chrome ready - $($script:State.ChromeVer)" 'ok'
            return $true
        }
    }
    Write-Log "Chrome did not open its debug port within 14 s" 'error'
    return $false
}

function Connect-ChromeSocket {
    try {
        if ($script:State.Socket) {
            try { $script:State.Socket.Dispose() } catch { }
        }
        $ws = [System.Net.WebSockets.ClientWebSocket]::new()
        $ws.Options.KeepAliveInterval = [TimeSpan]::FromSeconds(20)
        $ct = [Threading.CancellationToken]::None
        [void]$ws.ConnectAsync([Uri]$script:State.ChromeWs, $ct).GetAwaiter().GetResult()
        $script:State.Socket = $ws
        Write-Log "CDP socket open" 'ok'
        return $true
    } catch {
        Write-Log "CDP socket failed: $($_.Exception.Message)" 'error'
        return $false
    }
}

function Send-Cdp {
    param([string]$Json)
    $ct    = [Threading.CancellationToken]::None
    $bytes = [Text.Encoding]::UTF8.GetBytes($Json)
    $seg   = [ArraySegment[byte]]::new($bytes)
    [void]$script:State.Socket.SendAsync($seg, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $ct).GetAwaiter().GetResult()
}

function Clear-CdpBacklog {
    # Page.navigate and friends make Chrome emit a burst of events. Anything
    # still buffered belongs to an earlier command, so drop it before sending
    # the next one - otherwise the backlog grows and every call gets slower.
    $dropped = 0
    while ($script:State.Socket -and
           $script:State.Socket.State -eq [System.Net.WebSockets.WebSocketState]::Open) {
        try {
            $frame = Receive-CdpFrame -TimeoutMs 120
            if (-not $frame) { break }
            $dropped++
            if ($dropped -gt 500) { break }   # pathological burst, stop draining
        } catch {
            break   # nothing waiting, which is the normal exit
        }
    }
    if ($dropped -gt 0) { Write-Log "Dropped $dropped stale CDP frames" 'info' }
}

function Receive-CdpFrame {
    # Reads exactly one complete websocket message.
    param([int]$TimeoutMs = 30000)
    $ct     = [Threading.CancellationToken]::None
    $buffer = [byte[]]::new(1048576)
    $ms     = New-Object System.IO.MemoryStream
    $sw     = [Diagnostics.Stopwatch]::StartNew()

    do {
        if ($sw.ElapsedMilliseconds -gt $TimeoutMs) { throw "CDP receive timed out" }
        $seg = [ArraySegment[byte]]::new($buffer)
        $res = $script:State.Socket.ReceiveAsync($seg, $ct).GetAwaiter().GetResult()
        if ($res.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Close) {
            throw "Chrome closed the CDP socket"
        }
        $ms.Write($buffer, 0, $res.Count)
    } while (-not $res.EndOfMessage)

    return [Text.Encoding]::UTF8.GetString($ms.ToArray())
}

function Receive-Cdp {
    # Returns the reply whose id matches $ExpectId. CDP interleaves unsolicited
    # events with command replies, so returning the next frame that arrives
    # hands back the wrong payload. Events are logged and skipped.
    param(
        [int]$TimeoutMs = 30000,
        [int]$ExpectId  = -1
    )
    $sw = [Diagnostics.Stopwatch]::StartNew()

    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
        $remaining = $TimeoutMs - $sw.ElapsedMilliseconds
        $frame = Receive-CdpFrame -TimeoutMs $remaining

        if ($ExpectId -lt 0) { return $frame }

        $id = $null
        try { $id = ($frame | ConvertFrom-Json).id } catch { }

        if ($null -eq $id) { continue }          # event, not a reply
        if ([int]$id -eq $ExpectId) { return $frame }
        # a stale reply from an earlier command - drop it and keep waiting
    }
    throw "No CDP reply with id $ExpectId within $TimeoutMs ms"
}

# ============================================================ recovery

function Repair-Bridge {
    param([string]$Reason)

    $script:State.ConsecFails++
    Write-Log "Recovering ($($script:State.ConsecFails)/$($script:Cfg.MaxRetries)): $Reason" 'warn'

    if ($script:State.ConsecFails -gt $script:Cfg.MaxRetries) {
        Write-Log "Too many consecutive failures - stopping. Press Connect to retry." 'error'
        Stop-Bridge
        return $false
    }

    $backoff = [Math]::Min([Math]::Pow(2, $script:State.ConsecFails), 16)
    Write-Log "Waiting $backoff s before retry" 'info'
    Start-Sleep -Seconds $backoff

    if (-not (Test-ChromeDebug)) {
        Write-Log "Chrome is gone - reconnecting" 'warn'
        if (-not (Start-AgentChrome)) { return $false }
    }
    if (-not (Connect-ChromeSocket)) { return $false }

    Write-Log "Recovered" 'ok'
    return $true
}

# ============================================================ main loop

function Invoke-BridgeCycle {
    # One poll: fetch pending commands, run them, push results back.
    try {
        $cmds = Get-BusCommands
    } catch {
        return (Repair-Bridge "bus read failed: $($_.Exception.Message)")
    }

    foreach ($c in $cmds) {
        try {
            $wantId = -1
            $method = ''
            try {
                $parsed = $c.Payload | ConvertFrom-Json
                $wantId = [int]$parsed.id
                $method = [string]$parsed.method
            } catch { }

            Clear-CdpBacklog

            # Navigation keeps Chrome busy far longer than a normal call.
            $limit = if ($method -like 'Page.navigate*') { 45000 } else { 25000 }

            Send-Cdp -Json $c.Payload
            $reply = Receive-Cdp -ExpectId $wantId -TimeoutMs $limit
            Push-BusResult -Seq $c.Seq -Json $reply
            $script:State.LastSeq = $c.Seq
            $script:State.Received++
            $script:State.ConsecFails = 0

            if (-not $method) { $method = 'unknown' }
            Write-Log "seq $($c.Seq)  ->  $method" 'info'
        } catch {
            $script:State.Errors++
            if (-not (Repair-Bridge "command $($c.Seq) failed: $($_.Exception.Message)")) {
                return $false
            }
        }
    }
    return $true
}

function Start-Bridge {
    if ($script:State.Running) { return }

    $script:State.Token = Read-Token
    if (-not $script:State.Token) {
        Write-Log "No GitHub token saved. Open Settings and add one." 'error'
        Show-Settings
        return
    }

    Write-Log "Starting bridge" 'info'
    if (-not (Test-GitHub))  { return }
    # Start-AgentChrome reuses a live instance and never opens a duplicate.
    if (-not (Start-AgentChrome)) { return }
    if (-not (Connect-ChromeSocket)) { return }

    $script:State.Running    = $true
    $script:State.BridgeUp   = $true
    $script:State.ConsecFails = 0
    Write-Log "Bridge established" 'ok'

    $script:UI.Timer.Interval = $script:Cfg.PollMs
    $script:UI.Timer.Start()
    Update-Status
}

function Stop-Bridge {
    if ($script:UI -and $script:UI.Timer) { $script:UI.Timer.Stop() }
    if ($script:State.Socket) {
        try { $script:State.Socket.Dispose() } catch { }
        $script:State.Socket = $null
    }
    $script:State.Running  = $false
    $script:State.BridgeUp = $false
    Write-Log "Bridge stopped" 'info'
    Update-Status
}

# ============================================================ ui

function New-StatusRow {
    param([Windows.Forms.Panel]$Parent, [string]$Label, [int]$Top)

    $dot = New-Object Windows.Forms.Label
    $dot.Text      = [char]0x25CF
    $dot.Font      = New-Object Drawing.Font('Segoe UI', 12)
    $dot.ForeColor = [Drawing.Color]::FromArgb(120,120,120)
    $dot.Location  = New-Object Drawing.Point(12, $Top)
    $dot.Size      = New-Object Drawing.Size(18, 22)
    $Parent.Controls.Add($dot)

    $name = New-Object Windows.Forms.Label
    $name.Text      = $Label
    $name.Font      = New-Object Drawing.Font('Segoe UI', 9)
    $name.ForeColor = [Drawing.Color]::FromArgb(210,210,210)
    $name.Location  = New-Object Drawing.Point(34, ($Top + 3))
    $name.Size      = New-Object Drawing.Size(70, 20)
    $Parent.Controls.Add($name)

    $val = New-Object Windows.Forms.Label
    $val.Text      = '-'
    $val.Font      = New-Object Drawing.Font('Consolas', 9)
    $val.ForeColor = [Drawing.Color]::FromArgb(160,160,160)
    $val.Location  = New-Object Drawing.Point(110, ($Top + 3))
    $val.Size      = New-Object Drawing.Size(330, 20)
    $Parent.Controls.Add($val)

    return @{ Dot = $dot; Value = $val }
}

function Update-Status {
    if (-not $script:UI) { return }

    $green = [Drawing.Color]::FromArgb(80,200,120)
    $red   = [Drawing.Color]::FromArgb(220,90,90)
    $grey  = [Drawing.Color]::FromArgb(120,120,120)

    $script:UI.GhRow.Dot.ForeColor   = if ($script:State.GithubOk) { $green } else { $red }
    $script:UI.GhRow.Value.Text      = if ($script:State.GithubOk) { 'connected' } else { 'not connected' }

    $script:UI.ChromeRow.Dot.ForeColor = if ($script:State.ChromeOk) { $green } else { $red }
    $script:UI.ChromeRow.Value.Text    = if ($script:State.ChromeOk) { $script:State.ChromeVer } else { 'not connected' }

    $script:UI.BridgeRow.Dot.ForeColor = if ($script:State.BridgeUp) { $green } else { $grey }
    $script:UI.BridgeRow.Value.Text    = if ($script:State.BridgeUp) {
        "active   up $($script:State.Sent)  down $($script:State.Received)  err $($script:State.Errors)"
    } else { 'idle' }

    $script:UI.BtnConnect.Enabled = -not $script:State.Running
    $script:UI.BtnStop.Enabled    = $script:State.Running
}

function Show-Settings {
    $f = New-Object Windows.Forms.Form
    $f.Text          = 'Settings'
    $f.Size          = New-Object Drawing.Size(560, 260)
    $f.StartPosition = 'CenterParent'
    $f.BackColor     = [Drawing.Color]::FromArgb(30,30,38)
    $f.FormBorderStyle = 'FixedDialog'
    $f.MaximizeBox   = $false

    $lbl = New-Object Windows.Forms.Label
    $lbl.Text      = "GitHub token (fine-grained, Contents: Read and write on this repo only)"
    $lbl.ForeColor = [Drawing.Color]::FromArgb(210,210,210)
    $lbl.Location  = New-Object Drawing.Point(16, 18)
    $lbl.Size      = New-Object Drawing.Size(510, 20)
    $f.Controls.Add($lbl)

    $box = New-Object Windows.Forms.TextBox
    $box.Location     = New-Object Drawing.Point(16, 44)
    $box.Size         = New-Object Drawing.Size(510, 26)
    $box.Font         = New-Object Drawing.Font('Consolas', 9)
    $box.UseSystemPasswordChar = $true
    $f.Controls.Add($box)

    $hint = New-Object Windows.Forms.Label
    $hint.Text      = "github.com  ->  Settings  ->  Developer settings  ->  Personal access tokens" +
                      [Environment]::NewLine +
                      "->  Fine-grained tokens  ->  Generate new token" + [Environment]::NewLine +
                      "Repository access: only bazino-advertising-campaign" + [Environment]::NewLine +
                      "Permissions: Contents = Read and write"
    $hint.ForeColor = [Drawing.Color]::FromArgb(150,150,150)
    $hint.Font      = New-Object Drawing.Font('Segoe UI', 8)
    $hint.Location  = New-Object Drawing.Point(16, 80)
    $hint.Size      = New-Object Drawing.Size(510, 70)
    $f.Controls.Add($hint)

    $save = New-Object Windows.Forms.Button
    $save.Text      = 'Save'
    $save.Location  = New-Object Drawing.Point(410, 165)
    $save.Size      = New-Object Drawing.Size(116, 34)
    $save.FlatStyle = 'Flat'
    $save.BackColor = [Drawing.Color]::FromArgb(70,130,200)
    $save.ForeColor = [Drawing.Color]::White
    $save.Add_Click({
        if ($box.Text.Trim()) {
            Save-Token -Token $box.Text.Trim()
            $script:State.Token = $box.Text.Trim()
            $f.Close()
        }
    })
    $f.Controls.Add($save)

    [void]$f.ShowDialog()
}

function New-MainWindow {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    [Windows.Forms.Application]::EnableVisualStyles()

    $bg   = [Drawing.Color]::FromArgb(24,24,32)
    $card = [Drawing.Color]::FromArgb(34,34,44)

    $form = New-Object Windows.Forms.Form
    $form.Text          = 'Bazino Bridge'
    $form.Size          = New-Object Drawing.Size(720, 640)
    $form.StartPosition = 'CenterScreen'
    $form.BackColor     = $bg
    $form.MinimumSize   = New-Object Drawing.Size(600, 520)

    # --- header ---
    $title = New-Object Windows.Forms.Label
    $title.Text      = 'Bazino Bridge'
    $title.Font      = New-Object Drawing.Font('Segoe UI', 15, [Drawing.FontStyle]::Bold)
    $title.ForeColor = [Drawing.Color]::FromArgb(240,240,240)
    $title.Location  = New-Object Drawing.Point(20, 16)
    $title.Size      = New-Object Drawing.Size(300, 32)
    $form.Controls.Add($title)

    $btnCfg = New-Object Windows.Forms.Button
    $btnCfg.Text      = 'Settings'
    $btnCfg.Location  = New-Object Drawing.Point(590, 18)
    $btnCfg.Size      = New-Object Drawing.Size(96, 30)
    $btnCfg.FlatStyle = 'Flat'
    $btnCfg.BackColor = $card
    $btnCfg.ForeColor = [Drawing.Color]::FromArgb(200,200,200)
    $btnCfg.Anchor    = 'Top,Right'
    $btnCfg.Add_Click({ Show-Settings })
    $form.Controls.Add($btnCfg)

    # --- buttons ---
    $btnGo = New-Object Windows.Forms.Button
    $btnGo.Text      = 'Connect'
    $btnGo.Location  = New-Object Drawing.Point(20, 64)
    $btnGo.Size      = New-Object Drawing.Size(200, 52)
    $btnGo.FlatStyle = 'Flat'
    $btnGo.BackColor = [Drawing.Color]::FromArgb(60,150,90)
    $btnGo.ForeColor = [Drawing.Color]::White
    $btnGo.Font      = New-Object Drawing.Font('Segoe UI', 11, [Drawing.FontStyle]::Bold)
    $btnGo.Add_Click({ Start-Bridge })
    $form.Controls.Add($btnGo)

    $btnStop = New-Object Windows.Forms.Button
    $btnStop.Text      = 'Disconnect'
    $btnStop.Location  = New-Object Drawing.Point(232, 64)
    $btnStop.Size      = New-Object Drawing.Size(150, 52)
    $btnStop.FlatStyle = 'Flat'
    $btnStop.BackColor = [Drawing.Color]::FromArgb(150,60,60)
    $btnStop.ForeColor = [Drawing.Color]::White
    $btnStop.Font      = New-Object Drawing.Font('Segoe UI', 10)
    $btnStop.Enabled   = $false
    $btnStop.Add_Click({ Stop-Bridge })
    $form.Controls.Add($btnStop)

    # --- status card ---
    $status = New-Object Windows.Forms.Panel
    $status.Location  = New-Object Drawing.Point(20, 132)
    $status.Size      = New-Object Drawing.Size(666, 104)
    $status.BackColor = $card
    $status.Anchor    = 'Top,Left,Right'
    $form.Controls.Add($status)

    $ghRow     = New-StatusRow -Parent $status -Label 'GitHub' -Top 10
    $chromeRow = New-StatusRow -Parent $status -Label 'Chrome' -Top 40
    $bridgeRow = New-StatusRow -Parent $status -Label 'Bridge' -Top 70

    # --- log ---
    $logLbl = New-Object Windows.Forms.Label
    $logLbl.Text      = 'Log'
    $logLbl.ForeColor = [Drawing.Color]::FromArgb(180,180,180)
    $logLbl.Location  = New-Object Drawing.Point(20, 248)
    $logLbl.Size      = New-Object Drawing.Size(60, 20)
    $form.Controls.Add($logLbl)

    $btnCopy = New-Object Windows.Forms.Button
    $btnCopy.Text      = 'Copy log'
    $btnCopy.Location  = New-Object Drawing.Point(586, 244)
    $btnCopy.Size      = New-Object Drawing.Size(100, 28)
    $btnCopy.FlatStyle = 'Flat'
    $btnCopy.BackColor = $card
    $btnCopy.ForeColor = [Drawing.Color]::FromArgb(200,200,200)
    $btnCopy.Anchor    = 'Top,Right'
    $btnCopy.Add_Click({
        if ($script:LogLines.Count -gt 0) {
            [Windows.Forms.Clipboard]::SetText(($script:LogLines -join [Environment]::NewLine))
            Write-Log "Log copied to clipboard" 'ok'
        }
    })
    $form.Controls.Add($btnCopy)

    $log = New-Object Windows.Forms.TextBox
    $log.Multiline  = $true
    $log.ScrollBars = 'Vertical'
    $log.ReadOnly   = $true
    $log.Location   = New-Object Drawing.Point(20, 278)
    $log.Size       = New-Object Drawing.Size(666, 300)
    $log.BackColor  = [Drawing.Color]::FromArgb(18,18,24)
    $log.ForeColor  = [Drawing.Color]::FromArgb(190,190,190)
    $log.Font       = New-Object Drawing.Font('Consolas', 9)
    $log.Anchor     = 'Top,Bottom,Left,Right'
    $form.Controls.Add($log)

    # --- poll timer ---
    $timer = New-Object Windows.Forms.Timer
    $timer.Interval = $script:Cfg.PollMs
    $timer.Add_Tick({
        if ($script:State.Running) {
            [void](Invoke-BridgeCycle)
            Update-Status
        }
    })

    $form.Add_FormClosing({ Stop-Bridge })

    $script:UI = @{
        Form       = $form
        LogBox     = $log
        BtnConnect = $btnGo
        BtnStop    = $btnStop
        GhRow      = $ghRow
        ChromeRow  = $chromeRow
        BridgeRow  = $bridgeRow
        Timer      = $timer
    }
    return $form
}

# ============================================================ entry point

# Hard exit. In a ps2exe -noConsole build neither `exit` nor
# [Environment]::Exit reliably terminates the host once WinForms assemblies are
# loaded, so the last resort is killing our own process.
function Stop-SelfTest {
    param([int]$Code)
    try { [Console]::Out.Flush() } catch { }
    try { [Environment]::Exit($Code) } catch { }
    try {
        $p = Get-Process -Id $PID -ErrorAction Stop
        if ($Code -eq 0) { $p.Kill() } else { $p.Kill() }
    } catch { }
    exit $Code
}

if ($SelfTest) {
    # CI path: prove every function parses and the basic ones behave.
    #
    # Packaged with -noConsole there is no stdout, so Write-Host can block and
    # the process never exits. Everything goes to a file instead, and we leave
    # with [Environment]::Exit, which terminates a WinForms host reliably
    # (plain `exit` does not).
    $selfTestLog = Join-Path ([IO.Path]::GetTempPath()) 'bazino-selftest.log'
    function Write-Test {
        param([string]$Text)
        Add-Content -Path $selfTestLog -Value $Text -Encoding UTF8
        try { Write-Host $Text } catch { }   # console may not exist
    }
    Set-Content -Path $selfTestLog -Value '' -Encoding UTF8

    Write-Test "Bazino Bridge self-test"
    $required = @(
        'Write-Log','Get-Settings','Save-Token','Read-Token','Invoke-GitHub',
        'Test-GitHub','Get-BusCommands','Push-BusResult','Find-ChromePath',
        'Test-ChromeDebug','Test-ProfileInUse','Start-AgentChrome','Connect-ChromeSocket','Send-Cdp',
        'Clear-CdpBacklog','Receive-CdpFrame','Receive-Cdp','Repair-Bridge','Invoke-BridgeCycle','Start-Bridge',
        'Stop-Bridge','New-StatusRow','Update-Status','Show-Settings','New-MainWindow'
    )
    $missing = @()
    foreach ($fn in $required) {
        if (-not (Get-Command $fn -ErrorAction SilentlyContinue)) { $missing += $fn }
    }
    if ($missing.Count -gt 0) {
        Write-Test "MISSING: $($missing -join ', ')"
        Stop-SelfTest 1
    }
    Write-Test "All $($required.Count) functions defined"

    Write-Log "logger works" 'ok'
    if ($script:LogLines.Count -ne 1) { Write-Test "Logger failed"; exit 1 }

    $chrome = Find-ChromePath
    Write-Test "Chrome path: $(if ($chrome) { $chrome } else { 'not installed (fine on CI)' })"

    # Only probe WinForms when running as a plain script. Inside the packaged
    # exe these assemblies are already loaded, and touching them here starts a
    # message pump that keeps the process alive after the test finishes.
    if (-not $env:BAZINO_SELFTEST) {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction SilentlyContinue
        Add-Type -AssemblyName System.Drawing -ErrorAction SilentlyContinue
        Write-Test "WinForms assemblies load"
    } else {
        Write-Test "WinForms probe skipped (packaged exe)"
    }

    Write-Test "SELF-TEST PASSED"
    Stop-SelfTest 0
}

$form = New-MainWindow
Write-Log "Bazino Bridge ready" 'info'
Write-Log "Transport: GitHub branch '$($script:Cfg.BusBranch)' - no VPN needed" 'info'
if (-not (Read-Token)) {
    Write-Log "No GitHub token yet - open Settings first" 'warn'
}
Update-Status
[void]$form.ShowDialog()
