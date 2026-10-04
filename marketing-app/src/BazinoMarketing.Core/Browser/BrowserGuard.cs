using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace BazinoMarketing.Core.Browser;

/// <summary>One open browser tab, as reported by Chrome's /json/list endpoint.</summary>
public sealed record BrowserTarget(
    string Id,
    string Title,
    string Url,
    string Type,
    string? WebSocketDebuggerUrl);

/// <summary>What a page read returned. Text is already trimmed and redacted of nothing — the owner sees exactly what the page holds.</summary>
public sealed record BrowserPage(string Url, string Title, string Text, int TotalChars, bool Truncated);

/// <summary>Result of any bridge call: either a value or a refusal, never both.</summary>
public sealed record BrowserResult(bool Ok, string? Error, JsonNode? Value)
{
    public static BrowserResult Good(JsonNode? value) => new(true, null, value);
    public static BrowserResult Fail(string error) => new(false, error, null);
}

/// <summary>
/// Browser policy as pure functions so it can be tested without Chrome. Any ordinary HTTP(S) address may be opened;
/// expressions that would read stored browser credentials remain blocked.
/// </summary>
public static class BrowserGuard
{
    /// <summary>Expressions that would read something the owner never meant to share.</summary>
    private static readonly string[] ForbiddenExpressionParts =
    {
        "document.cookie", "localstorage", "sessionstorage", "indexeddb",
        "input[type=password]", "input[type='password']", "input[type=\"password\"]",
        "navigator.credentials", "window.credentials"
    };

    /// <summary>Only http and https. Anything else (file:, javascript:, data:, about:) is refused.</summary>
    public static bool IsWebUrl(string? url, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) return false;
        if (parsed.Host.Length == 0) return false;
        uri = parsed;
        return true;
    }

    /// <summary>Accepts any HTTP(S) address; file, script, data and other local schemes remain unavailable.</summary>
    public static string? CheckUrl(string? url) =>
        IsWebUrl(url, out _) ? null : "نشانی معتبر نیست یا از نوع http/https نیست.";

    /// <summary>Checks a JavaScript expression. Returns null when allowed, otherwise the Persian reason it was refused.</summary>
    public static string? CheckExpression(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return "عبارت جاوااسکریپت خالی است.";
        if (expression.Length > 12000) return "عبارت خیلی بلند است (حداکثر ۱۲۰۰۰ کاراکتر).";
        var lowered = expression.ToLowerInvariant();
        foreach (var part in ForbiddenExpressionParts)
            if (lowered.Contains(part, StringComparison.Ordinal))
                return $"این عبارت سعی در خواندن اطلاعات ذخیره‌شدهٔ مرورگر دارد («{part}») و اجازه ندارد.";
        return null;
    }

    /// <summary>Cuts a page's text to <paramref name="maxChars"/> and says whether anything was lost.</summary>
    public static (string Text, int Total, bool Truncated) Trim(string text, int maxChars)
    {
        var total = text?.Length ?? 0;
        var safeMax = Math.Clamp(maxChars, 200, 200000);
        if (total <= safeMax) return (text ?? "", total, false);
        return (text![..safeMax], total, true);
    }

    /// <summary>Reads the visible text of a page: scripts and styles removed, whitespace collapsed.</summary>
    public const string ReadableTextExpression = """
        (() => {
          const drop = document.querySelectorAll('script,style,noscript,svg,template');
          drop.forEach(n => n.remove());
          const root = document.body || document.documentElement;
          const text = (root.innerText || root.textContent || '')
            .replace(/[ \t\r\f\v]+/g, ' ')
            .replace(/\n{3,}/g, '\n\n')
            .trim();
          return JSON.stringify({
            url: location.href,
            title: document.title || '',
            text: text
          });
        })()
        """;

    /// <summary>Parses Chrome's /json/list response. Unknown shapes yield an empty list rather than throwing.</summary>
    public static List<BrowserTarget> ParseTargets(string json)
    {
        var list = new List<BrowserTarget>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        JsonArray? array;
        try { array = JsonNode.Parse(json) as JsonArray; }
        catch (JsonException) { return list; }
        if (array is null) return list;
        foreach (var node in array)
        {
            if (node is not JsonObject o) continue;
            var type = o["type"]?.GetValue<string>() ?? "";
            // Only pages carry readable content; service workers and extensions do not.
            if (!string.Equals(type, "page", StringComparison.OrdinalIgnoreCase)) continue;
            var id = o["id"]?.GetValue<string>() ?? "";
            if (id.Length == 0) continue;
            list.Add(new BrowserTarget(
                id,
                o["title"]?.GetValue<string>() ?? "",
                o["url"]?.GetValue<string>() ?? "",
                type,
                o["webSocketDebuggerUrl"]?.GetValue<string>()));
        }
        return list;
    }

    /// <summary>Parses Chrome's /json/version response for the browser-level WebSocket URL.</summary>
    public static string? ParseBrowserWebSocketUrl(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var o = JsonNode.Parse(json) as JsonObject;
            var url = o?["webSocketDebuggerUrl"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(url) ? null : url;
        }
        catch (JsonException) { return null; }
    }
}

/// <summary>Serializer settings shared by the browser layer: camelCase, tolerant reading.</summary>
public static class BrowserJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
