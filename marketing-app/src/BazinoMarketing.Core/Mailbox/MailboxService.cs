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
    /// <summary>Polling; no approved agent identity published yet.</summary>
    Listening,
    /// <summary>An approved agent identity is published and trusted; its commands are executed.</summary>
    Active,
    /// <summary>An agent identity is waiting for the owner's approval; its commands are refused until then.</summary>
    PendingApproval,
    /// <summary>Last poll failed (network/token); retrying with back-off.</summary>
    Error,
    /// <summary>Stopped by the owner or on exit.</summary>
    Stopped
}

/// <summary>One executed command, for the UI's recent-activity list.</summary>
public sealed record MailboxActivity(DateTimeOffset At, string Cmd, bool Ok, string Summary, long DurationMs, string Risk);

/// <summary>Live state of one mailbox source (one repository + branch pair), for the UI list.</summary>
public sealed record MailboxSourceStatus(
    string Key,
    string Repository,
    string Branch,
    string Label,
    bool Enabled,
    string StatusText,
    string AgentLabel,
    string AgentFingerprint,
    string AgentState);

/// <summary>Small persisted ledger so restarts never reuse an outbox sequence or re-run an already answered command.</summary>
internal sealed class MailboxLedger
{
    [JsonPropertyName("outSeq")] public long OutSeq { get; set; }
    [JsonPropertyName("processed")] public Dictionary<string, DateTimeOffset> Processed { get; set; } = new();
    [JsonPropertyName("lastSeq")] public Dictionary<string, long> LastSeq { get; set; } = new();
}

/// <summary>
/// The «صندوق فرمان»: polls every registered mailbox source — the primary repository/branch plus any extra branch the owner
/// added by pasting its public URL (owner decision 2026-10-04) — answers verified commands of the agent published there, and
/// publishes a signed heartbeat on each source.
///
/// Trust model (owner decision 2026-10-04): an agent fingerprint must be approved by the owner in the app before any command
/// runs. Unknown fingerprints are recorded as «در انتظار تأیید» and every command of theirs is refused with a clear message;
/// fingerprints the owner switched off are refused too. The owner is the only approval authority and no key or token is ever
/// written to the registry.
///
/// Thread-safety: public members may be called from any thread; events are raised on background threads.
/// </summary>
public sealed class MailboxService : IDisposable
{
    /// <summary>Heartbeat commits are kept rare: every 5 min while an agent is connected, every 30 min when merely listening.</summary>
    private static readonly TimeSpan HeartbeatBusy = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HeartbeatIdle = TimeSpan.FromMinutes(30);
    private const string LogTool = "mailbox";

    /// <summary>Per-source runtime: transport, ETags, the identity published on that source and its own reply bookkeeping.</summary>
    private sealed class SourceRuntime
    {
        public MailboxSourceSettings Settings { get; set; } = new();
        public string Key { get; set; } = "";
        public GitHubMailboxTransport? Transport { get; set; }
        public string? AgentEtag, InboxEtag, FastInboxEtag, FastOutboxSha;
        public IdentityFile? Agent;
        public string Session = "";
        public bool AppIdentityPublished;
        public string StatusText = "در حال اتصال…";
        public MailboxStatus Status = MailboxStatus.Listening;
        public DateTimeOffset LastHeartbeat = DateTimeOffset.MinValue;
        public bool HeartbeatDirty = true;
        public long Tick;
    }

    private readonly Func<AppSettings> _settings;
    private readonly ISecretStore _secrets;
    private readonly JsonlLogStore _log;
    private readonly string _appVersion;
    private readonly string _ledgerPath;
    private readonly CommandExecutor _executor;
    private readonly AgentRegistry _agents;
    private readonly object _gate = new();
    private readonly ReplayGuard _guard = new();
    private readonly List<SourceRuntime> _sources = new();
    private readonly Dictionary<string, (string OutName, string ReplyJson, string Cmd, long OutSeq)> _pendingReplies = new();
    private readonly List<MailboxActivity> _activity = new();
    private readonly SemaphoreSlim _pollNow = new(0, 1);

    private MailboxIdentity? _identity;
    private MailboxLedger _ledger = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    private long _tickCount;
    private string _session = "";
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
        _agents = new AgentRegistry(AgentRegistry.DefaultPath(dataFolder));
        _executor = executor;
        RebuildSources();
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

