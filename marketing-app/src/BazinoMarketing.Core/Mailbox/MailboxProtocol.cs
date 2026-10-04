using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace BazinoMarketing.Core.Mailbox;

/// <summary>
/// Layout of the command mailbox inside the Git branch (all paths relative to <c>GitHubSettings.MailboxPath</c>):
/// <list type="bullet">
/// <item><c>app-identity.json</c> — public keys of this app (written by the app).</item>
/// <item><c>agent-identity.json</c> — public keys of the agent session (written by the agent; the owner approves its fingerprint).</item>
/// <item><c>inbox/NNNNNN-id.json</c> — sealed commands agent → app (deleted by the app once answered).</item>
/// <item><c>outbox/NNNNNN-id.json</c> — sealed replies app → agent (deleted by the agent once read).</item>
/// <item><c>state.json</c> — signed, unencrypted heartbeat so the agent can see whether the app listens/approved.</item>
/// </list>
/// </summary>
public static class MailboxLayout
{
    public const string AppIdentityFile = "app-identity.json";
    public const string AgentIdentityFile = "agent-identity.json";
    public const string StateFile = "state.json";
    public const string InboxDir = "inbox";
    public const string OutboxDir = "outbox";
    public const string FastInboxFile = "fast-inbox.json";
    public const string FastOutboxFile = "fast-outbox.json";

    public static string EnvelopeFileName(long seq, string id) => $"{seq:D6}-{id}.json";
}

/// <summary>Risk classes. Informational only: the owner decided (2026-09-27) that no command needs his approval while the app runs.</summary>
public static class Risk
{
    public const string Read = "read";
    public const string Write = "write";
    public const string Os = "os";
    public const string Publish = "publish";
    public const string Spend = "spend";
    public static readonly string[] All = { Read, Write, Os, Publish, Spend };
}

/// <summary>Commands the app understands. Everything else is answered with <c>unknown_command</c>.</summary>
public static class Commands
{
    public const string Ping = "ping";
    public const string AppUpdate = "app.update";
    public const string AppRestart = "app.restart";
    public const string Diag = "diag";
    public const string ToolCheck = "tool.check";
    public const string KlingCli = "kling.cli";
    public const string LogTail = "log.tail";
    public const string SettingsGet = "settings.get";
    public const string SettingsSet = "settings.set";
    public const string SecretStatus = "secret.status";
    public const string SecretSet = "secret.set";
    public const string ImportLegacy = "import.legacy";
    public const string OsRun = "os.run";
    public const string SessionClose = "session.close";

    // ---- Zernio (read-only listing of connected accounts and posts) ----
    public const string ZernioAccounts = "zernio.accounts";
    public const string ZernioPosts = "zernio.posts";
    public const string ZernioCommentAutomationCreate = "zernio.comment-automation.create";

    // ---- Browser bridge (the agent's connection to the owner's Chrome, inside this app) ----
    public const string BrowserStatus = "browser.status";
    public const string BrowserLaunch = "browser.launch";
    public const string BrowserTargets = "browser.targets";
    public const string BrowserSelect = "browser.select";
    public const string BrowserOpen = "browser.open";
    public const string BrowserNavigate = "browser.nav";
    public const string BrowserRead = "browser.read";
    public const string BrowserEval = "browser.eval";
    public const string BrowserClose = "browser.close";

    // ---- Owner-selected local media and the in-app FFmpeg studio ----
    public const string InstagramDownload = "instagram.download";
    public const string MediaList = "media.list";
    public const string MediaUpload = "media.upload";
    public const string MediaFfmpeg = "media.ffmpeg";
    public const string MediaTranscribe = "media.transcribe";

    /// <summary>Every command the app knows, in the order the owner's command list shows them.</summary>
    public static readonly string[] All =
    {
        Ping, AppUpdate, AppRestart, Diag, ToolCheck, KlingCli, LogTail, SettingsGet, SettingsSet,
        SecretStatus, SecretSet, ImportLegacy, OsRun, ZernioAccounts, ZernioPosts, ZernioCommentAutomationCreate,
        BrowserStatus, BrowserLaunch, BrowserTargets, BrowserSelect,
        BrowserOpen, BrowserNavigate, BrowserRead, BrowserEval, BrowserClose,
        InstagramDownload, MediaList, MediaUpload, MediaFfmpeg, MediaTranscribe, SessionClose
    };

