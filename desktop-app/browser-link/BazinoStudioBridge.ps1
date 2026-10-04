# ============================================================================
#  BazinoStudioBridge.ps1 - session-branch browser control adapter
#  Derived from tested BazinoBridge.ps1 at portal reference commit 843cfb3.
#  THIS version only reads/writes the session branch and marketing-browser-bus/.
#  Reuses the owner's existing DPAPI token and Chrome profile 9334.
#  No credentials/cookies/auth codes may be sent through this unencrypted bus.
#  Run for a short owner-approved setup task, then Disconnect/close.
#  Repo: paymanshafayan/bazino-gamenet-portal   (branch: arena/01a0d4ee-bazino-gamenet-portal)
#
#  Architecture (derived from browser-bridge-build-manual-fa.md):
#    agent --git push--> SESSION BRANCH / marketing-browser-bus/cmd/<n>.json
#    this app polls every 2 s, runs CDP on the owner's dedicated Chrome,
#    and writes res/<n>.json on that SAME session branch (Contents API).
#
#  PowerShell 5.1 compatible (also runs under pwsh 7).
#  Self-test (no GUI, no network, no Chrome):
#    powershell -NoProfile -ExecutionPolicy Bypass -File BazinoStudioBridge.ps1 -SelfTest
#    (also detected via BAZINO_SELFTEST=1 or a raw '-SelfTest' command line)
# ============================================================================

param([switch]$SelfTest)

# --- self-test detection: ps2exe does not reliably bind switches (bug 2) ---
if (-not $SelfTest) {
    if ($env:BAZINO_SELFTEST -eq '1') { $SelfTest = $true }
    elseif ([Environment]::GetCommandLineArgs() -contains '-SelfTest') { $SelfTest = $true }
}

# ---------------------------------------------------------------------------
# configuration
# ---------------------------------------------------------------------------
$script:Cfg = @{
    Owner        = 'paymanshafayan'
    Repo         = 'bazino-gamenet-portal'
    BusBranch    = 'arena/01a0d4ee-bazino-gamenet-portal'
    BusRoot      = 'marketing-browser-bus'
    # Dedicated agent Chrome: own port + own profile, so this bridge never
    # clashes with the advertising-campaign bridge (port 9333 / chrome-bazino).
    # First Connect opens a brand-new Chrome instance; login once, it persists.
    ChromePort   = 9334
    ProfileDir   = Join-Path $env:USERPROFILE 'chrome-bazino-portal'
    PollMs       = 2000
    MaxRetries   = 5
    SettingsPath = Join-Path $env:APPDATA 'BazinoBridge\settings.json'
    Version      = '1.6-studio'
}

$script:SelfTestMode = [bool]$SelfTest
$script:SelfTestLog  = Join-Path $env:TEMP 'bazino-studio-bridge-selftest.log'
$script:LogFile      = Join-Path $env:APPDATA 'BazinoStudioBridge\bridge.log'

$script:State = @{
    Running        = $false
    Busy           = $false
    Socket         = $null          # ClientWebSocket to the page target
    PageTargetId   = $null
    ForcedTargetId = $null          # set via Bridge.select
    ConsecFails    = 0
    LastStatusPush = $null
    SessionStarted = $null
    LastCycleText  = ''
    Rows           = @{}            # name -> value label control
    LogBox         = $null
    Window         = $null
    Timer          = $null
}

# ---------------------------------------------------------------------------
# logging
# ---------------------------------------------------------------------------
function Write-Log {
    param([string]$Message, [string]$Level = 'info')
    $line = '{0} [{1,-5}] {2}' -f (Get-Date -Format 'HH:mm:ss'), $Level, $Message
    if ($script:SelfTestMode) {
        Add-Content -Path $script:SelfTestLog -Value $line -Encoding UTF8
        return
    }
    if ($script:State.LogBox) {
        $script:State.LogBox.AppendText($line + [Environment]::NewLine)
    }
    try { Add-Content -Path $script:LogFile -Value $line -Encoding UTF8 } catch { }
}

