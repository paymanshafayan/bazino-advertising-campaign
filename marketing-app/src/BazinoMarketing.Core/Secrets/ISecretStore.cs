namespace BazinoMarketing.Core.Secrets;

/// <summary>Well-known secret keys. Custom cards use <see cref="ForCustomCredential"/> / <see cref="ForCustomVariable"/>.</summary>
public static class SecretKeys
{
    public const string GitHubToken = "github.token";
    public const string ZernioApiKey = "zernio.apiKey";
    public const string BazinoPortalIngestToken = "bazinoPortal.instagramIngestToken";
    public const string FluxApiKey = "flux.apiKey";
    public const string GroqApiKey = "groq.apiKey";
    /// <summary>Private keys of this app's mailbox identity (JSON from <c>MailboxIdentity.ExportPrivate</c>). Never shown, never exported.</summary>
    public const string MailboxIdentity = "mailbox.identity";

    public static string ForCustomCredential(string cardId) => $"custom.{cardId}.credential";
    public static string ForCustomVariable(string cardId, string variableName) => $"custom.{cardId}.var.{variableName}";
    public static string CustomPrefix(string cardId) => $"custom.{cardId}.";
}

public interface ISecretStore
{
    /// <summary>False when the OS cannot protect secrets for this user (e.g. not Windows).</summary>
    bool IsAvailable { get; }
    string? UnavailableReason { get; }

    bool Has(string key);
    bool TryGet(string key, out string value);
    void Set(string key, string value);
    void Remove(string key);
    void RemoveByPrefix(string prefix);
    IReadOnlyCollection<string> Keys { get; }

    /// <summary>All plaintext values, used only by the log redactor. Never log or display the result.</summary>
    IEnumerable<string> AllValues();
}

public static class SecretStoreExtensions
{
    public static string GetOrEmpty(this ISecretStore store, string key) =>
        store.TryGet(key, out var value) ? value : "";
}

/// <summary>Volatile store for tests and the screenshot/sample mode. Never used for real data.</summary>
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public bool IsAvailable => true;
    public string? UnavailableReason => null;

    public bool Has(string key) { lock (_gate) return _values.ContainsKey(key); }

    public bool TryGet(string key, out string value)
    {
        lock (_gate)
        {
            if (_values.TryGetValue(key, out var v)) { value = v; return true; }
            value = "";
            return false;
        }
    }

    public void Set(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (_gate) _values[key] = value ?? "";
    }

    public void Remove(string key) { lock (_gate) _values.Remove(key); }

    public void RemoveByPrefix(string prefix)
    {
        lock (_gate)
        {
            foreach (var k in _values.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                _values.Remove(k);
        }
    }

    public IReadOnlyCollection<string> Keys { get { lock (_gate) return _values.Keys.ToList(); } }

    public IEnumerable<string> AllValues() { lock (_gate) return _values.Values.ToList(); }
}
