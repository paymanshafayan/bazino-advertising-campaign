using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BazinoMarketing.Core.Mailbox;

/// <summary>Trust state of one agent fingerprint (owner decision 2026-10-04).</summary>
public enum AgentTrustState
{
    /// <summary>Seen in the mailbox but the owner has not approved it yet; every command is refused.</summary>
    Pending,
    /// <summary>Approved by the owner: commands run with full access.</summary>
    Approved,
    /// <summary>Switched off by the owner: commands are refused until the owner enables it again.</summary>
    Disabled
}

/// <summary>One agent identity the app has seen, with the owner's decision about it.</summary>
public sealed class AgentRecord
{
    [JsonPropertyName("fingerprint")] public string Fingerprint { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("sourceKey")] public string SourceKey { get; set; } = "";
    [JsonPropertyName("firstSeenAt")] public DateTimeOffset FirstSeenAt { get; set; }
    [JsonPropertyName("state")] public string StateName { get; set; } = nameof(AgentTrustState.Pending);
    [JsonPropertyName("approvedAt")] public DateTimeOffset? ApprovedAt { get; set; }
    [JsonPropertyName("disabledAt")] public DateTimeOffset? DisabledAt { get; set; }

    [JsonIgnore]
    public AgentTrustState State =>
        Enum.TryParse<AgentTrustState>(StateName, ignoreCase: true, out var parsed) ? parsed : AgentTrustState.Pending;

    [JsonIgnore] public bool IsApproved => State == AgentTrustState.Approved;
}

/// <summary>
/// The owner's ledger of agent fingerprints (persisted next to the app data). Only fingerprints published in the mailbox are
/// mirrored here — never a key or a token. The owner is the only authority: the app only records a fingerprint and never
/// approves one by itself.
/// </summary>
public sealed class AgentRegistry
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;
    private readonly object _gate = new();
    private readonly Dictionary<string, AgentRecord> _records = new(StringComparer.OrdinalIgnoreCase);

    public AgentRegistry(string path)
    {
        _path = path;
        Load();
    }

    public static string DefaultPath(string dataFolder) => Path.Combine(dataFolder, "agents.json");

    /// <summary>Every known agent, approved first, then pending, then disabled; newest first inside each group.</summary>
    public IReadOnlyList<AgentRecord> All
    {
        get
        {
            lock (_gate)
                return _records.Values
                    .OrderBy(r => r.State switch { AgentTrustState.Approved => 0, AgentTrustState.Pending => 1, _ => 2 })
                    .ThenByDescending(r => r.FirstSeenAt)
                    .ToList();
        }
    }

    public AgentRecord? Get(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint)) return null;
        lock (_gate) return _records.TryGetValue(fingerprint, out var record) ? record : null;
    }

    public bool IsApproved(string fingerprint) => Get(fingerprint)?.IsApproved == true;

    /// <summary>Records a fingerprint seen in the mailbox and returns its (possibly new) record. Never approves by itself.</summary>
    public AgentRecord Observe(string fingerprint, string label, string sourceKey)
    {
        lock (_gate)
        {
            if (_records.TryGetValue(fingerprint, out var existing))
            {
                var changed = false;
                if (!string.IsNullOrWhiteSpace(label) && label != existing.Label) { existing.Label = label; changed = true; }
                if (!string.IsNullOrWhiteSpace(sourceKey) && sourceKey != existing.SourceKey) { existing.SourceKey = sourceKey; changed = true; }
                if (changed) Save();
                return existing;
            }
            var record = new AgentRecord
            {
                Fingerprint = fingerprint,
                Label = label ?? "",
                SourceKey = sourceKey ?? "",
                FirstSeenAt = DateTimeOffset.UtcNow,
                StateName = nameof(AgentTrustState.Pending)
            };
            _records[fingerprint] = record;
            Save();
            return record;
        }
    }

    public bool Approve(string fingerprint) => SetState(fingerprint, AgentTrustState.Approved);
    public bool Disable(string fingerprint) => SetState(fingerprint, AgentTrustState.Disabled);
    public bool Enable(string fingerprint) => SetState(fingerprint, AgentTrustState.Approved);

    public bool Forget(string fingerprint)
    {
        lock (_gate)
        {
            if (!_records.Remove(fingerprint)) return false;
            Save();
            return true;
        }
    }

    private bool SetState(string fingerprint, AgentTrustState state)
    {
        lock (_gate)
        {
            if (!_records.TryGetValue(fingerprint, out var record)) return false;
            record.StateName = state.ToString();
            if (state == AgentTrustState.Approved) { record.ApprovedAt = DateTimeOffset.UtcNow; record.DisabledAt = null; }
            if (state == AgentTrustState.Disabled) record.DisabledAt = DateTimeOffset.UtcNow;
            Save();
            return true;
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var text = File.ReadAllText(_path);
            var list = JsonSerializer.Deserialize<List<AgentRecord>>(text, Json) ?? new List<AgentRecord>();
            foreach (var record in list)
                if (!string.IsNullOrWhiteSpace(record.Fingerprint)) _records[record.Fingerprint] = record;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A broken ledger must never stop the mailbox: start empty and let the owner re-approve.
            _records.Clear();
        }
    }

    private void Save()
    {
        try
        {
            var folder = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(_path, JsonSerializer.Serialize(_records.Values.ToList(), Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort; the in-memory ledger still gates commands correctly for this session.
        }
    }
}