    /// <summary>Fingerprint of the first connected agent (kept for the connection card's summary line).</summary>
    public string AgentFingerprint { get { lock (_gate) return _sources.FirstOrDefault(s => s.Agent is not null)?.Agent?.Fingerprint ?? ""; } }
    public string AgentLabel { get { lock (_gate) return _sources.FirstOrDefault(s => s.Agent is not null)?.Agent?.Label ?? ""; } }

    /// <summary>True when at least one connected agent is approved (so the summary can say «وصل و تأییدشده»).</summary>
    public bool HasApprovedAgent { get { lock (_gate) return _sources.Any(s => s.Agent is not null && _agents.IsApproved(s.Agent.Fingerprint)); } }

    /// <summary>True when at least one connected agent is still waiting for the owner's approval.</summary>
    public bool HasPendingAgent { get { lock (_gate) return _sources.Any(s => s.Agent is not null && _agents.Get(s.Agent.Fingerprint)?.State == AgentTrustState.Pending); } }

    public IReadOnlyList<MailboxActivity> Activity { get { lock (_gate) return _activity.ToList(); } }

    /// <summary>The registered mailbox sources (one row per repository + branch) with their live state.</summary>
    public IReadOnlyList<MailboxSourceStatus> SourceStates
    {
        get
        {
            lock (_gate)
                return _sources.Select(s => new MailboxSourceStatus(
                    s.Key,
                    s.Settings.Repository,
                    s.Settings.Branch,
                    string.IsNullOrWhiteSpace(s.Settings.Label) ? $"{s.Settings.Repository} · {s.Settings.Branch}" : s.Settings.Label,
                    s.Settings.Enabled,
                    s.StatusText,
                    s.Agent?.Label ?? "",
                    s.Agent?.Fingerprint ?? "",
                    s.Agent is null ? "" : (_agents.Get(s.Agent.Fingerprint)?.State ?? AgentTrustState.Pending).ToString())).ToList();
        }
    }

    /// <summary>Identity (repository + branch + mailbox folder) of the primary source in the GitHub settings.</summary>
    public string PrimarySourceKey => _settings().GitHub.PrimarySource().Key;

    /// <summary>Every agent fingerprint the app has seen, with the owner's decision (approved / pending / disabled).</summary>
    public IReadOnlyList<AgentRecord> Agents => _agents.All;

    public AgentRecord? AgentRecordOf(string fingerprint) => _agents.Get(fingerprint);

    /// <summary>Owner action: approve an agent so its commands run with full access.</summary>
    public bool ApproveAgent(string fingerprint)
    {
        if (!_agents.Approve(fingerprint)) return false;
        _log.Append(new LogEvent { Level = LogLevel.Success, Tool = LogTool, Category = "identity", Message = "اثر انگشت ایجنت به دست مالک تأیید شد", Detail = fingerprint, Decision = "approved" });
        lock (_gate) _heartbeatDirtyAll();
        RecomputeStatus();
        return true;
    }

    /// <summary>Owner action: switch an agent off; its commands are refused until it is enabled again.</summary>
    public bool DisableAgent(string fingerprint)
    {
        if (!_agents.Disable(fingerprint)) return false;
        _log.Append(new LogEvent { Level = LogLevel.Warning, Tool = LogTool, Category = "identity", Message = "دسترسی ایجنت به دست مالک غیرفعال شد", Detail = fingerprint, Decision = "disabled" });
        lock (_gate) _heartbeatDirtyAll();
        RecomputeStatus();
        return true;
    }

    /// <summary>Owner action: enable a previously disabled agent (approval stays valid).</summary>
    public bool EnableAgent(string fingerprint) => ApproveAgent(fingerprint);

    /// <summary>Owner action: forget an agent entirely; if it is still published in the mailbox it will reappear as pending.</summary>
    public bool ForgetAgent(string fingerprint)
    {
        if (!_agents.Forget(fingerprint)) return false;
        _log.Append(new LogEvent { Level = LogLevel.Info, Tool = LogTool, Category = "identity", Message = "اثر انگشت ایجنت از دفتر برنامه حذف شد", Detail = fingerprint });
        RecomputeStatus();
        return true;
    }

