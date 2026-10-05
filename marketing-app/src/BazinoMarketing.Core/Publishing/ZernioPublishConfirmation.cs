using System.Text.Json.Nodes;

namespace BazinoMarketing.Core.Publishing;

/// <summary>
/// Official per-platform publish states documented by Zernio for POST/GET /v1/posts
/// (pending | processing | uploading | published | failed | cancelled).
/// Anything that is not a terminal state must never be reported as a failure: the platform may
/// still be publishing, and the post can be confirmed on a later poll (owner incident 2026-10-04:
/// content was published while the app reported «زرنیو موفقیت نهایی Instagram را تأیید نکرد»).
/// </summary>
public enum ZernioPlatformState
{
    Missing,
    Transient,
    Published,
    Failed,
    Cancelled
}

/// <summary>One platform entry of a Zernio post, with the fields needed to prove the outcome.</summary>
public sealed record ZernioPlatformOutcome(
    string Platform,
    string Status,
    ZernioPlatformState State,
    string PlatformPostId,
    string Url,
    DateTimeOffset? PublishedAt,
    string ErrorMessage,
    string ErrorCategory,
    string ErrorSource)
{
    /// <summary>Human-readable Persian detail for a failed/cancelled entry (never invents a reason).</summary>
    public string FailureDetail()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(ErrorMessage)) parts.Add(ErrorMessage.Trim());
        if (!string.IsNullOrWhiteSpace(ErrorCategory)) parts.Add("دستهٔ خطا: " + ErrorCategory.Trim());
        if (!string.IsNullOrWhiteSpace(ErrorSource)) parts.Add("منبع خطا: " + ErrorSource.Trim());
        if (parts.Count == 0) parts.Add("ژینوس وضعیت «" + (string.IsNullOrWhiteSpace(Status) ? "نامشخص" : Status) + "» را برگرداند.");
        return string.Join("؛ ", parts);
    }
}

/// <summary>
/// Pure reading of Zernio post payloads (create, get-one and list responses). Kept free of HTTP so the
/// publish-confirmation rules are unit-testable and can never silently downgrade a real success to a failure.
/// </summary>
public static class ZernioPublishConfirmation
{
    /// <summary>Terminal only for published/failed/cancelled; every other value keeps waiting instead of failing.</summary>
    public static ZernioPlatformState Classify(string? status)
    {
        var normalized = (status ?? "").Trim().ToLowerInvariant();
        if (normalized.Length == 0) return ZernioPlatformState.Missing;
        return normalized switch
        {
            "published" => ZernioPlatformState.Published,
            "failed" => ZernioPlatformState.Failed,
            "cancelled" or "canceled" => ZernioPlatformState.Cancelled,
            // Every documented transient value — and any unknown value — waits for confirmation instead of
            // being reported as a failure. Only Zernio's explicit terminal states may end an attempt.
            _ => ZernioPlatformState.Transient
        };
    }

    /// <summary>
    /// Finds the post object inside a create/get response (<c>post</c>) or a list response (<c>posts[]</c>,
    /// matched by the app's own <c>metadata.contentId</c>). Returns null when the payload carries no post.
    /// </summary>
    public static JsonObject? FindPostNode(JsonNode? body, string? contentId)
    {
        if (body is not JsonObject obj) return null;
        if (obj["post"] is JsonObject single)
        {
            if (string.IsNullOrWhiteSpace(contentId) || MatchesContent(single, contentId)) return single;
        }
        if (obj["posts"] is JsonArray list)
        {
            foreach (var node in list.OfType<JsonObject>())
                if (string.IsNullOrWhiteSpace(contentId) || MatchesContent(node, contentId)) return node;
        }
        if (obj["data"] is JsonObject data && !ReferenceEquals(data, obj)) return FindPostNode(data, contentId);
        return null;
    }

    /// <summary>True when the post was created by the app for exactly this queue item.</summary>
    public static bool MatchesContent(JsonNode? post, string contentId) =>
        !string.IsNullOrWhiteSpace(contentId) &&
        string.Equals(ReadString(post?["metadata"], "contentId"), contentId.Trim(), StringComparison.OrdinalIgnoreCase);

    public static string ReadPostId(JsonNode? post) => FirstNonEmpty(ReadString(post, "_id"), ReadString(post, "id"));

    /// <summary>Reads one platform entry (instagram / facebook / tiktok / youtube / telegram) from a post node.</summary>
    public static ZernioPlatformOutcome? ReadPlatform(JsonNode? post, string platform)
    {
        if (post?["platforms"] is not JsonArray entries) return null;
        foreach (var node in entries.OfType<JsonObject>())
        {
            if (!string.Equals(ReadString(node, "platform"), platform, StringComparison.OrdinalIgnoreCase)) continue;
            var status = ReadString(node, "status");
            var platformPostId = FirstNonEmpty(ReadString(node, "platformPostId"), ReadString(node, "mediaId"));
            // Zernio puts publishedAt on the post itself for most responses; the platform entry only carries it
            // sometimes. Without this fallback a successful publish would be recorded without its real time.
            var publishedAt = FirstNonEmpty(ReadString(node, "publishedAt"), ReadString(post, "publishedAt"));
            DateTimeOffset? parsedAt = DateTimeOffset.TryParse(publishedAt, out var parsed) ? parsed.ToUniversalTime() : null;
            var outcome = new ZernioPlatformOutcome(platform, status, Classify(status), platformPostId,
                ReadString(node, "platformPostUrl"), parsedAt,
                ReadString(node, "errorMessage"), ReadString(node, "errorCategory"), ReadString(node, "errorSource"));
            // A real platformPostId is success evidence even when the status field is missing or empty.
            return outcome.State == ZernioPlatformState.Missing && platformPostId.Length != 0
                ? outcome with { State = ZernioPlatformState.Published }
                : outcome;
        }
        return null;
    }

    public static PublishPlatformResult ToPlatformResult(ZernioPlatformOutcome outcome) =>
        new(outcome.Platform, outcome.Status, outcome.Url, outcome.PlatformPostId, outcome.PublishedAt);

    private static string ReadString(JsonNode? node, string name) =>
        node is JsonObject obj && obj[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text ?? "" : "";

    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
}