    /// <summary>Nominal risk of a command (for the log). Nothing is gated on it.</summary>
    public static string RequiredRisk(string cmd) => cmd switch
    {
        SettingsSet or SecretSet or ImportLegacy or AppUpdate or InstagramDownload or MediaUpload or MediaFfmpeg or MediaTranscribe or ZernioCommentAutomationCreate => Risk.Write,
        OsRun or AppRestart => Risk.Os,
        _ => Risk.Read
    };

    public static int RiskRank(string risk) => Array.IndexOf(Risk.All, risk);
}

public sealed class CommandRequest
{
    [JsonPropertyName("cmd")] public string Cmd { get; set; } = "";
    [JsonPropertyName("args")] public JsonElement? Args { get; set; }
    /// <summary>Plain-language (Persian) reason shown to the owner in the log and in confirmation dialogs.</summary>
    [JsonPropertyName("note")] public string Note { get; set; } = "";

    public static CommandRequest Parse(string json) =>
        JsonSerializer.Deserialize<CommandRequest>(json, CryptoBox.JsonOptions) ?? throw new CommandException("malformed", "empty command");
}

public sealed class CommandError
{
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
}

public sealed class CommandResponse
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("cmd")] public string Cmd { get; set; } = "";
    [JsonPropertyName("inReplyTo")] public string InReplyTo { get; set; } = "";
    [JsonPropertyName("result")] public JsonNode? Result { get; set; }
    [JsonPropertyName("error")] public CommandError? Error { get; set; }
    [JsonPropertyName("at")] public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("durationMs")] public long DurationMs { get; set; }
    [JsonPropertyName("app")] public string App { get; set; } = "";

    public string ToJson() => JsonSerializer.Serialize(this, CryptoBox.JsonOptions);
    public static CommandResponse FromJson(string json) =>
        JsonSerializer.Deserialize<CommandResponse>(json, CryptoBox.JsonOptions) ?? throw new InvalidDataException("empty response");

    public static CommandResponse Success(string cmd, string inReplyTo, object? result, long durationMs, string app) => new()
    {
        Ok = true, Cmd = cmd, InReplyTo = inReplyTo, DurationMs = durationMs, App = app,
        Result = result is null ? null : result as JsonNode ?? JsonSerializer.SerializeToNode(result, CryptoBox.JsonOptions)
    };

    public static CommandResponse Failure(string cmd, string inReplyTo, string code, string message, long durationMs, string app) => new()
    {
        Ok = false, Cmd = cmd, InReplyTo = inReplyTo, DurationMs = durationMs, App = app,
        Error = new CommandError { Code = code, Message = message }
    };
}

/// <summary>Raised by command handlers; the code travels back to the agent, the message is Persian/technical but never secret.</summary>
public sealed class CommandException : Exception
{
    public string Code { get; }
    public CommandException(string code, string message) : base(message) { Code = code; }
}

/// <summary>Public identity published in the mailbox folder by either party.</summary>
public sealed class IdentityFile
{
    [JsonPropertyName("v")] public int V { get; set; } = 1;
    [JsonPropertyName("exchange")] public string Exchange { get; set; } = "";
    [JsonPropertyName("signing")] public string Signing { get; set; } = "";
    [JsonPropertyName("fingerprint")] public string Fingerprint { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("webhookUrl")] public string? WebhookUrl { get; set; }
    [JsonPropertyName("updatedAt")] public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public PeerKeys ToPeerKeys() => new(Exchange, Signing);

