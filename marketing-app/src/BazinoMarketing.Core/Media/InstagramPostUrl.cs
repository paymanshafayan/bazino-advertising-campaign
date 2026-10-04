using System.Text.RegularExpressions;

namespace BazinoMarketing.Core.Media;

/// <summary>Recognizes one explicitly selected Instagram post/reel URL; it never discovers or traverses an account.</summary>
public static partial class InstagramPostUrl
{
    [GeneratedRegex(@"^/(?:[^/]+/)?(?:p|reel|reels|tv)/([A-Za-z0-9_-]+)/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PostPathRegex();

    public static bool TryGetShortcode(string? value, out string shortcode)
    {
        shortcode = "";
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;
        var host = uri.Host.TrimEnd('.');
        if (!host.Equals("instagram.com", StringComparison.OrdinalIgnoreCase) &&
            !host.Equals("www.instagram.com", StringComparison.OrdinalIgnoreCase) &&
            !host.Equals("m.instagram.com", StringComparison.OrdinalIgnoreCase)) return false;
        var match = PostPathRegex().Match(uri.AbsolutePath);
        if (!match.Success) return false;
        var candidate = match.Groups[1].Value;
        if (candidate.Length is < 3 or > 64) return false;
        shortcode = candidate;
        return true;
    }
}