    /// <summary>True when the settings + token allow listening to at least one source.</summary>
    public bool CanStart(out string reason)
    {
        var s = _settings();
        var sources = s.GitHub.EffectiveSources();
        if (sources.Count == 0) { reason = "هیچ مخزن/شاخه‌ای ثبت نشده است"; return false; }
        if (sources.All(x => string.IsNullOrWhiteSpace(x.Repository) || !Tools.GitHubClient.IsValidRepository(x.Repository)))
        {
            reason = "مخزن گیت‌هاب تنظیم نشده است";
            return false;
        }
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
            RebuildSources();
            _cts = new CancellationTokenSource();
            _heartbeatDirtyAll();
            if (CanStart(out var reason))
                SetStatus(MailboxStatus.Listening, "در حال اتصال به صندوق فرمان…");
            else
                SetStatus(MailboxStatus.Off, reason);
            var token = _cts.Token;
            _loop = Task.Run(() => LoopAsync(token), CancellationToken.None);
        }
        _log.Append(LogLevel.Info, LogTool, "start", "گوش‌دادن به صندوق فرمان آغاز شد", $"app fingerprint {AppFingerprint}");
    }

    /// <summary>Stops polling and (best effort, bounded by <paramref name="grace"/>) publishes listening=false on every source.</summary>
    public async Task StopAsync(TimeSpan grace)
    {
        Task? loop;
        CancellationTokenSource? cts;
        List<SourceRuntime> sources;
        lock (_gate)
        {
            loop = _loop;
            cts = _cts;
            _loop = null;
            _cts = null;
            sources = _sources.ToList();
        }
        if (loop is null || cts is null) return;
        cts.Cancel();
        try { await loop.WaitAsync(grace).ConfigureAwait(false); } catch (Exception) { }
        lock (_gate) SetStatus(MailboxStatus.Stopped, "گوش‌دادن متوقف شد");
        try
        {
            using var stopCts = new CancellationTokenSource(grace);
            foreach (var source in sources)
            {
                try { await WriteHeartbeatAsync(source, listening: false, stopCts.Token).ConfigureAwait(false); }
                catch (Exception) { }
            }
        }
        catch (Exception) { }
        cts.Dispose();
    }

    /// <summary>Forces the next poll to run immediately (after settings/token/source changes or a button click).</summary>
    public void PollNow()
    {
        try { _pollNow.Release(); } catch (SemaphoreFullException) { }
    }

    /// <summary>Re-reads the source list from the settings (called after the owner adds or removes a branch).</summary>
    public void ReloadSources()
    {
        lock (_gate) RebuildSources();
        PollNow();
        RecomputeStatus();
    }

    // ---------------------------------------------------------------- loop

    /// <summary>Caller holds <see cref="_gate"/>.</summary>
    private void RebuildSources()
    {
        var settings = _settings();
        var wanted = settings.GitHub.EffectiveSources();
        var keep = new List<SourceRuntime>();
        foreach (var source in wanted)
        {
            var key = source.Key;
            var existing = _sources.FirstOrDefault(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                existing.Settings = source;
                keep.Add(existing);
                continue;
            }
            var runtime = new SourceRuntime { Settings = source, Key = key };
            try { runtime.Transport = new GitHubMailboxTransport(settings.GitHub.ForSource(source), _secrets.GetOrEmpty(SecretKeys.GitHubToken)); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { runtime.StatusText = "آدرس این شاخه معتبر نیست"; }
            keep.Add(runtime);
        }
        foreach (var gone in _sources.Where(s => !keep.Contains(s)))
        {
            gone.Transport?.Dispose();
            _log.Append(LogLevel.Info, LogTool, "source", "شاخه از فهرست صندوق فرمان برداشته شد", gone.Key);
        }
        _sources.Clear();
        _sources.AddRange(keep);
    }

    /// <summary>Caller holds <see cref="_gate"/>: marks every source for a heartbeat refresh (identity/status changed).</summary>
    private void _heartbeatDirtyAll()
    {
        foreach (var source in _sources) source.HeartbeatDirty = true;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var settings = _settings();
            var configuredSeconds = Math.Clamp(settings.GitHub.PollSeconds, 1, 300);
            var anyAgent = false;
            lock (_gate) anyAgent = _sources.Any(s => s.Agent is not null);
            var delay = anyAgent
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
                EnsureIdentity();
                Volatile.Write(ref _busy, 1);
                try
                {
                    List<SourceRuntime> sources;
                    lock (_gate)
                    {
                        if (_sources.Count == 0) RebuildSources();
                        sources = _sources.ToList();
                    }
                    foreach (var source in sources)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!source.Settings.Enabled) { source.StatusText = "خاموش — به دست مالک"; continue; }
                        await PollSourceAsync(source, settings, ct).ConfigureAwait(false);
                    }
                    lock (_gate)
                    {
                        _consecutiveErrors = 0;
                        _lastPoll = DateTimeOffset.Now;
                        _lastError = "";
                        RecomputeStatus();
                    }
                }
                finally { Volatile.Write(ref _busy, 0); }
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

    private async Task PollSourceAsync(SourceRuntime source, AppSettings settings, CancellationToken ct)
    {
        var transport = source.Transport;
        if (transport is null) return;
        var tick = ++source.Tick;

        if (!source.AppIdentityPublished)
        {
            await PublishAppIdentityAsync(source, ct).ConfigureAwait(false);
            source.AppIdentityPublished = true;
        }

        // 1) agent identity (checked when not yet connected or every 15 ticks so active fast-inbox polling uses only 1 ETag GET per tick)
        if (source.Agent is null || tick % 15 == 1)
        {
            var (agentText, _, agentEtag, agentNotModified) = await transport.ReadAsync(MailboxLayout.AgentIdentityFile, source.AgentEtag, ct).ConfigureAwait(false);
            if (!agentNotModified)
            {
                source.AgentEtag = agentEtag;
                HandleAgentIdentity(source, agentText);
            }
        }

        // 2) single-slot fast stream (fast-inbox.json → fast-outbox.json: 1 GET with ETag = 0 rate limit when 304, 1 PUT on reply, 0 DELETEs)
        var (fastText, _, fastEtag, fastNotModified) = await transport.ReadAsync(MailboxLayout.FastInboxFile, source.FastInboxEtag, ct).ConfigureAwait(false);
        if (!fastNotModified)
        {
            if (!string.IsNullOrWhiteSpace(fastText))
                await ProcessFastInboxAsync(source, fastText, ct).ConfigureAwait(false);
            source.FastInboxEtag = fastEtag;
        }

        // 3) multi-file inbox fallback
        if (tick % 2 == 1)
        {
            var etagHolder = source;
            var files = await transport.ListAsync(MailboxLayout.InboxDir, source.InboxEtag, e => etagHolder.InboxEtag = e, ct).ConfigureAwait(false);
            if (files is not null)
            {
                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!file.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                    await ProcessInboxFileAsync(source, file, ct).ConfigureAwait(false);
                }
                if (files.Count > 0) source.InboxEtag = null;
            }
        }

        // 4) heartbeat
        bool dirty;
        lock (_gate)
        {
            var every = source.Agent is null ? HeartbeatIdle : HeartbeatBusy;
            dirty = source.HeartbeatDirty || DateTimeOffset.UtcNow - source.LastHeartbeat > every;
        }
        if (dirty) await WriteHeartbeatAsync(source, listening: true, ct).ConfigureAwait(false);
    }

    private void HandleAgentIdentity(SourceRuntime source, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            IdentityFile? gone;
            lock (_gate)
            {
                gone = source.Agent;
                source.Agent = null;
                if (gone is not null) { source.HeartbeatDirty = true; source.StatusText = "منتظر ایجنت — هویت خود را برداشت"; }
            }
            if (gone is not null) _log.Append(LogLevel.Info, LogTool, "identity", "هویت ایجنت از یک شاخه برداشته شد", $"{gone.Fingerprint} @ {source.Key}");
            RecomputeStatus();
            return;
        }
        IdentityFile file;
        try
        {
            file = IdentityFile.Parse(text);
        }
        catch (Exception ex) when (ex is MailboxSecurityException or JsonException or FormatException or System.Security.Cryptography.CryptographicException)
        {
            _log.Append(LogLevel.Warning, LogTool, "identity", "پروندهٔ هویت ایجنت معتبر نیست", $"{source.Key}: {ex.Message}", errorCode: "identity_invalid", outcome: "failed");
            return;
        }
        bool changed;
        var previous = _agents.Get(file.Fingerprint)?.State;
        var record = _agents.Observe(file.Fingerprint, file.Label, source.Key);
        lock (_gate)
        {
            changed = source.Agent is null || source.Agent.Fingerprint != file.Fingerprint;
            source.Agent = file;
            if (changed) { source.Session = Guid.NewGuid().ToString("N")[..12]; source.HeartbeatDirty = true; }
            if (record.State == AgentTrustState.Approved) source.StatusText = $"ایجنت «{file.Label}» وصل و تأییدشده است";
            else if (record.State == AgentTrustState.Disabled) source.StatusText = $"ایجنت «{file.Label}» به دست مالک غیرفعال شده است";
            else source.StatusText = $"ایجنت «{file.Label}» در انتظار تأیید مالک است";
        }
        if (changed || previous != record.State)
        {
            var approved = record.State == AgentTrustState.Approved;
            _log.Append(new LogEvent
            {
                Level = approved ? LogLevel.Success : LogLevel.Warning,
                Tool = LogTool,
                Category = "identity",
                Message = approved ? "ایجنت تأییدشده وصل شد؛ فرمان‌هایش اجرا می‌شوند" : record.State == AgentTrustState.Disabled
                    ? "ایجنت غیرفعال تلاش کرد وصل شود؛ فرمان‌هایش رد می‌شوند"
                    : "ایجنت تازه در انتظار تأیید مالک است؛ فرمان‌هایش رد می‌شوند",
                Detail = $"agent {file.Fingerprint} ({file.Label}) @ {source.Key}",
                Decision = record.State.ToString().ToLowerInvariant(),
                SessionId = source.Session
            });
        }
        RecomputeStatus();
    }

    private async Task PublishAppIdentityAsync(SourceRuntime source, CancellationToken ct)
    {
        var transport = source.Transport;
        if (transport is null || _identity is null) return;
        var mine = IdentityFile.From(_identity, "Bazino Marketing Studio " + _appVersion);
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
        _log.Append(LogLevel.Info, LogTool, "identity", "هویت عمومی برنامه در یک شاخه منتشر شد", $"{mine.Fingerprint} @ {source.Key}");
    }

    private async Task WriteHeartbeatAsync(SourceRuntime source, bool listening, CancellationToken ct)
    {
        var transport = source.Transport;
        if (transport is null) return;
        MailboxState state;
        lock (_gate)
        {
            if (_identity is null) return;
            state = new MailboxState
            {
                Listening = listening,
                Status = listening ? source.Status.ToString().ToLowerInvariant() : "stopped",
                Session = source.Session,
                Agent = source.Agent?.Fingerprint ?? "",
                ApprovedUntil = null,
                App = _appVersion
            };
            state.Sign(_identity);
        }
        var (_, sha, _, _) = await transport.ReadAsync(MailboxLayout.StateFile, null, ct).ConfigureAwait(false);
        await transport.WriteAsync(MailboxLayout.StateFile, state.ToJson(), $"mailbox: state {state.Status}", sha, ct).ConfigureAwait(false);
        lock (_gate)
        {
            source.LastHeartbeat = DateTimeOffset.UtcNow;
            source.HeartbeatDirty = false;
        }
    }

    /// <summary>
    /// Decides whether a command from <paramref name="agent"/> may run. The owner is the only approval authority: a pending
    /// fingerprint gets a refusal reply that tells the agent to ask the owner, a disabled one gets a refusal too.
    /// </summary>
    private (bool Allowed, CommandResponse? Denial) Authorize(SourceRuntime source, IdentityFile agent, Envelope env, string cmd)
    {
        var record = _agents.Observe(agent.Fingerprint, agent.Label, source.Key);
        if (record.State == AgentTrustState.Approved) return (true, null);
        var message = record.State == AgentTrustState.Disabled
            ? "دسترسی این اثر انگشت به دست مالک غیرفعال شده است؛ تا فعال‌سازی دوباره در برنامهٔ ژینوس، هیچ فرمانی اجرا نمی‌شود."
            : "این اثر انگشت هنوز به دست مالک تأیید نشده است؛ در برنامهٔ ژینوس، صفحهٔ «اتصال» → کارت «صندوق فرمان» → بخش «ایجنت‌های ثبت‌شده»، آن را تأیید کنید.";
        var response = CommandResponse.Failure(cmd, env.Id, record.State == AgentTrustState.Disabled ? "agent_disabled" : "agent_not_approved", message, 0, _appVersion);
        _log.Append(new LogEvent
        {
            Level = LogLevel.Warning, Tool = LogTool, Category = "command",
            Message = record.State == AgentTrustState.Disabled ? "فرمان ایجنت غیرفعال رد شد" : "فرمان ایجنت تأییدنشده رد شد",
            Detail = $"agent {agent.Fingerprint} ({agent.Label}) @ {source.Key} cmd {cmd}", RequestId = env.Id, SessionId = env.Session,
            Risk = env.Risk, Outcome = "failed", ErrorCode = record.State == AgentTrustState.Disabled ? "agent_disabled" : "agent_not_approved"
        });
        return (false, response);
    }

    private async Task ProcessFastInboxAsync(SourceRuntime source, string text, CancellationToken ct)
    {
        var transport = source.Transport;
        if (transport is null) return;
        Envelope env;
        try { env = Envelope.FromJson(text); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException) { return; }

        IdentityFile? agent;
        lock (_gate) agent = source.Agent;
        if (agent is null || env.From != agent.Fingerprint)
        {
            var (agentText, _, agentEtag, agentNotModified) = await transport.ReadAsync(MailboxLayout.AgentIdentityFile, source.AgentEtag, ct).ConfigureAwait(false);
            if (!agentNotModified) { source.AgentEtag = agentEtag; HandleAgentIdentity(source, agentText); }
            lock (_gate) agent = source.Agent;
        }
        if (agent is null || env.From != agent.Fingerprint) return;

        (string OutName, string ReplyJson, string Cmd, long OutSeq) pending;
        var idKey = source.Key + "|" + env.Id;
        bool hasPending;
        bool duplicate;
        lock (_gate)
        {
            hasPending = _pendingReplies.TryGetValue(idKey, out pending);
            duplicate = _ledger.Processed.ContainsKey(idKey);
        }
        if (hasPending)
        {
            if (source.FastOutboxSha is null)
            {
                var (_, currentSha, _, _) = await transport.ReadAsync(MailboxLayout.FastOutboxFile, null, ct).ConfigureAwait(false);
                source.FastOutboxSha = currentSha;
            }
            source.FastOutboxSha = await transport.WriteAsync(MailboxLayout.FastOutboxFile, pending.ReplyJson, $"mailbox: fast-reply {pending.Cmd} #{pending.OutSeq}", source.FastOutboxSha, ct).ConfigureAwait(false);
            _ = TryPostWebhookReplyAsync(agent.WebhookUrl, env.Id, pending.ReplyJson);
            lock (_gate)
            {
                _pendingReplies.Remove(idKey);
                _ledger.Processed[idKey] = env.ExpiresAt;
                TrimLedger();
                SaveLedger();
            }
            return;
        }
        if (duplicate) return;

        var (response, cmd, risk) = await AuthorizeAndExecuteAsync(source, agent, env, ct).ConfigureAwait(false);

        long outSeq;
        string replyJson;
        lock (_gate)
        {
            outSeq = ++_ledger.OutSeq;
            var reply = CryptoBox.Seal(_identity!, agent.ToPeerKeys(), response.ToJson(), "reply", risk, env.Session, outSeq, TimeSpan.FromHours(1), id: env.Id);
            replyJson = reply.ToJson();
            var sessionKey = source.Key + "|" + env.Session;
            if (!_ledger.LastSeq.TryGetValue(sessionKey, out var lastSeq) || env.Seq > lastSeq) _ledger.LastSeq[sessionKey] = env.Seq;
            _pendingReplies[idKey] = (MailboxLayout.FastOutboxFile, replyJson, cmd, outSeq);
            SaveLedger();
        }

        if (source.FastOutboxSha is null)
        {
            var (_, currentSha, _, _) = await transport.ReadAsync(MailboxLayout.FastOutboxFile, null, ct).ConfigureAwait(false);
            source.FastOutboxSha = currentSha;
        }
        source.FastOutboxSha = await transport.WriteAsync(MailboxLayout.FastOutboxFile, replyJson, $"mailbox: fast-reply {cmd} #{outSeq}", source.FastOutboxSha, ct).ConfigureAwait(false);
        _ = TryPostWebhookReplyAsync(agent.WebhookUrl, env.Id, replyJson);

        lock (_gate)
        {
            _pendingReplies.Remove(idKey);
            _ledger.Processed[idKey] = env.ExpiresAt;
            TrimLedger();
            SaveLedger();
        }
        RecordCommandActivity(response, cmd, risk, env);
    }

    private async Task ProcessInboxFileAsync(SourceRuntime source, RemoteFile file, CancellationToken ct)
    {
        var transport = source.Transport;
        if (transport is null) return;
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
            _log.Append(LogLevel.Warning, LogTool, "inbox", "پیام صندوق فرمان قابل خواندن نبود و حذف شد", $"{source.Key}: {file.Name}: {ex.Message}", errorCode: "malformed", outcome: "failed");
            await transport.DeleteAsync(relative, sha, "mailbox: drop malformed " + file.Name, ct).ConfigureAwait(false);
            return;
        }

        IdentityFile? agent;
        lock (_gate) agent = source.Agent;
        if (agent is null || env.From != agent.Fingerprint)
        {
            var (agentText, _, agentEtag, agentNotModified) = await transport.ReadAsync(MailboxLayout.AgentIdentityFile, source.AgentEtag, ct).ConfigureAwait(false);
            if (!agentNotModified) { source.AgentEtag = agentEtag; HandleAgentIdentity(source, agentText); }
            lock (_gate) agent = source.Agent;
        }

        if (agent is null || env.From != agent.Fingerprint)
        {
            // Not from the identity published on this branch: leave it (the identity file may still be on its way) unless it has expired.
            if (env.ExpiresAt + CryptoBox.MaxClockSkew < DateTimeOffset.UtcNow)
            {
                _log.Append(LogLevel.Warning, LogTool, "inbox", "پیام منقضی از فرستندهٔ ناشناس حذف شد", $"{source.Key}: {file.Name} from {env.From}", errorCode: "unknown_sender");
                await transport.DeleteAsync(relative, sha, "mailbox: drop expired " + file.Name, ct).ConfigureAwait(false);
            }
            return;
        }

        var idKey = source.Key + "|" + env.Id;
        (string OutName, string ReplyJson, string Cmd, long OutSeq) pending;
        bool hasPending;
        bool duplicate;
        lock (_gate)
        {
            hasPending = _pendingReplies.TryGetValue(idKey, out pending);
            duplicate = _ledger.Processed.ContainsKey(idKey);
        }
        if (hasPending)
        {
            var (_, existingReplySha, _, _) = await transport.ReadAsync(MailboxLayout.OutboxDir + "/" + pending.OutName, null, ct).ConfigureAwait(false);
            if (existingReplySha is null)
                await transport.WriteAsync(MailboxLayout.OutboxDir + "/" + pending.OutName, pending.ReplyJson, $"mailbox: reply {pending.Cmd} #{pending.OutSeq}", null, ct).ConfigureAwait(false);
            lock (_gate)
            {
                _pendingReplies.Remove(idKey);
                _ledger.Processed[idKey] = env.ExpiresAt;
                TrimLedger();
                SaveLedger();
            }
            await transport.DeleteAsync(relative, sha, $"mailbox: done {pending.Cmd} {Short(env.Id)}", ct).ConfigureAwait(false);
            return;
        }
        if (duplicate)
        {
            // Already answered before a crash/restart: just remove it.
            await transport.DeleteAsync(relative, sha, "mailbox: drop duplicate " + file.Name, ct).ConfigureAwait(false);
            return;
        }

        var (response, cmd, risk) = await AuthorizeAndExecuteAsync(source, agent, env, ct).ConfigureAwait(false);

        // Reply (sealed to the agent), then remove the request.
        long outSeq;
        Envelope reply;
        string outName;
        string replyJson;
        lock (_gate)
        {
            outSeq = ++_ledger.OutSeq;
            reply = CryptoBox.Seal(_identity!, agent.ToPeerKeys(), response.ToJson(), "reply", risk, env.Session, outSeq, TimeSpan.FromHours(1), id: null);
            outName = MailboxLayout.EnvelopeFileName(outSeq, env.Id);
            replyJson = reply.ToJson();
            _pendingReplies[idKey] = (outName, replyJson, cmd, outSeq);
            var sessionKey = source.Key + "|" + env.Session;
            if (!_ledger.LastSeq.TryGetValue(sessionKey, out var lastSeq) || env.Seq > lastSeq) _ledger.LastSeq[sessionKey] = env.Seq;
            SaveLedger();
        }
        await transport.WriteAsync(MailboxLayout.OutboxDir + "/" + outName, replyJson, $"mailbox: reply {cmd} #{outSeq}", null, ct).ConfigureAwait(false);
        _ = TryPostWebhookReplyAsync(agent.WebhookUrl, env.Id, replyJson);
        lock (_gate)
        {
            _pendingReplies.Remove(idKey);
            _ledger.Processed[idKey] = env.ExpiresAt;
            TrimLedger();
            SaveLedger();
        }
        await transport.DeleteAsync(relative, sha, $"mailbox: done {cmd} {Short(env.Id)}", ct).ConfigureAwait(false);

        RecordCommandActivity(response, cmd, risk, env);
    }

    private static string Short(string id) => id.Length <= 8 ? id : id[..8];

    /// <summary>Authorizes the sender first (owner approval), then either runs the command or returns the refusal response.</summary>
    private async Task<(CommandResponse Response, string Cmd, string Risk)> AuthorizeAndExecuteAsync(SourceRuntime source, IdentityFile agent, Envelope env, CancellationToken ct)
    {
        var cmdName = "?";
        try
        {
            // The command name lives inside the sealed payload; read it only for the refusal text (the payload is verified by CryptoBox.Open).
            cmdName = PeekCommand(agent, env);
        }
        catch (Exception) { /* refusal text falls back to "?" */ }

        var (allowed, denial) = Authorize(source, agent, env, cmdName);
        if (!allowed && denial is not null) return (denial, cmdName, env.Risk);
        return await ExecuteEnvelopeAsync(source, agent, env, ct).ConfigureAwait(false);
    }

    /// <summary>Best-effort read of the command name for the refusal reply; the real verification happens in <see cref="ExecuteEnvelopeAsync"/>.</summary>
    private string PeekCommand(IdentityFile agent, Envelope env)
    {
        try
        {
            var plaintext = CryptoBox.Open(_identity!, agent.ToPeerKeys(), env, guard: null);
            var request = CommandRequest.Parse(plaintext);
            return string.IsNullOrWhiteSpace(request.Cmd) ? "?" : request.Cmd.Trim();
        }
        catch (Exception)
        {
            return "?";
        }
    }

    private async Task<(CommandResponse Response, string Cmd, string Risk)> ExecuteEnvelopeAsync(SourceRuntime source, IdentityFile agent, Envelope env, CancellationToken ct)
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
                var sessionKey = source.Key + "|" + env.Session;
                if (_ledger.LastSeq.TryGetValue(sessionKey, out var last) && env.Seq <= last)
                    throw new MailboxSecurityException("replay", $"sequence {env.Seq} not after {last}");
                plaintext = CryptoBox.Open(_identity!, agent.ToPeerKeys(), env, guard: _guard);
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
        lock (_gate)
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
            if (string.IsNullOrEmpty(_session)) _session = Guid.NewGuid().ToString("N")[..12];
        }
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

    /// <summary>Caller holds <see cref="_gate"/>: aggregate the per-source states into the single status the UI shows.</summary>
    private void RecomputeStatus()
    {
        if (_sources.Count == 0) return;
        var enabled = _sources.Where(s => s.Settings.Enabled).ToList();
        if (enabled.Count == 0) { SetStatus(MailboxStatus.Off, "همهٔ شاخه‌ها خاموش‌اند"); return; }
        var approved = enabled.Where(s => s.Agent is not null && _agents.IsApproved(s.Agent.Fingerprint)).ToList();
        var pending = enabled.Where(s => s.Agent is not null && _agents.Get(s.Agent.Fingerprint)?.State == AgentTrustState.Pending).ToList();
        var disabled = enabled.Where(s => s.Agent is not null && _agents.Get(s.Agent.Fingerprint)?.State == AgentTrustState.Disabled).ToList();
        if (approved.Count > 0)
        {
            var names = string.Join("، ", approved.Select(s => s.Agent!.Label));
            SetStatus(MailboxStatus.Active, (_sources.Count > 1 ? $"{PersianDigits(approved.Count)} ایجنت تأییدشده وصل است ({names})؛ فرمان‌هایشان اجرا می‌شوند." : $"ایجنت «{names}» تأییدشده وصل است؛ فرمان‌هایش اجرا می‌شوند."));
            return;
        }
        if (pending.Count > 0)
        {
            var names = string.Join("، ", pending.Select(s => s.Agent!.Label));
            SetStatus(MailboxStatus.PendingApproval, $"ایجنت «{names}» در انتظار تأیید مالک است؛ فرمان‌هایش تا تأیید شما اجرا نمی‌شود.");
            return;
        }
        if (disabled.Count > 0)
        {
            var names = string.Join("، ", disabled.Select(s => s.Agent!.Label));
            SetStatus(MailboxStatus.Listening, $"ایجنت «{names}» به دست شما غیرفعال شده است؛ فرمانی اجرا نمی‌شود.");
            return;
        }
        SetStatus(MailboxStatus.Listening, _sources.Count > 1
            ? $"روی {PersianDigits(_sources.Count)} شاخه گوش می‌دهم — هنوز ایجنتی هویتش را نگذاشته است."
            : "در حال گوش‌دادن — هنوز ایجنتی هویتش را نگذاشته است.");
    }

    private static string PersianDigits(int value)
    {
        var text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var digits = "۰۱۲۳۴۵۶۷۸۹";
        return string.Concat(text.Select(c => char.IsDigit(c) ? digits[c - '0'] : c));
    }

    private void SetStatus(MailboxStatus status, string text)
    {
        // caller holds _gate
        _status = status;
        _statusText = text;
        var handler = Changed;
        if (handler is not null) ThreadPool.QueueUserWorkItem(_ => handler());
    }

    public void Dispose()
    {
        _cts?.Cancel();
        lock (_gate)
        {
            foreach (var source in _sources) source.Transport?.Dispose();
        }
        _identity?.Dispose();
        _pollNow.Dispose();
    }
}