    /// <summary>Parses and checks that the fingerprint matches the keys (a mismatch means a tampered or hand-edited file).</summary>
    public static IdentityFile Parse(string json)
    {
        var f = JsonSerializer.Deserialize<IdentityFile>(json, CryptoBox.JsonOptions) ?? throw new MailboxSecurityException("identity", "identity file empty");
        if (f.V != 1 || string.IsNullOrWhiteSpace(f.Exchange) || string.IsNullOrWhiteSpace(f.Signing))
            throw new MailboxSecurityException("identity", "identity file malformed");
        var expected = PeerKeys.FingerprintOf(f.Exchange, f.Signing);
        if (!string.Equals(expected, f.Fingerprint, StringComparison.Ordinal))
            throw new MailboxSecurityException("identity", "identity fingerprint does not match its keys");
        using var x = CryptoBox.ImportExchangeKey(f.Exchange);
        using var s = CryptoBox.ImportSigningKey(f.Signing);
        return f;
    }

    public static IdentityFile From(MailboxIdentity identity, string label) => new()
    {
        Exchange = identity.ExchangePublicKey, Signing = identity.SigningPublicKey, Fingerprint = identity.Fingerprint, Label = label
    };

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions(CryptoBox.JsonOptions) { WriteIndented = true });
}

/// <summary>Signed heartbeat the app writes so the agent knows whether anybody is listening and which agent identity it currently trusts.</summary>
public sealed class MailboxState
{
    [JsonPropertyName("v")] public int V { get; set; } = 1;
    [JsonPropertyName("listening")] public bool Listening { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("session")] public string Session { get; set; } = "";
    [JsonPropertyName("agent")] public string Agent { get; set; } = "";
    [JsonPropertyName("approvedUntil")] public DateTimeOffset? ApprovedUntil { get; set; }
    [JsonPropertyName("updatedAt")] public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("app")] public string App { get; set; } = "";
    [JsonPropertyName("from")] public string From { get; set; } = "";
    [JsonPropertyName("sig")] public string Sig { get; set; } = "";

    public string SignedText() => string.Join("\n", new[]
    {
        "bazino-state-v1", V.ToString(), Listening ? "true" : "false", Status, Session, Agent,
        ApprovedUntil?.ToUniversalTime().ToString("O") ?? "", UpdatedAt.ToUniversalTime().ToString("O"), App, From
    });

    public void Sign(MailboxIdentity identity)
    {
        From = identity.Fingerprint;
        Sig = Convert.ToBase64String(identity.Signing.SignData(Encoding.UTF8.GetBytes(SignedText()), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    public bool Verify(PeerKeys app)
    {
        if (!string.Equals(From, app.Fingerprint, StringComparison.Ordinal)) return false;
        try
        {
            using var key = CryptoBox.ImportSigningKey(app.SigningPublicKey);
            return key.VerifyData(Encoding.UTF8.GetBytes(SignedText()), Convert.FromBase64String(Sig), HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or MailboxSecurityException)
        {
            return false;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions(CryptoBox.JsonOptions) { WriteIndented = true });
    public static MailboxState? TryParse(string json)
    {
        try { return JsonSerializer.Deserialize<MailboxState>(json, CryptoBox.JsonOptions); }
        catch (JsonException) { return null; }
    }
}

/// <summary>Deep-merges <paramref name="changes"/> into <paramref name="target"/> (objects merge, everything else replaces) and lists what changed.</summary>
public static class JsonMerge
{
    public static List<string> Apply(JsonObject target, JsonObject changes, string prefix = "")
    {
        var changed = new List<string>();
        foreach (var (key, value) in changes)
        {
            var path = prefix.Length == 0 ? key : prefix + "." + key;
            if (value is JsonObject childChanges && target[key] is JsonObject childTarget)
            {
                changed.AddRange(Apply(childTarget, childChanges, path));
                continue;
            }
            var before = target[key]?.ToJsonString() ?? "null";
            var after = value?.ToJsonString() ?? "null";
            if (before == after) continue;
            target[key] = value?.DeepClone();
            changed.Add($"{path}: {Trim(before)} → {Trim(after)}");
        }
        return changed;
    }

    private static string Trim(string s) => s.Length <= 80 ? s : s[..80] + "…";
}
