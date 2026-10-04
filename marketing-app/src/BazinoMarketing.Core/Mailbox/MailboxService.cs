using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using BazinoMarketing.Core.Logging;
using BazinoMarketing.Core.Secrets;
using BazinoMarketing.Core.Settings;

namespace BazinoMarketing.Core.Mailbox;

public enum MailboxStatus
{
    /// <summary>Not started (no token/repository, sample mode, or the owner switched it off).</summary>
    Off,
    /// <summary>Polling; no agent identity published yet.</summary>
    Listening,
    /// <summary>An agent identity is published and trusted; its commands are executed immediately (owner's decision: no approvals).</summary>
    Active,
    /// <summary>Last poll failed (network/token); retrying with back-off.</summary>
    Error,
    /// <summary>Stopped by the owner or on exit.</summary>
    Stopped
}

/// <summary>One executed command, for the UI's recent-activity list.</summary>
public sealed record MailboxActivity(DateTimeOffset At, string Cmd, bool Ok, string Summary, long DurationMs, string Risk);

/// <summary>Small persisted ledger so restarts never reuse an outbox sequence or re-run an already answered command.</summary>
internal sealed class MailboxLedger
{
    [JsonPropertyName("outSeq")] public long OutSeq { get; set; }
    [JsonPropertyName("processed")] public Dictionary<string, DateTimeOffset> Processed { get; set; } = new();
    [JsonPropertyName("lastSeq")] public Dictionary<string, long> LastSeq { get; set; } = new();
}

/// <summary>
/// The «صندوق فرمان»: polls the mailbox folder on the configured Git branch, answers verified commands of the agent whose
/// identity is published there, and publishes a signed heartbeat. By the owner's explicit decision (2026-09-27) there is no
/// approval step: whoever can write <c>agent-identity.json</c> to the private branch is the agent. The owner's control is
/// closing the app (or the stop button). Thread-safety: public members may be called from any thread; events are raised on background threads.
/// </summary>
public sealed class MailboxService : IDisposable
{
    /// <summary>Heartbeat commits are kept rare: every 5 min while an agent is pending/active, every 30 min when merely listening.</summary>
    private static readonly TimeSpan HeartbeatBusy = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HeartbeatIdle = TimeSpan.FromMinutes(30);
    private const string LogTool = "mailbox";

    private readonly Func<AppSettings> _settings;
    private readonly ISecretStore _secrets;
    private readonly JsonlLogStore _log;
    private readonly string _appVersion;
    private readonly string _ledgerPath;
    private readonly CommandExecutor _executor;
    private readonly object _gate = new();
    private readonly ReplayGuard _guard = new();
    private readonly Dictionary<string, (string OutName, string ReplyJson, string Cmd, long OutSeq)> _pendingReplies = new();
    private readonly List<MailboxActivity> _activity = new();
    private readonly SemaphoreSlim _pollNow = new(0, 1);

    private MailboxIdentity? _identity;
    private MailboxLedger _ledger = new();
    private GitHubMailboxTransport? _transport;
    private string _transportKey = "";
    private CancellationTokenSource? _cts;
    private Task? _loop;

    private string? _agentEtag, _inboxEtag, _fastInboxEtag, _fastOutboxSha;
    private long _tickCount;
    private IdentityFile? _agent;         // identity currently published in agent-identity.json (trusted as-is)
    private string _session = "";
    private bool _appIdentityPublished;
    private DateTimeOffset _lastHeartbeat = DateTimeOffset.MinValue;
    private bool _heartbeatDirty = true;
    private int _consecutiveErrors;

    private MailboxStatus _status = MailboxStatus.Off;
    private string _statusText = "";
    private string _lastError = "";
    private DateTimeOffset? _lastPoll;

    public MailboxService(Func<AppSettings> settings, ISecretStore secrets, JsonlLogStore log, string appVersion, string dataFolder, CommandExecutor executor)
    {
        _settings = settings;
        _secrets = secrets;
        _log = log;
        _appVersion = appVersion;
        _ledgerPath = Path.Combine(dataFolder, "mailbox-state.json");
        _executor = executor;
    }

    public event Action? Changed;
    public event Action<MailboxActivity>? ActivityAdded;

