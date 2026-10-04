using System.Text.RegularExpressions;

namespace BazinoMarketing.Core.Logging;

/// <summary>
/// Removes secrets from any text before it is stored, displayed, copied or sent to the agent.
/// Two layers: (1) exact known secret values from the secret store, (2) well-known token shapes.
/// </summary>
public sealed class Redactor
{
    public const string Mask = "***";
    private readonly Func<IEnumerable<string>> _secretValues;

    private static readonly (Regex Pattern, string Replacement)[] Patterns =
    {
        (new Regex(@"gh[pousr]_[A-Za-z0-9]{16,}", RegexOptions.Compiled), Mask),
        (new Regex(@"github_pat_[A-Za-z0-9_]{20,}", RegexOptions.Compiled), Mask),
        (new Regex(@"(?i)(bearer\s+)[A-Za-z0-9._~+/=\-]{8,}", RegexOptions.Compiled), "$1" + Mask),
        (new Regex(@"(?i)(basic\s+)[A-Za-z0-9+/=]{8,}", RegexOptions.Compiled), "$1" + Mask),
        (new Regex(@"(?i)\b(token|secret|password|passwd|pwd|api[_-]?key|apikey|authorization|client_secret|access_token|refresh_token|private_key)(\s*[""']?\s*[:=]\s*[""']?)([^""'\s,;&]{4,})", RegexOptions.Compiled), "$1$2" + Mask),
        (new Regex(@"(?i)([?&](?:code|token|access_token|refresh_token|key|api_key|apikey|client_secret|sig|signature)=)[^&\s""']+", RegexOptions.Compiled), "$1" + Mask),
        (new Regex(@"(?i)((?:set-)?cookie\s*[:=]\s*)[^\r\n]+", RegexOptions.Compiled), "$1" + Mask),
        (new Regex(@"://([^/:@\s]+):([^@/\s]+)@", RegexOptions.Compiled), "://" + Mask + ":" + Mask + "@"),
        (new Regex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----", RegexOptions.Compiled), Mask),
    };

    public Redactor(Func<IEnumerable<string>> secretValues)
    {
        _secretValues = secretValues ?? throw new ArgumentNullException(nameof(secretValues));
    }

    public static Redactor Empty { get; } = new(() => Array.Empty<string>());

    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var result = text;

        foreach (var secret in SortedSecrets())
        {
            if (secret.Length < 6) continue; // too short to be safely replaced without mangling normal text
            result = result.Replace(secret, Mask, StringComparison.Ordinal);
        }

        foreach (var (pattern, replacement) in Patterns)
            result = pattern.Replace(result, replacement);

        return result;
    }

    public IReadOnlyDictionary<string, string> Redact(IReadOnlyDictionary<string, string>? data)
    {
        if (data is null || data.Count == 0) return new Dictionary<string, string>();
        return data.ToDictionary(kv => kv.Key, kv => Redact(kv.Value), StringComparer.Ordinal);
    }

    private IEnumerable<string> SortedSecrets()
    {
        // Longest first so a secret that contains another is fully masked.
        IEnumerable<string> values;
        try { values = _secretValues() ?? Array.Empty<string>(); }
        catch { values = Array.Empty<string>(); }
        return values.Where(v => !string.IsNullOrEmpty(v)).Distinct(StringComparer.Ordinal).OrderByDescending(v => v.Length);
    }
}
