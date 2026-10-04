using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BazinoMarketing.Core.Secrets;

/// <summary>
/// Secrets protected with Windows DPAPI (CurrentUser scope + app entropy) and stored one entry per key in
/// <c>secrets.json</c>. Only the same Windows user on the same machine can read them back. Values are cached
/// in memory for the lifetime of the process so the UI never re-prompts.
/// </summary>
public sealed class DpapiSecretStore : ISecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("BazinoMarketing.secrets.v1");
    private readonly string _rootPath;
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _blobs = new(StringComparer.Ordinal);   // key -> base64(DPAPI blob)
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);   // key -> plaintext

    public string FilePath => Path.Combine(_rootPath, "secrets.json");

    public bool IsAvailable { get; }
    public string? UnavailableReason { get; }

    public DpapiSecretStore(string rootPath)
    {
        _rootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
        IsAvailable = OperatingSystem.IsWindows();
        UnavailableReason = IsAvailable ? null : "حفاظت از اسرار فقط روی Windows (DPAPI) در دسترس است.";
        LoadFile();
    }

    private void LoadFile()
    {
        if (!File.Exists(FilePath)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
            if (doc.RootElement.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in entries.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String) _blobs[p.Name] = p.Value.GetString() ?? "";
            }
        }
        catch (JsonException)
        {
            // A corrupt file must not take the whole app down; the owner can re-enter keys.
            _blobs.Clear();
        }
    }

    private void SaveFile()
    {
        Directory.CreateDirectory(_rootPath);
        var payload = new Dictionary<string, object> { ["v"] = 1, ["entries"] = _blobs };
        var tmp = FilePath + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, FilePath, overwrite: true);
    }

    public bool Has(string key) { lock (_gate) return _blobs.ContainsKey(key); }

    public bool TryGet(string key, out string value)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var cached)) { value = cached; return true; }
            if (!_blobs.TryGetValue(key, out var b64)) { value = ""; return false; }
            if (!IsAvailable) { value = ""; return false; }
            try
            {
                var plain = ProtectedData.Unprotect(Convert.FromBase64String(b64), Entropy, DataProtectionScope.CurrentUser);
                value = Encoding.UTF8.GetString(plain);
                _cache[key] = value;
                return true;
            }
            catch (CryptographicException)
            {
                value = "";
                return false; // written by another user/machine; treat as absent
            }
            catch (FormatException)
            {
                value = "";
                return false;
            }
        }
    }

    public void Set(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!IsAvailable) throw new PlatformNotSupportedException(UnavailableReason);
        lock (_gate)
        {
            var blob = ProtectedData.Protect(Encoding.UTF8.GetBytes(value ?? ""), Entropy, DataProtectionScope.CurrentUser);
            _blobs[key] = Convert.ToBase64String(blob);
            _cache[key] = value ?? "";
            SaveFile();
        }
    }

    public void Remove(string key)
    {
        lock (_gate)
        {
            var changed = _blobs.Remove(key);
            _cache.Remove(key);
            if (changed) SaveFile();
        }
    }

    public void RemoveByPrefix(string prefix)
    {
        lock (_gate)
        {
            var keys = _blobs.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            foreach (var k in keys) { _blobs.Remove(k); _cache.Remove(k); }
            if (keys.Count > 0) SaveFile();
        }
    }

    public IReadOnlyCollection<string> Keys { get { lock (_gate) return _blobs.Keys.ToList(); } }

    public IEnumerable<string> AllValues()
    {
        var keys = Keys;
        foreach (var k in keys)
            if (TryGet(k, out var v) && !string.IsNullOrEmpty(v)) yield return v;
    }

    /// <summary>Raw DPAPI CurrentUser unprotect without app entropy — the format PowerShell's ConvertFrom-SecureString uses.</summary>
    public static byte[] UnprotectRaw(byte[] blob)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI requires Windows");
        return ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser);
    }
}
