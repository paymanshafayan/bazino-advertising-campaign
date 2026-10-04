using System.Text.Json;
using System.Text.Json.Serialization;

namespace BazinoMarketing.Core.Settings;

/// <summary>
/// Non-secret application settings. Secrets never live here: they are stored in the
/// <see cref="Secrets.ISecretStore"/> and referenced by key (see <see cref="Secrets.SecretKeys"/>).
/// </summary>
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public GitHubSettings GitHub { get; set; } = new();
    public KlingSettings Kling { get; set; } = new();
    public ZernioSettings Zernio { get; set; } = new();
    public BazinoPortalSettings BazinoPortal { get; set; } = new();
    public FluxSettings Flux { get; set; } = new();
    public GroqSettings Groq { get; set; } = new();
    public List<CustomCard> CustomCards { get; set; } = new();
    public UiSettings Ui { get; set; } = new();
    public BrowserSettings Browser { get; set; } = new();
    public MediaSettings Media { get; set; } = new();
    public LegacyImportState LegacyImport { get; set; } = new();

    public AppSettings Clone() => JsonUtil.Clone(this);
}

/// <summary>Per-tool permission switches. Everything is off by default; the owner enables what is allowed.</summary>
public sealed class ToolPermissions
{
    public bool AllowWrites { get; set; }
    public bool AllowPublish { get; set; }
    public bool AllowSpend { get; set; }
}

public sealed class ProxySettings
{
    /// <summary>system | none | custom</summary>
    public string Mode { get; set; } = "system";
    public string Url { get; set; } = "";
}

public sealed class GitHubSettings
{
    public string Repository { get; set; } = "paymanshafayan/bazino-gamenet-portal";
    public string Branch { get; set; } = "arena/01a0f126-bazino-gamenet-portal";
    public string MailboxPath { get; set; } = "marketing-app-mailbox";
    public int PollSeconds { get; set; } = 1;
    public ProxySettings Proxy { get; set; } = new();
    public ToolPermissions Permissions { get; set; } = new();
}

public sealed class KlingSettings
{
    /// <summary>global (kling.ai) | cn (klingai.com). Chosen by the owner; never guessed.</summary>
    public string Region { get; set; } = "global";
    /// <summary>Optional explicit path to kling.cmd / kling. Empty = search PATH and %APPDATA%\npm.</summary>
    public string CliPath { get; set; } = "";
    /// <summary>Optional explicit path to node.exe. Empty = search PATH.</summary>
    public string NodePath { get; set; } = "";
    public ProxySettings Proxy { get; set; } = new();
    public ToolPermissions Permissions { get; set; } = new();
}

public sealed class ZernioSettings
{
    public string BaseUrl { get; set; } = "https://zernio.com/api";
    public ProxySettings Proxy { get; set; } = new();
    public ToolPermissions Permissions { get; set; } = new();
}

public sealed class BazinoPortalSettings
{
    public string BaseUrl { get; set; } = "https://bazino.pro";
    public ProxySettings Proxy { get; set; } = new();
}

public sealed class FluxSettings
{
    /// <summary>cloudflare | generic | none. No provider or cost is assumed by default.</summary>
    public string Provider { get; set; } = "none";
    public string AccountId { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.cloudflare.com/client/v4";
    public string Model { get; set; } = "@cf/black-forest-labs/flux-1-schnell";
    /// <summary>For provider=generic: a read-only URL used by "test connection".</summary>
    public string TestUrl { get; set; } = "";
    public ProxySettings Proxy { get; set; } = new();
    public ToolPermissions Permissions { get; set; } = new();
}

public sealed class GroqSettings
{
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1";
    /// <summary>whisper-large-v3 (highest multilingual accuracy + translation) | whisper-large-v3-turbo (fastest)</summary>
    public string Model { get; set; } = "whisper-large-v3";
    /// <summary>Optional ISO-639-1 language code (e.g. "fa", "en"). Empty means auto-detect.</summary>
    public string Language { get; set; } = "";
    public ProxySettings Proxy { get; set; } = new();
    public ToolPermissions Permissions { get; set; } = new();
}

public sealed class CustomCard
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>http | mcp</summary>
    public string Kind { get; set; } = "http";
    public string BaseUrl { get; set; } = "";
    /// <summary>none | bearer | header | basic</summary>
    public string AuthType { get; set; } = "none";
    public string HeaderName { get; set; } = "";
    /// <summary>Relative path used by the safe, read-only "test connection" (GET).</summary>
    public string TestPath { get; set; } = "/";
    public List<CustomVariable> Variables { get; set; } = new();
    public ProxySettings Proxy { get; set; } = new();
    public ToolPermissions Permissions { get; set; } = new();
    public string Notes { get; set; } = "";
    public string ImportedFrom { get; set; } = "";
}

public sealed class CustomVariable
{
    public string Name { get; set; } = "";
    /// <summary>Plain value. For secret variables this stays empty and the value lives in the secret store.</summary>
    public string Value { get; set; } = "";
    public bool IsSecret { get; set; }
}

public sealed class UiSettings
{
    /// <summary>Only the light design exists since 2026-10-03; older settings files that say "dark" are migrated on load.</summary>
    public string Theme { get; set; } = "light";
    public string LastPage { get; set; } = "connection";
}

public sealed class LegacyImportState
{
    public bool Attempted { get; set; }
    /// <summary>Importer revision that last ran; a newer importer re-runs automatically (non-destructively).</summary>
    public int Version { get; set; }
    public string LastSummary { get; set; } = "";
    public DateTimeOffset? LastImportedAtUtc { get; set; }
}

public static class JsonUtil
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static readonly JsonSerializerOptions Compact = new(Options) { WriteIndented = false };

    public static T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)
        ?? throw new InvalidOperationException("Clone failed");
}