    public MailboxStatus Status { get { lock (_gate) return _status; } }
    public string StatusText { get { lock (_gate) return _statusText; } }
    public string LastError { get { lock (_gate) return _lastError; } }
    public DateTimeOffset? LastPoll { get { lock (_gate) return _lastPoll; } }
    public bool IsRunning => _loop is { IsCompleted: false };
    /// <summary>True while a poll (reading, executing, replying, heartbeat) is in progress — the host waits for this before a self-update restart.</summary>
    public bool IsBusy => Volatile.Read(ref _busy) == 1;
    private int _busy;
    public string SessionId { get { lock (_gate) return _session; } }
    public string AppFingerprint { get { lock (_gate) return _identity?.Fingerprint ?? ""; } }
    public string AgentFingerprint { get { lock (_gate) return _agent?.Fingerprint ?? ""; } }
    public string AgentLabel { get { lock (_gate) return _agent?.Label ?? ""; } }
    public IReadOnlyList<MailboxActivity> Activity { get { lock (_gate) return _activity.ToList(); } }

    /// <summary>True when the settings + token allow listening at all.</summary>
    public bool CanStart(out string reason)
    {
        var s = _settings();
        if (!Tools.GitHubClient.IsValidRepository(s.GitHub.Repository)) { reason = "مخزن گیت‌هاب تنظیم نشده است"; return false; }
        if (string.IsNullOrWhiteSpace(s.GitHub.Branch)) { reason = "شاخهٔ گیت‌هاب تنظیم نشده است"; return false; }
        if (string.IsNullOrWhiteSpace(s.GitHub.MailboxPath)) { reason = "مسیر صندوق فرمان خالی است"; return false; }
        if (!_secrets.Has(SecretKeys.GitHubToken)) { reason = "توکن گیت‌هاب ذخیره نشده است"; return false; }
        if (!SecretFormat.IsHeaderSafe(_secrets.GetOrEmpty(SecretKeys.GitHubToken))) { reason = "توکن گیت‌هاب معتبر نیست"; return false; }
        reason = "";
        return true;
    }

    public void Start()
    {
        lock (_gate)
        {
            if (IsRunning) return;
            LoadLedger();
            _cts = new CancellationTokenSource();
            _heartbeatDirty = true;
            if (CanStart(out var reason))
            {
                EnsureIdentity();
                SetStatus(_agent is null ? MailboxStatus.Listening : MailboxStatus.Active, "در حال اتصال به صندوق فرمان…");
            }
            else
            {
                // The loop keeps checking; as soon as the token/repository are saved it starts listening by itself.
                SetStatus(MailboxStatus.Off, reason);
            }
            var token = _cts.Token;
            _loop = Task.Run(() => LoopAsync(token), CancellationToken.None);
        }
        _log.Append(LogLevel.Info, LogTool, "start", "گوش‌دادن به صندوق فرمان آغاز شد", $"app fingerprint {AppFingerprint}");
    }

    /// <summary>Stops polling and (best effort, bounded by <paramref name="grace"/>) publishes listening=false.</summary>
    public async Task StopAsync(TimeSpan grace)
    {
        Task? loop;
        CancellationTokenSource? cts;
        lock (_gate)
        {
            loop = _loop;
            cts = _cts;
            _loop = null;
            _cts = null;
        }
        if (loop is null || cts is null) return;
        cts.Cancel();
        try { await loop.WaitAsync(grace).ConfigureAwait(false); } catch (Exception) { }
        lock (_gate) SetStatus(MailboxStatus.Stopped, "گوش‌دادن متوقف شد");
        try
        {
            using var stopCts = new CancellationTokenSource(grace);
            await WriteHeartbeatAsync(listening: false, stopCts.Token).ConfigureAwait(false);
        }
        catch (Exception) { }
        cts.Dispose();
    }

    /// <summary>Forces the next poll to run immediately (after settings/token changes or a button click).</summary>
    public void PollNow()
    {
        try { _pollNow.Release(); } catch (SemaphoreFullException) { }
    }