# ---------------------------------------------------------------------------
# settings / token (DPAPI - only this Windows user on this machine)
# ---------------------------------------------------------------------------
function Get-Settings {
    if (Test-Path $script:Cfg.SettingsPath) {
        try {
            return Get-Content $script:Cfg.SettingsPath -Raw | ConvertFrom-Json
        } catch { return $null }
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
}

function Read-Token {
    try {
        if (-not (Test-Path $script:Cfg.SettingsPath)) { return $null }
        $json = Get-Content $script:Cfg.SettingsPath -Raw | ConvertFrom-Json
        if (-not $json.token) { return $null }
        $secure = ConvertTo-SecureString $json.token
        $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
        try     { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
        finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
    } catch {
        return $null
    }
}

# ---------------------------------------------------------------------------
# GitHub bus (contents API with the owner's fine-grained PAT)
# ---------------------------------------------------------------------------
function Invoke-GitHub {
    param([string]$Method = 'GET', [string]$Path, $Body = $null)
    $token = Read-Token
    if (-not $token) { throw 'No GitHub token - open Settings and paste a fine-grained PAT (Contents: Read and write).' }

    $headers = @{
        'Authorization'         = "Bearer $token"
        'Accept'                = 'application/vnd.github+json'
        'X-GitHub-Api-Version'  = '2022-11-28'
        'User-Agent'            = 'BazinoStudioBridge'
    }
    $uri = "https://api.github.com/$Path"
    try {
        if ($Body -ne $null) {
            $json = ConvertTo-Json $Body -Depth 16
            return Invoke-RestMethod -Uri $uri -Method $Method -Headers $headers `
                -ContentType 'application/json' -Body $json -TimeoutSec 30
        }
        return Invoke-RestMethod -Uri $uri -Method $Method -Headers $headers -TimeoutSec 30
    } catch {
        # 404 is a normal "not there yet" on the bus - return $null instead of throwing
        $code = $null
        try { $code = [int]$_.Exception.Response.StatusCode } catch { }
        if ($code -eq 404) { return $null }
        throw
    }
}

function Test-GitHub {
    # This short-lived bus stores CDP commands in code history: private ONLY.
    $repo = Invoke-GitHub -Path ("repos/{0}/{1}" -f $script:Cfg.Owner, $script:Cfg.Repo)
    if (-not $repo -or $repo.private -ne $true) {
        Write-Log 'GitHub: private repository with Contents access required; refusing to connect' 'error'
        return $false
    }
    Write-Log ('GitHub: ok (private repo {0})' -f $repo.full_name)
    return $true
}

function Get-BusCommands {
    # Returns @{ seq; sha; cmd } sorted by seq, or an empty array.
    $list = Invoke-GitHub -Path ("repos/{0}/{1}/contents/{2}/cmd?ref={3}" -f `
            $script:Cfg.Owner, $script:Cfg.Repo, $script:Cfg.BusRoot, [uri]::EscapeDataString($script:Cfg.BusBranch))
    if (-not $list) { return @() }
    $out = @()
    foreach ($f in ($list | Where-Object { $_.name -match '^\d+\.json$' })) {
        $blobPath = ($f.url -replace '^https://api\.github\.com/', '')
        $blob = Invoke-GitHub -Path $blobPath
        if (-not $blob) { continue }
        $text = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(($blob.content -replace "\s", '')))
        try { $cmd = $text | ConvertFrom-Json }
        catch {
            Write-Log ("bad command file {0} - skipped" -f $f.name) 'warn'
            continue
        }
        $seq = 0
        if ([int]::TryParse(($f.name -replace '\.json$', ''), [ref]$seq)) {
            $out += @{ seq = $seq; sha = $f.sha; cmd = $cmd }
        }
    }
    return ($out | Sort-Object -Property seq)
}

function Push-BusFile {
    # PUT a file on the bus branch (creates or updates via sha).
    # Self-heal: a 409 (stale sha / someone pushed first) is retried with a
    # freshly fetched sha instead of failing the whole cycle.
    param([string]$RelPath, [string]$Json, [string]$Message)
    $b64  = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($Json))
    $path = ("repos/{0}/{1}/contents/{2}/{3}?ref={4}" -f `
            $script:Cfg.Owner, $script:Cfg.Repo, $script:Cfg.BusRoot, $RelPath, [uri]::EscapeDataString($script:Cfg.BusBranch))
    $lastErr = $null
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            $existing = Invoke-GitHub -Path $path
            $body = @{
                message = $Message
                content = $b64
                branch  = $script:Cfg.BusBranch
            }
            if ($existing -and $existing.sha) { $body.sha = $existing.sha }
            Invoke-GitHub -Method 'PUT' -Path $path -Body $body | Out-Null
            return
        } catch {
            $lastErr = $_.Exception.Message
            Write-Log ("push {0} attempt {1} failed: {2}" -f $RelPath, $attempt, $lastErr) 'warn'
            Start-Sleep -Seconds (2 * $attempt)
        }
    }
    throw "Push-BusFile $RelPath failed after 3 attempts: $lastErr"
}

function Push-BusResult {
    param([int]$Seq, $Res)
    # Defense against pipeline pollution: if the command's return stream
    # picked up stray objects (observed: an empty object before the real
    # reply on CDP commands), keep only the element that carries the reply.
    if ($Res -is [array]) {
        $carrying = @($Res | Where-Object { $_ -and $_.PSObject.Properties.Match('ok').Count -gt 0 })
        if ($carrying.Count -gt 0) { $Res = $carrying[$carrying.Count - 1] }
    }
    $json = $Res | ConvertTo-Json -Depth 32
    # Raw CDP payloads live in Git history: fail closed instead of recording
    # OAuth redirects, cookies, Bearer tokens, or large page snapshots.
    $sensitive = '(?i)(?:[?&#](?:code|access_token|refresh_token|id_token|state)=|"(?:cookie|cookies|authorization|access_token|refresh_token|client_secret)"\s*:|Bearer\s+\S+|gh[pousr]_[A-Za-z0-9]{20,})'
    if ([Text.Encoding]::UTF8.GetByteCount($json) -gt 65536 -or $json -match $sensitive) {
        $json = (@{ id = $Res.id; ok = $false; error = 'SENSITIVE_RESULT_BLOCKED: nothing published' } | ConvertTo-Json)
    }
    Push-BusFile -RelPath ("res/{0}.json" -f $Seq) -Json $json -Message ("bridge: res/{0}" -f $Seq)
}

function Remove-BusCommand {
    param([int]$Seq, [string]$Sha)
    $path = ("repos/{0}/{1}/contents/{2}/cmd/{3}.json" -f $script:Cfg.Owner, $script:Cfg.Repo, $script:Cfg.BusRoot, $Seq)
    $body = @{ message = ("bridge: done cmd/{0}" -f $Seq); sha = $Sha; branch = $script:Cfg.BusBranch }
    Invoke-GitHub -Method 'DELETE' -Path $path -Body $body | Out-Null
}

function Push-Status {
    # Heartbeat only every 120 s; this bus shares the code branch, stop after use.
    $chrome = 'unknown'
    if (Test-ChromeDebug) { $chrome = ("port {0}" -f $script:Cfg.ChromePort) }
    elseif ($script:State.Running) { $chrome = 'down' }
    $status = @{
        software     = 'BazinoStudioBridge'
        version      = $script:Cfg.Version
        repo         = ("{0}/{1}" -f $script:Cfg.Owner, $script:Cfg.Repo)
        bridge       = $(if ($script:State.Running) { 'connected' } else { 'stopped' })
        chrome       = $chrome
        pageTargetId = $script:State.PageTargetId
        pollMs       = $script:Cfg.PollMs
        lastCycle    = (Get-Date).ToUniversalTime().ToString('o')
        lastCycleMs  = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
        note         = 'bridge heartbeat'
    }
    try {
        Push-BusFile -RelPath 'status.json' -Json ($status | ConvertTo-Json) -Message 'bridge: heartbeat'
        $script:State.LastStatusPush = Get-Date
    } catch {
        Write-Log ("heartbeat push failed: {0}" -f $_.Exception.Message) 'warn'
    }
}

# ---------------------------------------------------------------------------
# Chrome lifecycle
# ---------------------------------------------------------------------------
function Find-ChromePath {
    $candidates = @(
        (Join-Path $env:ProgramFiles 'Google\Chrome\Application\chrome.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Google\Chrome\Application\chrome.exe'),
        (Join-Path $env:LOCALAPPDATA 'Google\Chrome\Application\chrome.exe')
    )
    foreach ($c in $candidates) { if ($c -and (Test-Path $c)) { return $c } }
    return 'chrome.exe'   # last resort: PATH
}

function Test-ChromeDebug {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $ok = $client.ConnectAsync('127.0.0.1', $script:Cfg.ChromePort).Wait(800)
        return ($ok -and $client.Connected)
    } catch {
        return $false
    } finally {
        $client.Close()
    }
}

function Test-ProfileInUse {
    # True when a Chrome process already owns our profile folder.
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

function Get-AgentChromeArgs {
    # Fixed flags - identical every time, or Chrome rebuilds the profile and
    # the Google login drops.
    # --disable-quic: the owner's network kills UDP/QUIC (ERR_QUIC_PROTOCOL_ERROR
    # on google.com and every Cloudflare-fronted site incl. bazino.pro); with
    # QUIC off Chrome falls back to plain TCP HTTPS, which works.
    return @(
        "--remote-debugging-port=$($script:Cfg.ChromePort)"
        '--remote-allow-origins=*'
        "--user-data-dir=`"$($script:Cfg.ProfileDir)`""
        '--no-first-run'
        '--no-default-browser-check'
        '--restore-last-session'
        '--disable-quic'
    )
}

function Start-AgentChrome {
    # 1. debug port answers?     -> reuse it (e.g. another BazinoBridge is up)
    # 2. profile locked?         -> wait up to 10 s for its port
    # 3. neither?                -> launch a fresh agent Chrome
    if (Test-ChromeDebug) { return $true }

    if (Test-ProfileInUse) {
        Write-Log 'Chrome profile is in use - waiting for its debug port (max 10 s)'
        for ($i = 0; $i -lt 10; $i++) {
            Start-Sleep -Seconds 1
            if (Test-ChromeDebug) { return $true }
        }
        Write-Log 'Profile in use but the debug port never answered (Chrome started without the flag?)' 'error'
        return $false
    }

    $chrome = Find-ChromePath
    $chromeArgs = Get-AgentChromeArgs
    Write-Log ("starting Chrome: {0} {1}" -f $chrome, ($chromeArgs -join ' '))
    Start-Process -FilePath $chrome -ArgumentList $chromeArgs

    for ($i = 0; $i -lt 14; $i++) {
        Start-Sleep -Seconds 1
        if (Test-ChromeDebug) {
            Write-Log 'Chrome debug port is up'
            return $true
        }
    }
    Write-Log 'Chrome did not open the debug port within 14 s' 'error'
    return $false
}

function New-ChromeTab {
    # Self-heal: every tab closed -> open a fresh about:blank page target.
    # Chrome 111+ requires PUT on /json/new; older builds accept GET.
    foreach ($method in @('PUT', 'GET')) {
        try {
            $r = Invoke-WebRequest -Uri ("http://127.0.0.1:{0}/json/new" -f $script:Cfg.ChromePort) `
                -Method $method -TimeoutSec 5 -UseBasicParsing
            if ($r.StatusCode -eq 200) {
                Write-Log "self-heal: opened a new tab (no page target existed)"
                return $true
            }
        } catch { }
    }
    return $false
}

function Connect-ChromeSocket {
    # Returns a CLEAN boolean. The connection dance may put stray objects on
    # the pipeline; only the final flag must survive, otherwise a failed
    # connect looks like a truthy array and failure checks never fire.
    $out = & {
        try {
            $pages = Invoke-RestMethod -Uri ("http://127.0.0.1:{0}/json/list" -f $script:Cfg.ChromePort) -TimeoutSec 5
            $page = $null
            if ($script:State.ForcedTargetId) {
                $page = $pages | Where-Object { $_.id -eq $script:State.ForcedTargetId } | Select-Object -First 1
                if (-not $page) {
                    Write-Log ("forced target {0} is gone - falling back to the first tab" -f $script:State.ForcedTargetId) 'warn'
                    $script:State.ForcedTargetId = $null
                }
            }
            if (-not $page) { $page = $pages | Where-Object { $_.type -eq 'page' } | Select-Object -First 1 }
            if (-not $page) {
                # Self-heal: Chrome is up but has no page targets (all tabs
                # closed). Open one instead of failing the command.
                if (-not (New-ChromeTab)) {
                    Write-Log 'no page target found in Chrome and could not open one' 'error'
                    return $false
                }
                $pages = Invoke-RestMethod -Uri ("http://127.0.0.1:{0}/json/list" -f $script:Cfg.ChromePort) -TimeoutSec 5
                $page = $pages | Where-Object { $_.type -eq 'page' } | Select-Object -First 1
                if (-not $page) {
                    Write-Log 'still no page target after opening a tab' 'error'
                    return $false
                }
            }

            # close any stale socket first
            if ($script:State.Socket) {
                try { $script:State.Socket.Dispose() } catch { }
                $script:State.Socket = $null
            }

            $ws = New-Object System.Net.WebSockets.ClientWebSocket
            $cts = [System.Threading.CancellationTokenSource]::new(10000)
            $ws.ConnectAsync([Uri]$page.webSocketDebuggerUrl, $cts.Token).GetAwaiter().GetResult()
            $script:State.Socket       = $ws
            $script:State.PageTargetId = $page.id
            Write-Log ("CDP attached to tab: {0} ({1})" -f $page.title, $page.url)
            return $true
        } catch {
            Write-Log ("CDP connect failed: {0}" -f $_.Exception.Message) 'error'
            return $false
        }
    }
    if ($out -is [array]) { $out = $out | Select-Object -Last 1 }
    return [bool]$out
}

# ---------------------------------------------------------------------------
# CDP transport
# ---------------------------------------------------------------------------
function Send-Cdp {
    param($Frame)   # object with id/method/params
    $ws = $script:State.Socket
    if (-not $ws -or $ws.State -ne [System.Net.WebSockets.WebSocketState]::Open) {
        throw 'CDP socket is not open'
    }
    $json  = $Frame | ConvertTo-Json -Depth 32 -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($json)
    $seg   = [System.ArraySegment[byte]]::new($bytes)
    $ws.SendAsync($seg, [System.Net.WebSockets.WebSocketMessageType]::Text, $true,
                  [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
}

function Receive-CdpFrame {
    # Reads one complete frame. THROWS on timeout: a cancelled ReceiveAsync
    # leaves a ClientWebSocket aborted in .NET, so a timed-out socket must
    # never be reused (the caller disposes it and reconnects).
    param([int]$TimeoutMs = 30000)
    $ws = $script:State.Socket
    if (-not $ws -or $ws.State -ne [System.Net.WebSockets.WebSocketState]::Open) {
        throw 'CDP socket is not open'
    }
    $buffer = New-Object byte[] 262144
    $sw     = [Diagnostics.Stopwatch]::StartNew()
    $sb     = New-Object System.Text.StringBuilder

    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
        $remaining = [int][Math]::Max(250, $TimeoutMs - $sw.ElapsedMilliseconds)
        $cts = [System.Threading.CancellationTokenSource]::new($remaining)
        $seg = [System.ArraySegment[byte]]::new($buffer)
        try {
            $res = $ws.ReceiveAsync($seg, $cts.Token).GetAwaiter().GetResult()
        } catch [System.OperationCanceledException] {
            break        # timeout - socket is aborted now, do not reuse it
        }
        if ($res.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Close) {
            throw 'CDP socket closed by Chrome'
        }
        [void]$sb.Append([Text.Encoding]::UTF8.GetString($buffer, 0, $res.Count))
        if ($res.EndOfMessage) { return $sb.ToString() }
        # keep pumping the UI while waiting for long CDP replies
        if (-not $script:SelfTestMode -and $script:State.Window) { [System.Windows.Forms.Application]::DoEvents() }
    }
    throw "No CDP frame within $TimeoutMs ms (connection presumed dead - will reconnect)"
}

function Receive-Cdp {
    # Returns the reply whose id matches $ExpectId. Events are skipped.
    param(
        [int]$TimeoutMs = 30000,
        [int]$ExpectId  = -1
    )
    $sw = [Diagnostics.Stopwatch]::StartNew()

    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
        $remaining = $TimeoutMs - $sw.ElapsedMilliseconds
        $frame = Receive-CdpFrame -TimeoutMs $remaining

        if ($ExpectId -lt 0) { return $frame }
        if (-not $frame) { continue }

        $id = $null
        try { $id = ($frame | ConvertFrom-Json).id } catch { }

        if ($null -eq $id) { continue }          # event, not a reply
        if ([int]$id -eq $ExpectId) { return $frame }
        # stale reply from an earlier command - drop and keep waiting
    }
    throw "No CDP reply with id $ExpectId within $TimeoutMs ms"
}

# ---------------------------------------------------------------------------
# command execution
# ---------------------------------------------------------------------------
function Invoke-BridgeMethod {
    # Bridge.* extensions that do not go through CDP.
    param($Cmd)
    $id = [int]$Cmd.id
    switch ($Cmd.method) {
        'Bridge.targets' {
            $list = Invoke-RestMethod -Uri ("http://127.0.0.1:{0}/json/list" -f $script:Cfg.ChromePort) -TimeoutSec 5
            return @{ id = $id; ok = $true; result = $list }
        }
        'Bridge.select' {
            $tid = $null
            if ($Cmd.params) { $tid = $Cmd.params.targetId }
            if (-not $tid) { return @{ id = $id; ok = $false; error = 'Bridge.select needs params.targetId' } }
            $script:State.ForcedTargetId = $tid
            if ($script:State.Socket) {
                try { $script:State.Socket.Dispose() } catch { }
                $script:State.Socket = $null
            }
            if (-not (Connect-ChromeSocket)) { return @{ id = $id; ok = $false; error = 'could not connect to the selected target' } }
            return @{ id = $id; ok = $true; result = @{ selected = $tid } }
        }
        'Bridge.reconnect' {
            if ($script:State.Socket) {
                try { $script:State.Socket.Dispose() } catch { }
                $script:State.Socket = $null
            }
            $ok = Connect-ChromeSocket
            return @{ id = $id; ok = $ok; result = @{ reconnected = $ok } }
        }
        'Bridge.echo' {
            return @{ id = $id; ok = $true; result = $Cmd.params }
        }
        default {
            return @{ id = $id; ok = $false; error = ("unknown bridge method {0}" -f $Cmd.method) }
        }
    }
}

function Invoke-BusCommand {
    param($Cmd)
    $id = [int]$Cmd.id
    try {
        if (-not $Cmd.method) { return @{ id = $id; ok = $false; error = 'command has no method' } }
        if ($Cmd.method -like 'Bridge.*') { return Invoke-BridgeMethod -Cmd $Cmd }

        if (-not (Test-ChromeDebug)) {
            if (-not (Start-AgentChrome)) { throw 'Chrome is not available' }
        }

        # Stateless CDP: a fresh websocket per command, plus one retry.
        # Immune to stale sockets (tab closed, Chrome restart, half-open TCP).
            $attempt = 0
            while ($true) {
                $attempt++
                try {
                    if (-not (Connect-ChromeSocket)) { throw 'could not attach to a tab' }
                    # NOTE: no backlog clearing here. The socket is brand new,
                    # so nothing stale can be buffered; and cancelling a
                    # pending ReceiveAsync ABORTS a ClientWebSocket in .NET,
                    # which is what silently killed every command in v1.0-1.2.
                    $frame = @{ id = $id; method = [string]$Cmd.method }
                if ($Cmd.params) { $frame.params = $Cmd.params }
                Send-Cdp $frame

                $reply = Receive-Cdp -ExpectId $id -TimeoutMs 60000
                $obj   = $reply | ConvertFrom-Json
                if ($obj.error) {
                    return @{ id = $id; ok = $false; error = ($obj.error | ConvertTo-Json -Depth 10 -Compress) }
                }
                return @{ id = $id; ok = $true; result = $obj.result }
            } catch {
                # drop the broken socket before any retry
                if ($script:State.Socket) {
                    try { $script:State.Socket.Dispose() } catch { }
                    $script:State.Socket = $null
                }
                if ($attempt -ge 2) { throw }
                Write-Log ("CDP attempt {0} failed ({1}) - reconnecting and retrying" -f $attempt, $_.Exception.Message) 'warn'
            }
        }
    } catch {
        return @{ id = $id; ok = $false; error = $_.Exception.Message }
    }
}

# ---------------------------------------------------------------------------
# repair / main loop
# ---------------------------------------------------------------------------
function Repair-Bridge {
    param([string]$Reason)

    $script:State.ConsecFails++
    if ($script:State.ConsecFails -gt $script:Cfg.MaxRetries) {
        Write-Log "Too many consecutive failures - stopping." 'error'
        Stop-Bridge
        return $false
    }

    # exponential backoff: 2, 4, 8, 16 seconds
    $backoff = [Math]::Min([Math]::Pow(2, $script:State.ConsecFails), 16)
    Write-Log ("repair #{0} ({1}) - waiting {2}s" -f $script:State.ConsecFails, $Reason, $backoff) 'warn'
    Start-Sleep -Seconds $backoff

    if (-not (Test-ChromeDebug)) {
        if (-not (Start-AgentChrome)) { return $false }
    }
    if (-not (Connect-ChromeSocket)) { return $false }
    return $true
}

function Invoke-BridgeCycle {
    if (-not $script:State.Running) { return }
    # This is a short, explicitly owner-approved browser session, not a 24/7
    # heartbeat on the code branch. Connect can be pressed again if needed.
    if ($script:State.SessionStarted -and
        ((Get-Date) - $script:State.SessionStarted).TotalMinutes -ge 30) {
        Write-Log 'Session reached 30 minutes; disconnecting to limit Git history.' 'warn'
        Stop-Bridge
        return
    }
    try {
        # heartbeat at most every 120 s (not every 2 s cycle)
        $now = Get-Date
        if (-not $script:State.LastStatusPush -or
            (($now - $script:State.LastStatusPush).TotalSeconds -ge 120)) {
            Push-Status
        }

        $cmds = Get-BusCommands
        foreach ($c in $cmds) {
            $method = '?'
            if ($c.cmd -and $c.cmd.method) { $method = $c.cmd.method }
            $res = Invoke-BusCommand -Cmd $c.cmd
            Push-BusResult -Seq $c.seq -Res $res
            try { Remove-BusCommand -Seq $c.seq -Sha $c.sha }
            catch { Write-Log ("could not delete cmd/{0}: {1}" -f $c.seq, $_.Exception.Message) 'warn' }

            if ($res.ok) {
                Write-Log ("cmd/{0} {1} -> ok" -f $c.seq, $method)
                $script:State.LastCycleText = ('{0} {1} ok' -f (Get-Date -Format HH:mm:ss), $method)
            } else {
                Write-Log ("cmd/{0} {1} -> FAILED: {2}" -f $c.seq, $method, $res.error) 'warn'
                $script:State.LastCycleText = ('{0} {1} failed' -f (Get-Date -Format HH:mm:ss), $method)
            }
            $script:State.ConsecFails = 0
        }
        if ($cmds.Count -eq 0) {
            $script:State.LastCycleText = ('idle {0}' -f (Get-Date -Format HH:mm:ss))
        }
        Update-Status 'Bridge' 'ok' $script:State.LastCycleText
    } catch {
        Write-Log ("cycle error: {0}" -f $_.Exception.Message) 'error'
        Update-Status 'Bridge' 'warn' 'repairing'
        Repair-Bridge -Reason $_.Exception.Message | Out-Null
    }
}

function Start-Bridge {
    if ($script:State.Running) { return }
    Update-Status 'Bridge' 'warn' 'connecting...'
    try {
        if (-not (Test-GitHub)) {
            Update-Status 'GitHub' 'error' 'failed (token / repo)'
            return
        }
        Update-Status 'GitHub' 'ok' ('repo OK ({0})' -f $script:Cfg.Repo)

        if (-not (Test-ChromeDebug)) {
            if (-not (Start-AgentChrome)) {
                Update-Status 'Chrome' 'error' 'not running'
                Update-Status 'Bridge' 'error' 'cannot start Chrome'
                return
            }
        }
        Update-Status 'Chrome' 'ok' ('debug port {0}' -f $script:Cfg.ChromePort)

        if (-not (Connect-ChromeSocket)) {
            Update-Status 'Bridge' 'error' 'CDP attach failed'
            return
        }

        $script:State.Running     = $true
        $script:State.SessionStarted = Get-Date
        $script:State.ConsecFails = 0
        Push-Status
        if ($script:State.Timer) { $script:State.Timer.Start() }
        Update-Status 'Bridge' 'ok' 'connected'
        Write-Log 'Bridge connected - polling for commands'
    } catch {
        Write-Log ("connect failed: {0}" -f $_.Exception.Message) 'error'
        Update-Status 'Bridge' 'error' 'connect failed'
    }
}

function Stop-Bridge {
    $wasRunning = $script:State.Running
    $script:State.Running = $false
    if ($script:State.Timer) { $script:State.Timer.Stop() }
    if ($script:State.Socket) {
        try {
            if ($script:State.Socket.State -eq [System.Net.WebSockets.WebSocketState]::Open) {
                $script:State.Socket.CloseAsync(
                    [System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, 'bye',
                    [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
            }
        } catch { }
        try { $script:State.Socket.Dispose() } catch { }
        $script:State.Socket = $null
    }
    if ($wasRunning) {
        Push-Status          # final heartbeat: bridge = stopped
        Write-Log 'Bridge disconnected'
    }
    Update-Status 'Bridge' 'idle' 'disconnected'
}

# ---------------------------------------------------------------------------
# self-test (no GUI, no network, no Chrome) - used by the CI workflow
# ---------------------------------------------------------------------------
function Stop-SelfTest {
    param([int]$Code)
    try { [Console]::Out.Flush() } catch { }
    try { [Environment]::Exit($Code) } catch { }
    try {
        $p = Get-Process -Id $PID -ErrorAction Stop
        $p.Kill()
    } catch { }
    exit $Code
}

function Invoke-SelfTest {
    Set-Content -Path $script:SelfTestLog -Value ("Bazino Studio Bridge self-test {0}" -f (Get-Date -Format o)) -Encoding UTF8
    $failed = $false

    # 1) config sanity
    foreach ($k in @('Owner', 'Repo', 'BusBranch', 'ChromePort', 'ProfileDir', 'PollMs', 'MaxRetries', 'SettingsPath')) {
        if (-not $script:Cfg[$k]) {
            Add-Content -Path $script:SelfTestLog -Value "FAIL: config key $k is missing" -Encoding UTF8
            $failed = $true
        }
    }
    if ($script:Cfg.Repo -ne 'bazino-gamenet-portal') {
        Add-Content -Path $script:SelfTestLog -Value 'FAIL: wrong repo in config' -Encoding UTF8
        $failed = $true
    }
    Write-Log 'config: ok'

    # 2) DPAPI token round-trip against a scratch settings file
    $realSettings = $script:Cfg.SettingsPath
    $script:Cfg.SettingsPath = Join-Path $env:TEMP 'bazino-studio-bridge-selftest-settings.json'
    try {
        Save-Token 'selftest-token-123'
        $back = Read-Token
        if ($back -ne 'selftest-token-123') {
            Add-Content -Path $script:SelfTestLog -Value 'FAIL: DPAPI round-trip mismatch' -Encoding UTF8
            $failed = $true
        } else { Write-Log 'DPAPI round-trip: ok' }
    } finally {
        Remove-Item -Path $script:Cfg.SettingsPath -ErrorAction SilentlyContinue
        $script:Cfg.SettingsPath = $realSettings
    }

    # 3) command JSON round-trip (the exact shape that travels on the bus)
    $frame = @{
        id     = 77
        method = 'Runtime.evaluate'
        params = @{ expression = '1+1'; returnByValue = $true }
    }
    $json   = $frame | ConvertTo-Json -Depth 32
    $parsed = $json | ConvertFrom-Json
    if ($parsed.id -ne 77 -or $parsed.method -ne 'Runtime.evaluate' -or $parsed.params.expression -ne '1+1') {
        Add-Content -Path $script:SelfTestLog -Value 'FAIL: command JSON round-trip' -Encoding UTF8
        $failed = $true
    } else { Write-Log 'command JSON round-trip: ok' }

    # 4) reply-vs-event matching on sample frames (the logic of Receive-Cdp)
    $frames = @(
        '{"method":"Page.frameStartedLoading","params":{"frameId":"A"}}'
        '{"id":2,"result":{"ok":"stale"}}'
        '{"id":77,"result":{"result":{"type":"number","value":2}}}'
    )
    $matched = $null
    foreach ($f in $frames) {
        $o = $null; try { $o = $f | ConvertFrom-Json } catch { }
        if ($null -eq $o.id) { continue }
        if ([int]$o.id -eq 77) { $matched = $o }
    }
    if (-not $matched -or $matched.result.result.value -ne 2) {
        Add-Content -Path $script:SelfTestLog -Value 'FAIL: CDP id matching' -Encoding UTF8
        $failed = $true
    } else { Write-Log 'CDP id matching: ok' }

    # 5) fixed Chrome flags (dedicated port + dedicated profile)
    $chromeArgs = Get-AgentChromeArgs
    $argsText = $chromeArgs -join ' '
    if ($argsText -notmatch '--remote-debugging-port=9334' -or
        $argsText -notmatch '--user-data-dir=' -or
        $argsText -notmatch '--no-first-run' -or
        $argsText -notmatch [regex]::Escape('chrome-bazino-portal')) {
        Add-Content -Path $script:SelfTestLog -Value 'FAIL: Chrome launch flags' -Encoding UTF8
        $failed = $true
    } else { Write-Log 'Chrome launch flags: ok' }

    if ($failed) {
        Add-Content -Path $script:SelfTestLog -Value 'SELFTEST FAILED' -Encoding UTF8
        Stop-SelfTest 1
    }
    Add-Content -Path $script:SelfTestLog -Value 'SELFTEST OK' -Encoding UTF8
    Stop-SelfTest 0
}

# ---------------------------------------------------------------------------
# single-instance guard (prevents another Studio Bridge from stealing commands)
# ---------------------------------------------------------------------------
$script:InstanceMutex = $null

function Test-SingleInstance {
    # A global named mutex: held for the lifetime of this Studio Bridge.
    # The older cdp-bus bridge has a separate mutex: disconnect it manually
    # before connecting this one to avoid two tools controlling Chrome.
    $created = $false
    try {
        $script:InstanceMutex = New-Object System.Threading.Mutex(
            $true, 'Global\BazinoStudioBridge-portal', [ref]$created)
    } catch {
        Write-Log ("single-instance mutex unavailable: {0}" -f $_.Exception.Message) 'warn'
        return $true   # do not block the app if the OS refuses the mutex
    }
    if (-not $created) {
        $msg = 'Another Bazino Studio Bridge is already running (check the system tray ' +
               'and Task Manager for BazinoStudioBridge.exe). Close it first - two ' +
               'bridges steal each other''s commands.'
        Write-Log $msg 'error'
        try {
            [System.Windows.Forms.MessageBox]::Show($msg, 'Bazino Studio Bridge',
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Warning) | Out-Null
        } catch { }
        return $false
    }
    return $true
}

# ---------------------------------------------------------------------------
# GUI (skipped entirely in self-test mode: WinForms keeps the process alive)
# ---------------------------------------------------------------------------
function New-StatusRow {
    param([string]$Name, [int]$Top)
    $nameLabel = New-Object System.Windows.Forms.Label
    $nameLabel.Text     = $Name + ':'
    $nameLabel.Location = New-Object System.Drawing.Point(16, $Top)
    $nameLabel.AutoSize = $true
    $nameLabel.Font     = New-Object System.Drawing.Font('Segoe UI', 10, [System.Drawing.FontStyle]::Bold)

    $valueLabel = New-Object System.Windows.Forms.Label
    $valueLabel.Text     = 'idle'
    $valueLabel.Location = New-Object System.Drawing.Point(120, $Top)
    $valueLabel.AutoSize = $true
    $valueLabel.Font     = New-Object System.Drawing.Font('Segoe UI', 10)
    $valueLabel.ForeColor = [System.Drawing.Color]::Gray

    $script:Window.Controls.Add($nameLabel)
    $script:Window.Controls.Add($valueLabel)
    $script:State.Rows[$Name] = $valueLabel
}

function Update-Status {
    param([string]$Name, [string]$StateName, [string]$Text)
    if ($script:SelfTestMode) { return }
    $label = $script:State.Rows[$Name]
    if (-not $label) { return }
    $color = [System.Drawing.Color]::Gray
    switch ($StateName) {
        'ok'    { $color = [System.Drawing.Color]::FromArgb(46, 125, 50) }
        'warn'  { $color = [System.Drawing.Color]::FromArgb(230, 81, 0) }
        'error' { $color = [System.Drawing.Color]::Firebrick }
        default { $color = [System.Drawing.Color]::Gray }
    }
    $label.Text      = $Text
    $label.ForeColor = $color
}

function Show-Settings {
    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text        = 'Bazino Studio Bridge - Settings'
    $dlg.Size        = New-Object System.Drawing.Size(560, 300)
    $dlg.FormBorderStyle = 'FixedDialog'
    $dlg.MaximizeBox = $false
    $dlg.StartPosition = 'CenterParent'

    $info = New-Object System.Windows.Forms.Label
    $info.Text     = "Fine-grained PAT (github.com/settings/personal-access-tokens/new)`r`n" +
                     "Repository access: Only select repositories -> $($script:Cfg.Owner)/$($script:Cfg.Repo)`r`n" +
                     'Permissions -> Contents: Read and write. Nothing else.'
    $info.Location = New-Object System.Drawing.Point(12, 12)
    $info.Size     = New-Object System.Drawing.Size(520, 60)
    $dlg.Controls.Add($info)

    $box = New-Object System.Windows.Forms.TextBox
    $box.Location    = New-Object System.Drawing.Point(12, 80)
    $box.Size        = New-Object System.Drawing.Size(520, 24)
    $box.UseSystemPasswordChar = $true
    $existing = Read-Token
    if ($existing) { $box.Text = $existing }
    $dlg.Controls.Add($box)

    $btnSave = New-Object System.Windows.Forms.Button
    $btnSave.Text     = 'Save'
    $btnSave.Location = New-Object System.Drawing.Point(12, 120)
    $btnSave.Add_Click({
        if ($box.Text.Trim().Length -lt 10) {
            [System.Windows.Forms.MessageBox]::Show('That does not look like a token.', 'Bazino Studio Bridge')
            return
        }
        Save-Token $box.Text.Trim()
        Write-Log 'token saved (DPAPI-encrypted)'
        $dlg.DialogResult = 'OK'
        $dlg.Close()
    })
    $dlg.Controls.Add($btnSave)

    $btnTest = New-Object System.Windows.Forms.Button
    $btnTest.Text     = 'Save + Test'
    $btnTest.Location = New-Object System.Drawing.Point(100, 120)
    $btnTest.Add_Click({
        if ($box.Text.Trim().Length -ge 10) {
            Save-Token $box.Text.Trim()
        }
        if (Test-GitHub) {
            [System.Windows.Forms.MessageBox]::Show('GitHub OK - private repo reachable.', 'Bazino Studio Bridge')
        } else {
            [System.Windows.Forms.MessageBox]::Show('GitHub check failed - see the log.', 'Bazino Studio Bridge')
        }
    })
    $dlg.Controls.Add($btnTest)

    [void]$dlg.ShowDialog($script:Window)
}

function New-MainWindow {
    $form = New-Object System.Windows.Forms.Form
    $form.Text        = "Bazino Studio Bridge - $($script:Cfg.Repo)"
    $form.Size        = New-Object System.Drawing.Size(620, 460)
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.StartPosition = 'CenterScreen'
    $script:Window = $form

    New-StatusRow -Name 'GitHub' -Top 16
    New-StatusRow -Name 'Chrome' -Top 44
    New-StatusRow -Name 'Bridge' -Top 72

    $log = New-Object System.Windows.Forms.RichTextBox
    $log.Location  = New-Object System.Drawing.Point(12, 108)
    $log.Size      = New-Object System.Drawing.Size(584, 250)
    $log.ReadOnly  = $true
    $log.Font      = New-Object System.Drawing.Font('Consolas', 9)
    $log.BackColor = [System.Drawing.Color]::FromArgb(32, 32, 32)
    $log.ForeColor = [System.Drawing.Color]::Gainsboro
    $script:State.LogBox = $log
    $form.Controls.Add($log)

    $btnConnect = New-Object System.Windows.Forms.Button
    $btnConnect.Text     = 'Connect'
    $btnConnect.Location = New-Object System.Drawing.Point(12, 372)
    $btnConnect.Size     = New-Object System.Drawing.Size(100, 30)
    $btnConnect.Add_Click({ Start-Bridge })
    $form.Controls.Add($btnConnect)

    $btnDisconnect = New-Object System.Windows.Forms.Button
    $btnDisconnect.Text     = 'Disconnect'
    $btnDisconnect.Location = New-Object System.Drawing.Point(120, 372)
    $btnDisconnect.Size     = New-Object System.Drawing.Size(100, 30)
    $btnDisconnect.Add_Click({ Stop-Bridge })
    $form.Controls.Add($btnDisconnect)

    $btnSettings = New-Object System.Windows.Forms.Button
    $btnSettings.Text     = 'Settings'
    $btnSettings.Location = New-Object System.Drawing.Point(228, 372)
    $btnSettings.Size     = New-Object System.Drawing.Size(100, 30)
    $btnSettings.Add_Click({ Show-Settings })
    $form.Controls.Add($btnSettings)

    $btnCopy = New-Object System.Windows.Forms.Button
    $btnCopy.Text     = 'Copy Log'
    $btnCopy.Location = New-Object System.Drawing.Point(336, 372)
    $btnCopy.Size     = New-Object System.Drawing.Size(100, 30)
    $btnCopy.Add_Click({
        try {
            [System.Windows.Forms.Clipboard]::SetText($script:State.LogBox.Text)
            Write-Log 'log copied to clipboard'
        } catch { Write-Log 'clipboard unavailable' 'warn' }
    })
    $form.Controls.Add($btnCopy)

    $timer = New-Object System.Windows.Forms.Timer
    $timer.Interval = $script:Cfg.PollMs
    $timer.Add_Tick({
        if ($script:State.Busy) { return }
        $script:State.Busy = $true
        try { Invoke-BridgeCycle }
        finally { $script:State.Busy = $false }
    })
    $script:State.Timer = $timer

    $form.Add_FormClosing({
        Stop-Bridge
    })

    return $form
}

# ---------------------------------------------------------------------------
# entry point
# ---------------------------------------------------------------------------
if ($SelfTest) {
    Invoke-SelfTest
    Stop-SelfTest 0
}

# GUI mode
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
} catch { }
$logDir = Split-Path $script:LogFile -Parent
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
# keep the log bounded
if ((Test-Path $script:LogFile) -and ((Get-Item $script:LogFile).Length -gt 1MB)) {
    Remove-Item $script:LogFile -ErrorAction SilentlyContinue
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# refuse to run beside another instance (any version) - they steal commands
if (-not (Test-SingleInstance)) { Stop-SelfTest 1 }

$mainWindow = New-MainWindow
Write-Log ("BazinoStudioBridge v{0} - repo {1}/{2} - bus branch {3}" -f `
    $script:Cfg.Version, $script:Cfg.Owner, $script:Cfg.Repo, $script:Cfg.BusBranch)
Write-Log 'paste a fine-grained PAT in Settings, then press Connect.'
[void]$mainWindow.ShowDialog()
