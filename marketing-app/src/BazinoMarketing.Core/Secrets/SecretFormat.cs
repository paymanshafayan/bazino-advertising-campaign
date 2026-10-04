using System.Text.RegularExpressions;

namespace BazinoMarketing.Core.Secrets;

/// <summary>Kind of credential, used to validate imported/entered values and to pull a token out of surrounding text.</summary>
public enum SecretKind { GitHubToken, ZernioKey, CloudflareToken, CloudflareAccountId, Generic }

/// <summary>
/// Format checks for keys/tokens. HTTP headers only accept printable ASCII, so anything else can never be a working
/// credential; such values are rejected with a clear message instead of producing confusing network errors.
/// The extractors recover a token when the owner pasted it together with extra text (labels, notes, spaces).
/// </summary>
public static partial class SecretFormat
{
    [GeneratedRegex(@"github_pat_[A-Za-z0-9_]{82}|gh[pousr]_[A-Za-z0-9]{36,255}")]
    private static partial Regex GitHubTokenRegex();

    /// <summary>Cloudflare API token (40 chars of [A-Za-z0-9_-]) or legacy Global API key (37 hex chars).</summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{40}(?![A-Za-z0-9_-])|(?<![a-f0-9])[a-f0-9]{37}(?![a-f0-9])")]
    private static partial Regex CloudflareTokenRegex();

    [GeneratedRegex(@"(?<![a-f0-9])[a-f0-9]{32}(?![a-f0-9])")]
    private static partial Regex Hex32Regex();

    [GeneratedRegex(@"[A-Za-z0-9_\-\.:]{16,}")]
    private static partial Regex GenericKeyRegex();

    /// <summary>True when the value can be sent in an HTTP header: non-empty, printable ASCII, no whitespace.</summary>
    public static bool IsHeaderSafe(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        foreach (var c in value)
            if (c <= 0x20 || c >= 0x7F) return false;
        return true;
    }

    public static bool ContainsNonAscii(string? value) => !string.IsNullOrEmpty(value) && value.Any(c => c > 0x7F);

    public static bool LooksLikeGitHubToken(string value) => GitHubTokenRegex().Match(value) is { Success: true } m && m.Value == value;
    public static bool LooksLikeCloudflareToken(string value) => CloudflareTokenRegex().Match(value) is { Success: true } m && m.Value == value;
    public static bool LooksLikeHex32(string value) => value.Length == 32 && value.All(Uri.IsHexDigit) && value == value.ToLowerInvariant();

    /// <summary>Persian description of why a value is unusable (for reports/logs; never echoes the value).</summary>
    public static string DescribeProblem(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "خالی است";
        var parts = new List<string> { $"{value.Length} نویسه" };
        if (ContainsNonAscii(value)) parts.Add("شامل متن غیرلاتین است");
        else if (value.Any(char.IsWhiteSpace)) parts.Add("شامل فاصله است");
        else if (value.Any(c => c < 0x20 || c == 0x7F)) parts.Add("شامل نویسهٔ کنترلی است");
        return string.Join("، ", parts);
    }

    /// <summary>
    /// Validates <paramref name="raw"/> for the given kind. When the raw value is not usable as-is but contains a
    /// recognisable token, that token is returned with <c>Extracted = true</c>. Returns null when nothing usable exists.
    /// </summary>
    public static SecretCheck Check(SecretKind kind, string? raw)
    {
        var value = (raw ?? "").Trim();
        if (value.Length == 0) return SecretCheck.Empty;
        var shapeOk = kind switch
        {
            SecretKind.CloudflareAccountId => LooksLikeHex32(value),
            _ => true
        };
        if (IsHeaderSafe(value) && shapeOk)
            return new SecretCheck(value, false, Recognised(kind, value));

        var extracted = kind switch
        {
            SecretKind.GitHubToken => GitHubTokenRegex().Match(value) is { Success: true } g ? g.Value : null,
            SecretKind.CloudflareToken => CloudflareTokenRegex().Match(value) is { Success: true } c ? c.Value : null,
            SecretKind.CloudflareAccountId => Hex32Regex().Match(value) is { Success: true } h ? h.Value : null,
            _ => GenericKeyRegex().Matches(value).Select(m => m.Value).OrderByDescending(v => v.Length).FirstOrDefault()
        };
        return extracted is null ? SecretCheck.Invalid(DescribeProblem(value)) : new SecretCheck(extracted, true, Recognised(kind, extracted));
    }

    private static bool Recognised(SecretKind kind, string value) => kind switch
    {
        SecretKind.GitHubToken => LooksLikeGitHubToken(value),
        SecretKind.CloudflareToken => LooksLikeCloudflareToken(value),
        SecretKind.CloudflareAccountId => LooksLikeHex32(value),
        _ => true
    };
}

/// <summary>Result of <see cref="SecretFormat.Check"/>.</summary>
public sealed record SecretCheck(string? Value, bool Extracted, bool Recognised, string Problem = "")
{
    public bool IsUsable => !string.IsNullOrEmpty(Value);
    public static SecretCheck Empty { get; } = new(null, false, false, "خالی است");
    public static SecretCheck Invalid(string problem) => new(null, false, false, problem);
}