    // ---------------------------------------------------------------- loop

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var settings = _settings();
            var configuredSeconds = Math.Clamp(settings.GitHub.PollSeconds, 1, 300);
            var delay = _agent is not null
                ? TimeSpan.FromSeconds(Math.Min(configuredSeconds, 1))
                : TimeSpan.FromSeconds(Math.Min(configuredSeconds, 3));
            try
            {
                if (!CanStart(out var reason))
                {
                    lock (_gate) SetStatus(MailboxStatus.Off, reason);
                    try { await _pollNow.WaitAsync(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
                    continue;
                }
                lock (_gate)
                {
                    EnsureIdentity();
                    if (_status == MailboxStatus.Off) SetStatus(_agent is null ? MailboxStatus.Listening : MailboxStatus.Active, "در حال اتصال به صندوق فرمان…");
                }
                Volatile.Write(ref _busy, 1);
                try { await PollOnceAsync(settings, ct).ConfigureAwait(false); }
                finally { Volatile.Write(ref _busy, 0); }
                lock (_gate)
                {
                    _consecutiveErrors = 0;
                    _lastPoll = DateTimeOffset.Now;
                    if (_status == MailboxStatus.Error)
                        SetStatus(_agent is null ? MailboxStatus.Listening : MailboxStatus.Active, "اتصال دوباره برقرار شد");
                    else RefreshStatusText();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                int errors;
                lock (_gate)
                {
                    errors = ++_consecutiveErrors;
                    _lastError = ex.Message;
                    SetStatus(MailboxStatus.Error, ex is MailboxTransportException { Status: System.Net.HttpStatusCode.Unauthorized }
                        ? "توکن گیت‌هاب پذیرفته نشد — آن را در تنظیمات بررسی کنید"
                        : "خطا در دسترسی به صندوق فرمان: " + ex.Message);
                }
                if (errors == 1 || errors % 10 == 0)
                    _log.Append(LogLevel.Warning, LogTool, "poll", "خواندن صندوق فرمان ناموفق بود", ex is MailboxTransportException ? ex.Message : ex.ToString(), errorCode: "poll_failed", outcome: "failed");
                delay = TimeSpan.FromSeconds(Math.Min(120, delay.TotalSeconds * Math.Pow(2, Math.Min(errors, 4))));
            }

            try
            {
                await _pollNow.WaitAsync(delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private GitHubMailboxTransport GetTransport(AppSettings settings)
    {
        var token = _secrets.GetOrEmpty(SecretKeys.GitHubToken);
        var key = string.Join("|", settings.GitHub.Repository, settings.GitHub.Branch, settings.GitHub.MailboxPath, settings.GitHub.Proxy.Mode, settings.GitHub.Proxy.Url, token.Length, token.GetHashCode());
        lock (_gate)
        {
            if (_transport is null || key != _transportKey)
            {
                _transport?.Dispose();
                _transport = new GitHubMailboxTransport(settings.GitHub, token);
                _transportKey = key;
                _agentEtag = _inboxEtag = _fastInboxEtag = _fastOutboxSha = null;
                _appIdentityPublished = false;
            }
            return _transport;
        }
    }

    private async Task PollOnceAsync(AppSettings settings, CancellationToken ct)
    {
        var transport = GetTransport(settings);
        var tick = ++_tickCount;

        if (!_appIdentityPublished)
        {
            await PublishAppIdentityAsync(transport, ct).ConfigureAwait(false);
            _appIdentityPublished = true;
        }

        // 1) agent identity (checked when not yet connected or every 15 ticks so active fast-inbox polling uses only 1 ETag GET per tick)
        if (_agent is null || tick % 15 == 1)
        {
            var (agentText, _, agentEtag, agentNotModified) = await transport.ReadAsync(MailboxLayout.AgentIdentityFile, _agentEtag, ct).ConfigureAwait(false);
            if (!agentNotModified)
            {
                _agentEtag = agentEtag;
                HandleAgentIdentity(agentText);
            }
        }

        // 2) single-slot fast stream (fast-inbox.json → fast-outbox.json: 1 GET with ETag = 0 rate limit when 304, 1 PUT on reply, 0 DELETEs)
        var (fastText, _, fastEtag, fastNotModified) = await transport.ReadAsync(MailboxLayout.FastInboxFile, _fastInboxEtag, ct).ConfigureAwait(false);
        if (!fastNotModified)
        {
            if (!string.IsNullOrWhiteSpace(fastText))
                await ProcessFastInboxAsync(transport, fastText, ct).ConfigureAwait(false);
            _fastInboxEtag = fastEtag;
        }

        // 3) multi-file inbox fallback
        if (tick % 2 == 1)
        {
            var files = await transport.ListAsync(MailboxLayout.InboxDir, _inboxEtag, e => _inboxEtag = e, ct).ConfigureAwait(false);
            if (files is not null)
            {
                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!file.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                    await ProcessInboxFileAsync(transport, file, ct).ConfigureAwait(false);
                }
                if (files.Count > 0) _inboxEtag = null;
            }
        }

        // 4) heartbeat
        bool dirty;
        lock (_gate)
        {
            var every = _agent is null ? HeartbeatIdle : HeartbeatBusy;
            dirty = _heartbeatDirty || DateTimeOffset.UtcNow - _lastHeartbeat > every;
        }
        if (dirty) await WriteHeartbeatAsync(listening: true, ct).ConfigureAwait(false);
    }

    private void HandleAgentIdentity(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            IdentityFile? gone;
            lock (_gate)
            {
                gone = _agent;
                _agent = null;
                _session = "";
                if (gone is not null) { _heartbeatDirty = true; SetStatus(MailboxStatus.Listening, "ایجنت هویت خود را برداشت؛ منتظر ایجنت بعدی"); }
            }
            if (gone is not null) _log.Append(LogLevel.Info, LogTool, "identity", "هویت ایجنت از صندوق فرمان برداشته شد", gone.Fingerprint);
            return;
        }
        IdentityFile file;
        try
        {
            file = IdentityFile.Parse(text);
        }
        catch (Exception ex) when (ex is MailboxSecurityException or JsonException or FormatException or System.Security.Cryptography.CryptographicException)
        {
            _log.Append(LogLevel.Warning, LogTool, "identity", "پروندهٔ هویت ایجنت معتبر نیست", ex.Message, errorCode: "identity_invalid", outcome: "failed");
            return;
        }
        bool changed;
        lock (_gate)
        {
            changed = _agent is null || _agent.Fingerprint != file.Fingerprint;
            _agent = file;
            if (changed)
            {
                _session = Guid.NewGuid().ToString("N")[..12];
                _heartbeatDirty = true;
                SetStatus(MailboxStatus.Active, $"ایجنت «{file.Label}» وصل است — اثر انگشت {file.Fingerprint}");
            }
        }
        if (changed)
            _log.Append(new LogEvent { Level = LogLevel.Success, Tool = LogTool, Category = "identity", Message = "ایجنت وصل شد (بدون نیاز به اجازه — تصمیم مالک)", Detail = $"agent {file.Fingerprint} ({file.Label})", Decision = "trusted", SessionId = SessionId });
    }

    private async Task PublishAppIdentityAsync(GitHubMailboxTransport transport, CancellationToken ct)
    {
        var mine = IdentityFile.From(_identity!, "Bazino Marketing Studio " + _appVersion);
        var (text, sha, _, _) = await transport.ReadAsync(MailboxLayout.AppIdentityFile, null, ct).ConfigureAwait(false);
        if (text is not null)
        {
            try
            {
                var existing = IdentityFile.Parse(text);
                if (existing.Fingerprint == mine.Fingerprint) return;
            }
            catch (Exception ex) when (ex is MailboxSecurityException or JsonException or FormatException or System.Security.Cryptography.CryptographicException) { }
        }
        await transport.WriteAsync(MailboxLayout.AppIdentityFile, mine.ToJson(), "mailbox: app identity " + mine.Fingerprint, sha, ct).ConfigureAwait(false);
        _log.Append(LogLevel.Info, LogTool, "identity", "هویت عمومی برنامه در صندوق فرمان منتشر شد", mine.Fingerprint);
    }

    private async Task WriteHeartbeatAsync(bool listening, CancellationToken ct)
    {
        GitHubMailboxTransport? transport;
        MailboxState state;
        lock (_gate)
        {
            transport = _transport;
            if (transport is null || _identity is null) return;
            state = new MailboxState
            {
                Listening = listening,
                Status = listening ? _status.ToString().ToLowerInvariant() : "stopped",
                Session = _session,
                Agent = _agent?.Fingerprint ?? "",
                ApprovedUntil = null,
                App = _appVersion
            };
            state.Sign(_identity);
        }
        var (_, sha, _, _) = await transport.ReadAsync(MailboxLayout.StateFile, null, ct).ConfigureAwait(false);
        await transport.WriteAsync(MailboxLayout.StateFile, state.ToJson(), $"mailbox: state {state.Status}", sha, ct).ConfigureAwait(false);
        lock (_gate)
        {
            _lastHeartbeat = DateTimeOffset.UtcNow;
            _heartbeatDirty = false;
        }
    }

    private async Task ProcessFastInboxAsync(GitHubMailboxTransport transport, string text, CancellationToken ct)
    {
        Envelope env;
        try { env = Envelope.FromJson(text); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException) { return; }

        IdentityFile? approved;
        lock (_gate) approved = _agent;
        if (approved is null || env.From != approved.Fingerprint)
        {
            var (agentText, _, agentEtag, agentNotModified) = await transport.ReadAsync(MailboxLayout.AgentIdentityFile, _agentEtag, ct).ConfigureAwait(false);
            if (!agentNotModified) { _agentEtag = agentEtag; HandleAgentIdentity(agentText); }
            lock (_gate) approved = _agent;
        }
        if (approved is null || env.From != approved.Fingerprint) return;

        (string OutName, string ReplyJson, string Cmd, long OutSeq) pending;
        bool hasPending;
        bool duplicate;
        lock (_gate)
        {
            hasPending = _pendingReplies.TryGetValue(env.Id, out pending);
            duplicate = _ledger.Processed.ContainsKey(env.Id);
        }
        if (hasPending)
        {
            if (_fastOutboxSha is null)
            {
                var (_, currentSha, _, _) = await transport.ReadAsync(MailboxLayout.FastOutboxFile, null, ct).ConfigureAwait(false);
                _fastOutboxSha = currentSha;
            }
            _fastOutboxSha = await transport.WriteAsync(MailboxLayout.FastOutboxFile, pending.ReplyJson, $"mailbox: fast-reply {pending.Cmd} #{pending.OutSeq}", _fastOutboxSha, ct).ConfigureAwait(false);
            _ = TryPostWebhookReplyAsync(approved.WebhookUrl, env.Id, pending.ReplyJson);
            lock (_gate)
            {
                _pendingReplies.Remove(env.Id);
                _ledger.Processed[env.Id] = env.ExpiresAt;
                TrimLedger();
                SaveLedger();
            }
            return;
        }
        if (duplicate) return;

        var (response, cmd, risk) = await ExecuteEnvelopeAsync(approved, env, ct).ConfigureAwait(false);

        long outSeq;
        string replyJson;
        lock (_gate)
        {
            outSeq = ++_ledger.OutSeq;
            var reply = CryptoBox.Seal(_identity!, approved.ToPeerKeys(), response.ToJson(), "reply", risk, env.Session, outSeq, TimeSpan.FromHours(1), id: env.Id);
            replyJson = reply.ToJson();
            if (!_ledger.LastSeq.TryGetValue(env.Session, out var lastSeq) || env.Seq > lastSeq) _ledger.LastSeq[env.Session] = env.Seq;
            _pendingReplies[env.Id] = (MailboxLayout.FastOutboxFile, replyJson, cmd, outSeq);
            SaveLedger();
        }

        if (_fastOutboxSha is null)
        {
            var (_, currentSha, _, _) = await transport.ReadAsync(MailboxLayout.FastOutboxFile, null, ct).ConfigureAwait(false);
            _fastOutboxSha = currentSha;
        }
        _fastOutboxSha = await transport.WriteAsync(MailboxLayout.FastOutboxFile, replyJson, $"mailbox: fast-reply {cmd} #{outSeq}", _fastOutboxSha, ct).ConfigureAwait(false);
        _ = TryPostWebhookReplyAsync(approved.WebhookUrl, env.Id, replyJson);

        lock (_gate)
        {
            _pendingReplies.Remove(env.Id);
            _ledger.Processed[env.Id] = env.ExpiresAt;
            TrimLedger();
            SaveLedger();
        }
        RecordCommandActivity(response, cmd, risk, env);
    }

    private async Task ProcessInboxFileAsync(GitHubMailboxTransport transport, RemoteFile file, CancellationToken ct)
    {
        var relative = MailboxLayout.InboxDir + "/" + file.Name;
        var (text, sha, _, _) = await transport.ReadAsync(relative, null, ct).ConfigureAwait(false);
        if (text is null || sha is null) return; // vanished meanwhile

        Envelope env;
        try
        {
            env = Envelope.FromJson(text);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            _log.Append(LogLevel.Warning, LogTool, "inbox", "پیام صندوق فرمان قابل خواندن نبود و حذف شد", $"{file.Name}: {ex.Message}", errorCode: "malformed", outcome: "failed");
            await transport.DeleteAsync(relative, sha, "mailbox: drop malformed " + file.Name, ct).ConfigureAwait(false);
            return;
        }

        IdentityFile? approved;
        lock (_gate) approved = _agent;
        if (approved is null || env.From != approved.Fingerprint)
        {
            var (agentText, _, agentEtag, agentNotModified) = await transport.ReadAsync(MailboxLayout.AgentIdentityFile, _agentEtag, ct).ConfigureAwait(false);
            if (!agentNotModified) { _agentEtag = agentEtag; HandleAgentIdentity(agentText); }
            lock (_gate) approved = _agent;
        }

        if (approved is null || env.From != approved.Fingerprint)
        {
            // Not from the published agent identity: leave it (the identity file may still be on its way) unless it has expired.
            if (env.ExpiresAt + CryptoBox.MaxClockSkew < DateTimeOffset.UtcNow)
            {
                _log.Append(LogLevel.Warning, LogTool, "inbox", "پیام منقضی از فرستندهٔ ناشناس حذف شد", $"{file.Name} from {env.From}", errorCode: "unknown_sender");
                await transport.DeleteAsync(relative, sha, "mailbox: drop expired " + file.Name, ct).ConfigureAwait(false);
            }
            return;
        }

        (string OutName, string ReplyJson, string Cmd, long OutSeq) pending;
        bool hasPending;
        bool duplicate;
        lock (_gate)
        {
            hasPending = _pendingReplies.TryGetValue(env.Id, out pending);
            duplicate = _ledger.Processed.ContainsKey(env.Id);
        }
        if (hasPending)
        {
            var (_, existingReplySha, _, _) = await transport.ReadAsync(MailboxLayout.OutboxDir + "/" + pending.OutName, null, ct).ConfigureAwait(false);
            if (existingReplySha is null)
                await transport.WriteAsync(MailboxLayout.OutboxDir + "/" + pending.OutName, pending.ReplyJson, $"mailbox: reply {pending.Cmd} #{pending.OutSeq}", null, ct).ConfigureAwait(false);
            lock (_gate)
            {
                _pendingReplies.Remove(env.Id);
                _ledger.Processed[env.Id] = env.ExpiresAt;
                TrimLedger();
                SaveLedger();
            }
            await transport.DeleteAsync(relative, sha, $"mailbox: done {pending.Cmd} {env.Id[..Math.Min(8, env.Id.Length)]}", ct).ConfigureAwait(false);
            return;
        }
        if (duplicate)
        {
            // Already answered before a crash/restart: just remove it.
            await transport.DeleteAsync(relative, sha, "mailbox: drop duplicate " + file.Name, ct).ConfigureAwait(false);
            return;
        }

        var (response, cmd, risk) = await ExecuteEnvelopeAsync(approved, env, ct).ConfigureAwait(false);

        // Reply (sealed to the approved agent), then remove the request.
        long outSeq;
        Envelope reply;
        string outName;
        string replyJson;
        lock (_gate)
        {
            outSeq = ++_ledger.OutSeq;
            reply = CryptoBox.Seal(_identity!, approved.ToPeerKeys(), response.ToJson(), "reply", risk, env.Session, outSeq, TimeSpan.FromHours(1), id: null);
            outName = MailboxLayout.EnvelopeFileName(outSeq, env.Id);
            replyJson = reply.ToJson();
            _pendingReplies[env.Id] = (outName, replyJson, cmd, outSeq);
            if (!_ledger.LastSeq.TryGetValue(env.Session, out var lastSeq) || env.Seq > lastSeq) _ledger.LastSeq[env.Session] = env.Seq;
            SaveLedger();
        }
        await transport.WriteAsync(MailboxLayout.OutboxDir + "/" + outName, replyJson, $"mailbox: reply {cmd} #{outSeq}", null, ct).ConfigureAwait(false);
        _ = TryPostWebhookReplyAsync(approved.WebhookUrl, env.Id, replyJson);
        lock (_gate)
        {
            _pendingReplies.Remove(env.Id);
            _ledger.Processed[env.Id] = env.ExpiresAt;
            TrimLedger();
            SaveLedger();
        }
        await transport.DeleteAsync(relative, sha, $"mailbox: done {cmd} {env.Id[..Math.Min(8, env.Id.Length)]}", ct).ConfigureAwait(false);

        RecordCommandActivity(response, cmd, risk, env);
    }

    private async Task<(CommandResponse Response, string Cmd, string Risk)> ExecuteEnvelopeAsync(IdentityFile approved, Envelope env, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        CommandResponse response;
        string cmd = "?";
        string risk = env.Risk;
        try
        {
            string plaintext;
            lock (_gate)
            {
                if (_ledger.LastSeq.TryGetValue(env.Session, out var last) && env.Seq <= last)
                    throw new MailboxSecurityException("replay", $"sequence {env.Seq} not after {last}");
                plaintext = CryptoBox.Open(_identity!, approved.ToPeerKeys(), env, guard: _guard);
            }
            var request = CommandRequest.Parse(plaintext);
            cmd = string.IsNullOrWhiteSpace(request.Cmd) ? "?" : request.Cmd.Trim();
            var args = request.Args is { ValueKind: JsonValueKind.Object } a ? a : JsonDocument.Parse("{}").RootElement;
            var ctx = new CommandContext(cmd, args, env.Risk, request.Note ?? "", env.Id, env.Session, env.From);

            _log.Append(new LogEvent
            {
                Level = LogLevel.Info, Tool = LogTool, Category = "command", Message = $"فرمان «{cmd}» از ایجنت رسید",
                Detail = string.IsNullOrWhiteSpace(request.Note) ? "" : request.Note, RequestId = env.Id, SessionId = env.Session, Risk = env.Risk
            });

            if (cmd == Commands.SessionClose)
            {
                response = CommandResponse.Success(cmd, env.Id, new { closed = true }, sw.ElapsedMilliseconds, _appVersion);
            }
            else
            {
                var result = await _executor.ExecuteAsync(ctx, ct).ConfigureAwait(false);
                response = CommandResponse.Success(cmd, env.Id, result, sw.ElapsedMilliseconds, _appVersion);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (MailboxSecurityException ex)
        {
            response = CommandResponse.Failure(cmd, env.Id, "security:" + ex.Code, ex.Message, sw.ElapsedMilliseconds, _appVersion);
        }
        catch (CommandException ex)
        {
            response = CommandResponse.Failure(cmd, env.Id, ex.Code, ex.Message, sw.ElapsedMilliseconds, _appVersion);
        }
        catch (Exception ex)
        {
            response = CommandResponse.Failure(cmd, env.Id, "internal", ex.GetType().Name + ": " + ex.Message, sw.ElapsedMilliseconds, _appVersion);
        }
        sw.Stop();
        return (response, cmd, risk);
    }

    private void RecordCommandActivity(CommandResponse response, string cmd, string risk, Envelope env)
    {
        var summary = response.Ok ? "انجام شد" : $"ناموفق ({response.Error?.Code}): {response.Error?.Message}";
        var activity = new MailboxActivity(DateTimeOffset.Now, cmd, response.Ok, summary, response.DurationMs, risk);
        lock (_gate)
        {
            _activity.Insert(0, activity);
            if (_activity.Count > 50) _activity.RemoveAt(_activity.Count - 1);
        }
        _log.Append(new LogEvent
        {
            Level = response.Ok ? LogLevel.Success : LogLevel.Warning, Tool = LogTool, Category = "command",
            Message = response.Ok ? $"فرمان «{cmd}» انجام شد" : $"فرمان «{cmd}» ناموفق بود", Detail = response.Ok ? "" : summary,
            RequestId = env.Id, SessionId = env.Session, Risk = risk, DurationMs = response.DurationMs,
            Outcome = response.Ok ? "ok" : "failed", ErrorCode = response.Error?.Code
        });
        ActivityAdded?.Invoke(activity);
        Changed?.Invoke();
    }

    private async Task TryPostWebhookReplyAsync(string? webhookUrl, string inReplyTo, string replyEnvelopeJson)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl) || !Uri.TryCreate(webhookUrl.Trim().TrimEnd('/') + "/reply", UriKind.Absolute, out var uri))
            return;
        try
        {
            using var http = Http.HttpFactory.Create(_settings().GitHub.Proxy, TimeSpan.FromSeconds(5));
            var payload = $"{{\"inReplyTo\":\"{inReplyTo}\",\"envelope\":{replyEnvelopeJson}}}";
            using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            _ = await http.PostAsync(uri, content).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort webhook push; fast-outbox.json / outbox/*.json already holds the reply.
        }
    }

    // ---------------------------------------------------------------- identity / ledger

    private void EnsureIdentity()
    {
        if (_identity is not null) return;
        if (_secrets.TryGet(SecretKeys.MailboxIdentity, out var json) && !string.IsNullOrWhiteSpace(json))
        {
            try { _identity = MailboxIdentity.ImportPrivate(json); return; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _log.Append(LogLevel.Warning, LogTool, "identity", "هویت ذخیره‌شدهٔ برنامه قابل استفاده نبود؛ هویت تازه ساخته می‌شود", ex.Message, errorCode: "identity_reset");
            }
        }
        _identity = MailboxIdentity.Create();
        _secrets.Set(SecretKeys.MailboxIdentity, _identity.ExportPrivate());
        _log.Append(LogLevel.Info, LogTool, "identity", "هویت تازهٔ برنامه برای صندوق فرمان ساخته شد", _identity.Fingerprint);
    }

    private void LoadLedger()
    {
        try
        {
            if (File.Exists(_ledgerPath))
                _ledger = JsonSerializer.Deserialize<MailboxLedger>(File.ReadAllText(_ledgerPath), CryptoBox.JsonOptions) ?? new MailboxLedger();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _ledger = new MailboxLedger { OutSeq = DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 1_000_000 };
        }
    }

    private void SaveLedger()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_ledgerPath)!);
            File.WriteAllText(_ledgerPath, JsonSerializer.Serialize(_ledger, CryptoBox.JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Append(LogLevel.Warning, LogTool, "ledger", "ذخیرهٔ دفترچهٔ صندوق فرمان ناموفق بود", ex.Message);
        }
    }

    private void TrimLedger()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var k in _ledger.Processed.Where(kv => kv.Value + TimeSpan.FromHours(3) < now).Select(kv => kv.Key).ToList()) _ledger.Processed.Remove(k);
        if (_ledger.Processed.Count > 500)
            foreach (var k in _ledger.Processed.OrderBy(kv => kv.Value).Take(_ledger.Processed.Count - 500).Select(kv => kv.Key).ToList()) _ledger.Processed.Remove(k);
        if (_ledger.LastSeq.Count > 50)
            foreach (var k in _ledger.LastSeq.Keys.Take(_ledger.LastSeq.Count - 50).ToList()) _ledger.LastSeq.Remove(k);
    }

    // ---------------------------------------------------------------- status

    private void SetStatus(MailboxStatus status, string text)
    {
        // caller holds _gate
        _status = status;
        _statusText = text;
        var handler = Changed;
        if (handler is not null) ThreadPool.QueueUserWorkItem(_ => handler());
    }

    private void RefreshStatusText()
    {
        // caller holds _gate
        var handler = Changed;
        if (handler is not null) ThreadPool.QueueUserWorkItem(_ => handler());
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _transport?.Dispose();
        _identity?.Dispose();
        _pollNow.Dispose();
    }
}
