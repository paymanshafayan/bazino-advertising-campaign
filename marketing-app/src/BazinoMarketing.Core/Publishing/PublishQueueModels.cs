using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BazinoMarketing.Core.Publishing;

/// <summary>Versioned topic ids from the daily-content guide; Affiliate is deliberately Reel-only.</summary>
public static class PublishContentCatalog
{
    public const string GuideVersion = "2026-10-02";
    private static readonly IReadOnlyDictionary<string, HashSet<string>> TopicsByType = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
    {
        ["post"] = new(StringComparer.Ordinal) { "gaming-news" },
        ["reels"] = new(StringComparer.Ordinal) { "daily-reels", "daily-didactic", "daily-game", "active-tournaments", "bazino-safe" },
        ["carousel"] = new(StringComparer.Ordinal) { "gaming-news" },
        ["story"] = new(StringComparer.Ordinal) { "story-game-interaction", "story-news-reshare", "story-club-link", "story-night-interaction" },
        ["DM"] = new(StringComparer.Ordinal) { "daily-reels", "daily-didactic", "daily-game", "gaming-news", "active-tournaments", "bazino-safe" },
        ["Affiliate"] = new(StringComparer.Ordinal) { "affiliate-reel" }
    };

    public static bool IsKnownType(string? contentType) => contentType is not null && TopicsByType.ContainsKey(contentType);
    public static bool IsTopicAllowed(string contentType, string? topic) =>
        topic is not null && TopicsByType.TryGetValue(contentType, out var topics) && topics.Contains(topic);
}

/// <summary>One agent-prepared item awaiting the owner's Instagram-style review in the Windows app.</summary>
public sealed class PublishQueueItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>Legacy code alias; never serialized. JSON must carry explicit mediaFormat.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Kind { get => MediaFormat; set => MediaFormat = value; }
    /// <summary>post | reel | carousel | story; distinct from contentType.</summary>
    public string MediaFormat { get; set; } = "";
    /// <summary>post | reels | carousel | story | DM | Affiliate.</summary>
    public string ContentType { get; set; } = "";
    public string Topic { get; set; } = "";
    public string GuideVersion { get; set; } = "";
    public string TopicCycle { get; set; } = "";
    public string ContentSlot { get; set; } = "";
    public string TimeZoneId { get; set; } = "";
    public string TargetPlatform { get; set; } = "";
    public string TargetAccountId { get; set; } = "";
    public string Cta { get; set; } = "";
    public string ProductionStatus { get; set; } = ""; // final only
    public bool PreviewReviewed { get; set; }
    public string? AffiliateDisclosure { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string Flow => ContentType switch { "DM" => "dm", "Affiliate" => "affiliate", _ => "normal" };
    public string Language { get; set; } = ""; // fa | tr | en
    public string Caption { get; set; } = "";
    public DateTimeOffset PublishAt { get; set; }
    public List<PublishMediaItem> Media { get; set; } = new();
    public bool RepublishToConnectedPlatforms { get; set; } = true;
    public string? YoutubeTitle { get; set; }
    public List<string> YoutubeTags { get; set; } = new();
    public string? TrialGroupId { get; set; }
    public string? TrialRole { get; set; } // trial | main
    public EngagementAutomation? Engagement { get; set; }

    private static bool IsQueueMediaPath(string? path)
    {
        const string prefix = "marketing-app-mailbox/publish-queue/media/";
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var filename = path[prefix.Length..];
        return filename.Length > 0 && filename is not ("." or "..") &&
               !filename.Contains('/') && !filename.Contains('\\') && filename.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    public static PublishQueueItem Parse(string json) =>
        JsonSerializer.Deserialize<PublishQueueItem>(json, BazinoMarketing.Core.Settings.JsonUtil.Options)
        ?? throw new InvalidDataException("Ù…Ø­ØªÙˆØ§ÛŒ ØµÙ Ø®ÙˆØ§Ù†Ø¯Ù‡ Ù†Ø´Ø¯.");

    public string Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || !Regex.IsMatch(Id, "^[a-zA-Z0-9][a-zA-Z0-9._-]{0,79}$")) return "Ø´Ù†Ø§Ø³Ù‡Ù” Ù…Ø­ØªÙˆØ§ Ø¨Ø§ÛŒØ¯ ÙÙ‚Ø· Ø´Ø§Ù…Ù„ Ø­Ø±ÙˆÙ Ù„Ø§ØªÛŒÙ†ØŒ Ø¹Ø¯Ø¯ØŒ Ø®Ø· ØªÛŒØ±Ù‡ØŒ Ù†Ù‚Ø·Ù‡ ÛŒØ§ Ø²ÛŒØ±Ø®Ø· Ø¨Ø§Ø´Ø¯.";
        if (string.IsNullOrWhiteSpace(Title) || Title.Length > 180) return "Ø¹Ù†ÙˆØ§Ù† Ù…Ø­ØªÙˆØ§ Ø®Ø§Ù„ÛŒ Ø§Ø³Øª ÛŒØ§ Ø¨ÛŒØ´ Ø§Ø² Ø­Ø¯ Ø·ÙˆÙ„Ø§Ù†ÛŒ Ø§Ø³Øª.";
        if (!PublishContentCatalog.IsKnownType(ContentType)) return "Ù†ÙˆØ¹ Ù…Ø­ØªÙˆØ§ Ø¨Ø§ÛŒØ¯ ØµØ±ÛŒØ­Ø§Ù‹ postØŒ reelsØŒ carouselØŒ storyØŒ DM ÛŒØ§ Affiliate Ø¨Ø§Ø´Ø¯.";
        if (!PublishContentCatalog.IsTopicAllowed(ContentType, Topic)) return "Ù…ÙˆØ¶ÙˆØ¹ Ø¨Ø§ Ù†ÙˆØ¹ Ù…Ø­ØªÙˆØ§ Ø³Ø§Ø²Ú¯Ø§Ø± Ù†ÛŒØ³Øª ÛŒØ§ Ø¯Ø± Ù†Ø³Ø®Ù‡Ù” Ø±Ø§Ù‡Ù†Ù…Ø§ÛŒ Ø±ÙˆØ²Ø§Ù†Ù‡ ÙˆØ¬ÙˆØ¯ Ù†Ø¯Ø§Ø±Ø¯.";
        if (GuideVersion != PublishContentCatalog.GuideVersion) return "Ù†Ø³Ø®Ù‡Ù” Ø±Ø§Ù‡Ù†Ù…Ø§ÛŒ ØªÙˆÙ„ÛŒØ¯ Ø¨Ø±Ø§ÛŒ Ø§ÛŒÙ† Ú©Ø§Ø±Øª Ø®Ø§Ù„ÛŒ ÛŒØ§ Ù‚Ø¯ÛŒÙ…ÛŒ Ø§Ø³Øª.";
        if (string.IsNullOrWhiteSpace(TopicCycle) || string.IsNullOrWhiteSpace(ContentSlot)) return "Ú†Ø±Ø®Ù‡ Ùˆ Ù†ÙˆØ¨Øª Ù…Ø­ØªÙˆØ§ÛŒÛŒ Ø¨Ø§ÛŒØ¯ Ù¾ÛŒØ´ Ø§Ø² ØµÙâ€ŒÚ¯Ø°Ø§Ø±ÛŒ Ø«Ø¨Øª Ø´ÙˆÙ†Ø¯.";
        if (MediaFormat is not ("post" or "reel" or "carousel" or "story")) return "Ù‚Ø§Ù„Ø¨ Ø±Ø³Ø§Ù†Ù‡ Ø¨Ø§ÛŒØ¯ ØµØ±ÛŒØ­Ø§Ù‹ postØŒ reelØŒ carousel ÛŒØ§ story Ø¨Ø§Ø´Ø¯.";
        if (ContentType switch
            {
                "post" => MediaFormat != "post",
                "reels" => MediaFormat != "reel",
                "carousel" => MediaFormat != "carousel",
                "story" => MediaFormat != "story",
                "DM" => MediaFormat is not ("post" or "reel" or "carousel"),
                "Affiliate" => MediaFormat != "reel",
                _ => true
            }) return ContentType == "Affiliate"
                ? "Ù…Ø­ØªÙˆØ§ÛŒ Affiliate ÙÙ‚Ø· Ø¨Ø§ mediaFormat=reel Ù…Ø¬Ø§Ø² Ø§Ø³ØªØ› postØŒ carousel Ùˆ story Ø§ÙÛŒÙ„ÛŒØª Ø±Ø¯ Ù…ÛŒâ€ŒØ´ÙˆÙ†Ø¯."
                : "Ù†ÙˆØ¹ Ù…Ø­ØªÙˆØ§ Ø¨Ø§ Ù‚Ø§Ù„Ø¨ Ø±Ø³Ø§Ù†Ù‡Ù” Ø§Ù†ØªØ®Ø§Ø¨â€ŒØ´Ø¯Ù‡ Ø³Ø§Ø²Ú¯Ø§Ø± Ù†ÛŒØ³Øª.";
        if (Language is not ("fa" or "tr" or "en")) return "Ø²Ø¨Ø§Ù† Ù…Ø­ØªÙˆØ§ Ø¨Ø§ÛŒØ¯ faØŒ tr ÛŒØ§ en Ø¨Ø§Ø´Ø¯.";
        if (Media is null) return "ÙÙ‡Ø±Ø³Øª Ø±Ø³Ø§Ù†Ù‡Ù” Ù…Ø­ØªÙˆØ§ Ø®Ø§Ù„ÛŒ Ø§Ø³Øª.";
        if (Caption is null) return "Ú©Ù¾Ø´Ù† Ù…Ø­ØªÙˆØ§ Ø®Ø§Ù„ÛŒ Ø§Ø³Øª.";
        if (string.IsNullOrWhiteSpace(Caption) && Kind != "story") return "Ú©Ù¾Ø´Ù† Ù…Ø­ØªÙˆØ§ÛŒ ÙÛŒØ¯ Ø¨Ø§ÛŒØ¯ Ú©Ø§Ù…Ù„ Ø¨Ø§Ø´Ø¯.";
        if (Caption.Length > 2200) return "Ú©Ù¾Ø´Ù† Instagram Ù†Ø¨Ø§ÛŒØ¯ Ø¨ÛŒØ´ Ø§Ø² Û²Û²Û°Û° Ù†ÙˆÛŒØ³Ù‡ Ø¨Ø§Ø´Ø¯.";
        if (!string.IsNullOrWhiteSpace(YoutubeTitle) && YoutubeTitle.Length > 100) return "Ø¹Ù†ÙˆØ§Ù† YouTube Ù†Ø¨Ø§ÛŒØ¯ Ø¨ÛŒØ´ Ø§Ø² Û±Û°Û° Ù†ÙˆÛŒØ³Ù‡ Ø¨Ø§Ø´Ø¯.";
        if (YoutubeTags is null || YoutubeTags.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > 100) || YoutubeTags.Sum(t => t.Length) > 500) return "Ø¨Ø±Ú†Ø³Ø¨â€ŒÙ‡Ø§ÛŒ YouTube Ø¨Ø§ÛŒØ¯ ØºÛŒØ±Ø®Ø§Ù„ÛŒ Ùˆ Ø¯Ø± Ù…Ø­Ø¯ÙˆØ¯ÛŒØª Ø·ÙˆÙ„ Ø¨Ø§Ø´Ù†Ø¯.";
        if (string.IsNullOrWhiteSpace(TargetPlatform) || TargetPlatform != "instagram" ||
            string.IsNullOrWhiteSpace(TargetAccountId) || TargetAccountId != ZernioAutomationBuilder.InstagramAccountId)
            return "Ù¾Ù„ØªÙØ±Ù… Ùˆ Ø´Ù†Ø§Ø³Ù‡Ù” Ø­Ø³Ø§Ø¨ Instagram Ù…Ù‚ØµØ¯ Ø¨Ø§ÛŒØ¯ ØµØ±ÛŒØ­ Ùˆ Ø¨Ø§ Ø­Ø³Ø§Ø¨ Ù…ØµÙˆØ¨ ØµÙ Ø¨Ø±Ø§Ø¨Ø± Ø¨Ø§Ø´Ù†Ø¯.";
        if (string.IsNullOrWhiteSpace(Cta) || Cta.Length > 500) return "Ø¯Ø¹ÙˆØª Ø¨Ù‡ Ø§Ù‚Ø¯Ø§Ù… Ø¨Ø§ÛŒØ¯ Ú©Ø§Ù…Ù„ Ùˆ Ø­Ø¯Ø§Ú©Ø«Ø± ÛµÛ°Û° Ù†ÙˆÛŒØ³Ù‡ Ø¨Ø§Ø´Ø¯.";
        if (ProductionStatus != "final" || !PreviewReviewed) return "ÙÙ‚Ø· Ø±Ø³Ø§Ù†Ù‡Ù” Ù†Ù‡Ø§ÛŒÛŒ Ùˆ Ù¾ÛŒØ´â€ŒÙ†Ù…Ø§ÛŒØ´ Ø¨Ø§Ø²Ø¨ÛŒÙ†ÛŒâ€ŒØ´Ø¯Ù‡ ÙˆØ§Ø±Ø¯ ØµÙ ØªØ£ÛŒÛŒØ¯ Ù…ÛŒâ€ŒØ´ÙˆØ¯.";
        if (ContentType == "Affiliate" && string.IsNullOrWhiteSpace(AffiliateDisclosure)) return "Ù…Ø­ØªÙˆØ§ÛŒ Affiliate Ø¨Ø§ÛŒØ¯ Ø§ÙØ´Ø§ÛŒ Ù‡Ù…Ú©Ø§Ø±ÛŒ Ø±Ø§ Ø¨Ù‡â€ŒØµÙˆØ±Øª Ú©Ø§Ù…Ù„ Ø¯Ø§Ø´ØªÙ‡ Ø¨Ø§Ø´Ø¯.";
        if (ContentType == "Affiliate" && RepublishToConnectedPlatforms) return "Affiliate ÙÙ‚Ø· Ø¨Ù‡â€ŒØµÙˆØ±Øª Reel Ø¯Ø± Instagram Ù…Ù†ØªØ´Ø± Ù…ÛŒâ€ŒØ´ÙˆØ¯Ø› Ø¨Ø§Ø²Ù†Ø´Ø± Ø¢Ù† Ø¨Ù‡ Ù…Ù‚ØµØ¯Ù‡Ø§ÛŒ Ø¯ÛŒÚ¯Ø± Ø¨Ø§ÛŒØ¯ Ø®Ø§Ù…ÙˆØ´ Ø¨Ø§Ø´Ø¯.";
        if (PublishAt == default) return "Ø²Ù…Ø§Ù† Ø§Ù†ØªØ´Ø§Ø± ØªØ¹ÛŒÛŒÙ† Ù†Ø´Ø¯Ù‡ Ø§Ø³Øª.";
        if (string.IsNullOrWhiteSpace(TimeZoneId)) return "Ù…Ù†Ø·Ù‚Ù‡Ù” Ø²Ù…Ø§Ù†ÛŒ Ø²Ù…Ø§Ù† Ø§Ù†ØªØ´Ø§Ø± Ø¨Ø§ÛŒØ¯ ØµØ±ÛŒØ­ Ø«Ø¨Øª Ø´ÙˆØ¯.";
        TimeZoneInfo timeZone;
        try { timeZone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId); }
        catch (TimeZoneNotFoundException) { return "Ø´Ù†Ø§Ø³Ù‡Ù” Ù…Ù†Ø·Ù‚Ù‡Ù” Ø²Ù…Ø§Ù†ÛŒ Ù…Ø¹ØªØ¨Ø± Ù†ÛŒØ³Øª."; }
        catch (InvalidTimeZoneException) { return "Ø´Ù†Ø§Ø³Ù‡Ù” Ù…Ù†Ø·Ù‚Ù‡Ù” Ø²Ù…Ø§Ù†ÛŒ Ù…Ø¹ØªØ¨Ø± Ù†ÛŒØ³Øª."; }
        if (TimeZoneInfo.ConvertTime(PublishAt, timeZone).Offset != PublishAt.Offset) return "Ø§Ø®ØªÙ„Ø§Ù Ø²Ù…Ø§Ù†ÛŒ PublishAt Ø¨Ø§ Ù…Ù†Ø·Ù‚Ù‡Ù” Ø²Ù…Ø§Ù†ÛŒ Ø«Ø¨Øªâ€ŒØ´Ø¯Ù‡ Ø³Ø§Ø²Ú¯Ø§Ø± Ù†ÛŒØ³Øª.";
        if (Media.Count == 0 || Media.Count > 10) return "Ù…Ø­ØªÙˆØ§ Ø¨Ø§ÛŒØ¯ ÛŒÚ© ØªØ§ Ø¯Ù‡ ÙØ§ÛŒÙ„ Ø±Ø³Ø§Ù†Ù‡â€ŒØ§ÛŒ Ø¯Ø§Ø´ØªÙ‡ Ø¨Ø§Ø´Ø¯.";
        foreach (var item in Media)
        {
            if (item is null) return "ÛŒÚ©ÛŒ Ø§Ø² Ø±Ø³Ø§Ù†Ù‡â€ŒÙ‡Ø§ÛŒ ØµÙ Ø®Ø§Ù„ÛŒ Ø§Ø³Øª.";
            if (!IsQueueMediaPath(item.Path)) return "Ø±Ø³Ø§Ù†Ù‡ Ø¨Ø§ÛŒØ¯ ÛŒÚ© ÙØ§ÛŒÙ„ Ù…Ø³ØªÙ‚ÛŒÙ… Ø§Ø² Ù¾ÙˆØ´Ù‡Ù” Ø§Ù…Ù† ØµÙ Ø§Ù†ØªØ´Ø§Ø± Ø¨Ø§Ø´Ø¯.";
            if (!string.IsNullOrWhiteSpace(item.PreviewPath) && !IsQueueMediaPath(item.PreviewPath))
                return "Ù¾ÛŒØ´â€ŒÙ†Ù…Ø§ÛŒØ´ Ø¨Ø§ÛŒØ¯ ÛŒÚ© ÙØ§ÛŒÙ„ Ù…Ø³ØªÙ‚ÛŒÙ… Ø§Ø² Ù¾ÙˆØ´Ù‡Ù” Ø§Ù…Ù† ØµÙ Ø§Ù†ØªØ´Ø§Ø± Ø¨Ø§Ø´Ø¯.";
            if (item.Type is not ("image" or "video")) return "Ù†ÙˆØ¹ ÛŒÚ©ÛŒ Ø§Ø² ÙØ§ÛŒÙ„â€ŒÙ‡Ø§ÛŒ Ø±Ø³Ø§Ù†Ù‡ Ù…Ø¹ØªØ¨Ø± Ù†ÛŒØ³Øª.";
            if (item.Type == "video" && string.IsNullOrWhiteSpace(item.PreviewPath)) return "Ø¨Ø±Ø§ÛŒ Ù‡Ø± ÙˆÛŒØ¯Ø¦ÙˆØŒ ØªØµÙˆÛŒØ± Ù¾ÛŒØ´â€ŒÙ†Ù…Ø§ÛŒØ´ Ù†Ù‡Ø§ÛŒÛŒ Ùˆ Ø¨Ø§Ø²Ø¨ÛŒÙ†ÛŒâ€ŒØ´Ø¯Ù‡ Ù„Ø§Ø²Ù… Ø§Ø³Øª.";
            if (item.Transcript is { Length: > 12000 }) return "Ù…ØªÙ† Ú¯ÙØªØ§Ø± Ù‡Ø± Ø±Ø³Ø§Ù†Ù‡ Ù†Ø¨Ø§ÛŒØ¯ Ø¨ÛŒØ´ Ø§Ø² Û±Û²Û°Û°Û° Ù†ÙˆÛŒØ³Ù‡ Ø¨Ø§Ø´Ø¯.";
            if (item.Type == "video" && string.IsNullOrWhiteSpace(item.Transcript))
                return "Ø¨Ø±Ø§ÛŒ Ù‡Ø± ÙˆÛŒØ¯Ø¦ÙˆØŒ Ù…ØªÙ† Ù¾ÛŒØ§Ø¯Ù‡â€ŒØ´Ø¯Ù‡Ù” Ú¯ÙØªØ§Ø± (transcript) Ù‡Ù…â€ŒØ±Ø§Ù‡ Ø®ÙˆØ¯ ÙˆÛŒØ¯Ø¦Ùˆ Ù„Ø§Ø²Ù… Ø§Ø³ØªØ› ÙˆÛŒØ¯Ø¦ÙˆÛŒ Ø¨Ø¯ÙˆÙ† Ù…ØªÙ† Ú¯ÙØªØ§Ø± Ù†Ø§Ù‚Øµ Ø§Ø³Øª (Ø¨Ù†Ø¯ Û·Ù«Û´ Ø±Ø§Ù‡Ù†Ù…Ø§ÛŒ ØªÙˆÙ„ÛŒØ¯ Ù…Ø­ØªÙˆØ§).";
        }
        if (Kind == "post" && (Media.Count != 1 || Media[0].Type != "image")) return "Ù¾Ø³Øª ØªØµÙˆÛŒØ±ÛŒ Ø¨Ø§ÛŒØ¯ Ø¯Ù‚ÛŒÙ‚Ø§Ù‹ ÛŒÚ© Ø¹Ú©Ø³ Ø¯Ø§Ø´ØªÙ‡ Ø¨Ø§Ø´Ø¯Ø› ÙˆÛŒØ¯Ø¦Ùˆ Ø±Ø§ Ø¨Ø§ Ù‚Ø§Ù„Ø¨ Reel Ø«Ø¨Øª Ú©Ù†ÛŒØ¯.";
        if (Kind == "reel" && (Media.Count != 1 || Media[0].Type != "video")) return "Ø±ÛŒÙ„ Ø¨Ø§ÛŒØ¯ Ø¯Ù‚ÛŒÙ‚Ø§Ù‹ ÛŒÚ© ÙˆÛŒØ¯Ø¦Ùˆ Ø¯Ø§Ø´ØªÙ‡ Ø¨Ø§Ø´Ø¯.";
        if (Kind == "carousel" && Media.Count < 2) return "Ú©Ø§Ø±ÙˆØ³Ù„ Ø¨Ø§ÛŒØ¯ Ø¯Ø³Øªâ€ŒÚ©Ù… Ø¯Ùˆ Ø±Ø³Ø§Ù†Ù‡ Ø¯Ø§Ø´ØªÙ‡ Ø¨Ø§Ø´Ø¯.";
        if (Kind == "story" && (Media.Count != 1 || !string.IsNullOrWhiteSpace(Caption))) return "Ø§Ø³ØªÙˆØ±ÛŒ Ø¨Ø§ÛŒØ¯ ÛŒÚ© Ø±Ø³Ø§Ù†Ù‡ Ø¯Ø§Ø´ØªÙ‡ Ø¨Ø§Ø´Ø¯Ø› API Ø§Ø³ØªÙˆØ±ÛŒ Ú©Ù¾Ø´Ù† Ø±Ø§ Ù†Ù…Ø§ÛŒØ´ Ù†Ù…ÛŒâ€ŒØ¯Ù‡Ø¯ØŒ Ù…ØªÙ† Ø±Ø§ Ø±ÙˆÛŒ Ø®ÙˆØ¯ Ø±Ø³Ø§Ù†Ù‡ Ø¨Ú¯Ø°Ø§Ø±ÛŒØ¯.";
        if (Kind == "story" && Engagement is not null) return "Ø§ØªÙˆÙ…Ø§Ø³ÛŒÙˆÙ† Ú©Ø§Ù…Ù†Øª Ø¨Ø±Ø§ÛŒ Ø§Ø³ØªÙˆØ±ÛŒ Ø¯Ø± Ø§ÛŒÙ† ØµÙ Ù¾Ø´ØªÛŒØ¨Ø§Ù†ÛŒ Ù†Ù…ÛŒâ€ŒØ´ÙˆØ¯.";
        if (Kind == "story" && RepublishToConnectedPlatforms) return "Ø§Ø³ØªÙˆØ±ÛŒ ÙÙ‚Ø· Ø¯Ø± Instagram Ù…Ù†ØªØ´Ø± Ù…ÛŒâ€ŒØ´ÙˆØ¯Ø› Ø¨Ø§Ø²Ù†Ø´Ø± Ø¨Ù‡ Ù…Ù‚ØµØ¯Ù‡Ø§ÛŒ Ø¯ÛŒÚ¯Ø± Ø¨Ø§ÛŒØ¯ Ø®Ø§Ù…ÙˆØ´ Ø¨Ø§Ø´Ø¯.";
        if (ContentType == "DM")
        {
            if (Engagement is null || Engagement.Kind != "interactive") return "Ù†ÙˆØ¹ DM Ø¨Ù‡ Ø§ØªÙˆÙ…Ø§Ø³ÛŒÙˆÙ† ØªØ¹Ø§Ù…Ù„ÛŒÙ Ú©Ø§Ù…Ù„ Ù†ÛŒØ§Ø² Ø¯Ø§Ø±Ø¯.";
            if (Engagement.Keywords is null || Engagement.Keywords.Count == 0 || Engagement.Keywords.Any(k => string.IsNullOrWhiteSpace(k) || k.Length > 40))
                return "Ø¨Ø±Ø§ÛŒ Ø§ØªÙˆÙ…Ø§Ø³ÛŒÙˆÙ†ØŒ ÙˆØ§Ú˜Ù‡Ù” Ú©Ù„ÛŒØ¯ÛŒ Ú©ÙˆØªØ§Ù‡ Ùˆ Ø±ÙˆØ´Ù† Ù„Ø§Ø²Ù… Ø§Ø³Øª.";
            if (string.IsNullOrWhiteSpace(Engagement.DmMessage) || Engagement.DmMessage.Length > 1000)
                return "Ù…ØªÙ† Ø¯Ø§ÛŒØ±Ú©Øª Ø§ØªÙˆÙ…Ø§Ø³ÛŒÙˆÙ† Ø¨Ø§ÛŒØ¯ Ø¢Ù…Ø§Ø¯Ù‡ Ùˆ Ø­Ø¯Ø§Ú©Ø«Ø± Û±Û°Û°Û° Ù†ÙˆÛŒØ³Ù‡ Ø¨Ø§Ø´Ø¯.";
            if (Engagement.Language != Language) return "Ø²Ø¨Ø§Ù† Ø§ØªÙˆÙ…Ø§Ø³ÛŒÙˆÙ† Ø¨Ø§ÛŒØ¯ Ø¨Ø§ Ø²Ø¨Ø§Ù† Ù…Ø­ØªÙˆØ§ÛŒ Ù…Ù†ØªØ´Ø±Ø´Ø¯Ù‡ ÛŒÚ©Ø³Ø§Ù† Ø¨Ø§Ø´Ø¯.";
            if (string.IsNullOrWhiteSpace(Engagement.FollowGateMessage) || string.IsNullOrWhiteSpace(Engagement.NotFollowingMessage) || string.IsNullOrWhiteSpace(Engagement.FollowButtonLabel))
                return "Ø¨Ø±Ø§ÛŒ Ù…Ø­ØªÙˆØ§ÛŒ DMØŒ Ù¾ÛŒØ§Ù… Ø§ÙˆÙ„ØŒ Ø¯Ú©Ù…Ù‡Ù” ÙØ§Ù„Ùˆ Ùˆ Ù¾ÛŒØ§Ù… ÙØ§Ù„Ùˆâ€ŒÙ†Ø¨ÙˆØ¯Ù† Ø¨Ø§ÛŒØ¯ Ø¢Ù…Ø§Ø¯Ù‡ Ø¨Ø§Ø´Ù†Ø¯.";
            if (Engagement.FollowButtonLabel.Length > 20) return "Ù†ÙˆØ´ØªÙ‡Ù” Ø¯Ú©Ù…Ù‡Ù” ÙØ§Ù„Ùˆ Ø¨Ø§ÛŒØ¯ Ø­Ø¯Ø§Ú©Ø«Ø± Û²Û° Ù†ÙˆÛŒØ³Ù‡ Ø¨Ø§Ø´Ø¯.";
        }
        else if (Engagement is not null) return "Ø§ØªÙˆÙ…Ø§Ø³ÛŒÙˆÙ† Ø²Ø±Ù†ÛŒÙˆ ÙÙ‚Ø· Ø¨Ø±Ø§ÛŒ contentType=DM Ù…Ø¬Ø§Ø² Ø§Ø³ØªØ› Affiliate Ø¨Ù‡ Ù¾ÙˆØ±ØªØ§Ù„ Ù…ÛŒâ€ŒØ±ÙˆØ¯.";
        if (TrialRole is not null && TrialRole is not ("trial" or "main")) return "Ù†ÙˆØ¹ Ù†Ø³Ø®Ù‡ Ø¨Ø§ÛŒØ¯ trial ÛŒØ§ main Ø¨Ø§Ø´Ø¯.";
        if (TrialRole is not null && Kind != "reel") return "Ù‚Ø§Ù†ÙˆÙ† ØªØ±Ø§ÛŒØ§Ù„ ÙÙ‚Ø· Ø¨Ø±Ø§ÛŒ Ø±ÛŒÙ„ Ù‚Ø§Ø¨Ù„ Ø§Ø³ØªÙØ§Ø¯Ù‡ Ø§Ø³Øª.";
        if (TrialRole is not null && string.IsNullOrWhiteSpace(TrialGroupId)) return "Ù†Ø³Ø®Ù‡Ù” ØªØ±Ø§ÛŒØ§Ù„ Ùˆ Ø§ØµÙ„ÛŒ Ø¨Ø§ÛŒØ¯ Ø´Ù†Ø§Ø³Ù‡Ù” Ú¯Ø±ÙˆÙ‡ Ù…Ø´ØªØ±Ú© Ø¯Ø§Ø´ØªÙ‡ Ø¨Ø§Ø´Ù†Ø¯.";
        return "";
    }
}

public sealed class PublishMediaItem
{
    public string Path { get; set; } = "";
    public string Type { get; set; } = "image";
    public string? PreviewPath { get; set; }
    /// <summary>
    /// Spoken-text transcript of the media file. Owner law (2026-10-03, daily-content guide Â§7.4): the video must
    /// always travel together with its transcript so the owner reviews the spoken words, not only the picture.
    /// </summary>
    public string? Transcript { get; set; }
    /// <summary>Language of <see cref="Transcript"/> when it is known (fa | tr | en).</summary>
    public string? TranscriptLanguage { get; set; }
}

/// <summary>One per-post automation. Zernio permits one active automation per post, so the two kinds are mutually exclusive.</summary>
public sealed class EngagementAutomation
{
    public string Kind { get; set; } = "interactive"; // interactive; reserved for contentType=DM
    public string Language { get; set; } = "";
    public List<string> Keywords { get; set; } = new();
    public string DmMessage { get; set; } = "";
    public string? FollowGateMessage { get; set; }
    public string? FollowButtonLabel { get; set; }
    public string? NotFollowingMessage { get; set; }
}

public sealed record QueueMediaFile(string RepositoryPath, string LocalPath, string Type, string? PreviewLocalPath,
    string RepositorySha = "", string? PreviewRepositoryPath = null, string? PreviewRepositorySha = null,
    string? Transcript = null, string? TranscriptLanguage = null);

public sealed record PublishQueueCard(
    PublishQueueItem Item,
    string ReadyFilePath,
    IReadOnlyList<QueueMediaFile> MediaFiles,
    string ReadyFileSha = "");

public sealed record PublishActionRecord(
    string ItemId,
    string Action,
    DateTimeOffset At,
    string? Feedback = null,
    string? AgentNote = null,
    string? ReadyFileSha = null,
    IReadOnlyList<ApprovedQueueMedia>? Media = null);

public sealed record ApprovedQueueMedia(string Path, string Sha);

public static class PublishIdempotencyKey
{
    /// <summary>Deterministic UUID for one logical queue item and destination, suitable for Zernio's Idempotency-Key header.</summary>
    public static string For(string itemId, string destination)
    {
        if (string.IsNullOrWhiteSpace(itemId) || string.IsNullOrWhiteSpace(destination))
            throw new ArgumentException("Ø´Ù†Ø§Ø³Ù‡Ù” Ù…Ø­ØªÙˆØ§ Ùˆ Ù…Ù‚ØµØ¯ Ø¨Ø±Ø§ÛŒ Ú©Ù„ÛŒØ¯ Ø§Ù†ØªØ´Ø§Ø± Ù„Ø§Ø²Ù…â€ŒØ§Ù†Ø¯.");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(itemId + "|" + destination));
        return new Guid(hash.AsSpan(0, 16)).ToString("D");
    }
}

public sealed record FacebookPageSelection(string ProfileId, bool Active, string SelectedPageId);

public static class ManusLocationGuard
{
    public static bool IsBound(string expectedPageId, string instagramProfileId, IEnumerable<FacebookPageSelection> pages) =>
        !string.IsNullOrWhiteSpace(expectedPageId) && !string.IsNullOrWhiteSpace(instagramProfileId) &&
        pages.Any(page => page.Active &&
            string.Equals(page.ProfileId, instagramProfileId, StringComparison.Ordinal) &&
            string.Equals(page.SelectedPageId, expectedPageId, StringComparison.Ordinal));
}

/// <summary>Ensures approval still refers to the exact JSON and binaries shown to the owner.</summary>
public static class PublishApprovalGuard
{
    public static string Validate(PublishQueueItem item, string itemId, PublishActionRecord approval,
        string currentReadyFileSha, IReadOnlyDictionary<string, string> currentMediaShas)
    {
        if (!string.Equals(approval.Action, "approved", StringComparison.Ordinal) ||
            !string.Equals(approval.ItemId, itemId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(item.Id, itemId, StringComparison.OrdinalIgnoreCase))
            return "Ø±Ø³ÛŒØ¯ ØªØ£ÛŒÛŒØ¯ Ø¨Ø§ Ø´Ù†Ø§Ø³Ù‡Ù” Ù…Ø­ØªÙˆØ§ Ø¨Ø±Ø§Ø¨Ø± Ù†ÛŒØ³ØªØ› Ø§Ù†ØªØ´Ø§Ø± Ù…ØªÙˆÙ‚Ù Ø´Ø¯.";
        if (!IsGitSha(approval.ReadyFileSha) || !IsGitSha(currentReadyFileSha) ||
            !string.Equals(approval.ReadyFileSha, currentReadyFileSha, StringComparison.OrdinalIgnoreCase))
            return "ÙØ§ÛŒÙ„ Ù…Ø­ØªÙˆØ§ Ù¾Ø³ Ø§Ø² ØªØ£ÛŒÛŒØ¯ ØªØºÛŒÛŒØ± Ú©Ø±Ø¯Ù‡ ÛŒØ§ Ù†Ø³Ø®Ù‡Ù” Ø±Ø³ÛŒØ¯ Ù‚Ø¯ÛŒÙ…ÛŒ Ø§Ø³ØªØ› Ø²Ù…Ø§Ù†/ØªØ£ÛŒÛŒØ¯ ØªØ§Ø²Ù‡ Ù„Ø§Ø²Ù… Ø§Ø³Øª.";

        var expectedPaths = item.Media.SelectMany(m => string.IsNullOrWhiteSpace(m.PreviewPath)
                ? new[] { m.Path }
                : new[] { m.Path, m.PreviewPath! })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
        if (approval.Media is null)
            return "Ø±Ø³ÛŒØ¯ ØªØ£ÛŒÛŒØ¯ ÙÙ‡Ø±Ø³Øª Ø±Ø³Ø§Ù†Ù‡â€ŒÙ‡Ø§ÛŒ Ø¯ÛŒØ¯Ù‡â€ŒØ´Ø¯Ù‡ Ø±Ø§ Ù†Ø¯Ø§Ø±Ø¯Ø› Ø§Ù†ØªØ´Ø§Ø± Ù…ØªÙˆÙ‚Ù Ø´Ø¯.";
        var approvedMedia = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var media in approval.Media)
        {
            if (media is null || string.IsNullOrWhiteSpace(media.Path) || !IsGitSha(media.Sha))
                return "Ø±Ø³ÛŒØ¯ ØªØ£ÛŒÛŒØ¯ ÛŒÚ©ÛŒ Ø§Ø² Ø´Ù†Ø§Ø³Ù‡â€ŒÙ‡Ø§ÛŒ Ø±Ø³Ø§Ù†Ù‡ ÛŒØ§ SHA Ù…Ø¹ØªØ¨Ø± Ø±Ø§ Ù†Ø¯Ø§Ø±Ø¯Ø› Ø§Ù†ØªØ´Ø§Ø± Ù…ØªÙˆÙ‚Ù Ø´Ø¯.";
            if (!approvedMedia.TryAdd(media.Path, media.Sha))
                return "Ø±Ø³ÛŒØ¯ ØªØ£ÛŒÛŒØ¯ ÙÙ‡Ø±Ø³Øª Ø±Ø³Ø§Ù†Ù‡â€ŒÙ‡Ø§ÛŒ ØªÚ©Ø±Ø§Ø±ÛŒ Ø¯Ø§Ø±Ø¯Ø› Ø§Ù†ØªØ´Ø§Ø± Ù…ØªÙˆÙ‚Ù Ø´Ø¯.";
        }
        if (approvedMedia.Count != expectedPaths.Length ||
            !expectedPaths.SequenceEqual(approvedMedia.Keys.OrderBy(p => p, StringComparer.Ordinal), StringComparer.Ordinal))
            return "Ø±Ø³ÛŒØ¯ ØªØ£ÛŒÛŒØ¯ ÙÙ‡Ø±Ø³Øª Ø±Ø³Ø§Ù†Ù‡â€ŒÙ‡Ø§ÛŒ Ø¯ÛŒØ¯Ù‡â€ŒØ´Ø¯Ù‡ Ø±Ø§ Ù†Ø¯Ø§Ø±Ø¯ ÛŒØ§ Ø¨Ø§ Ù…Ø­ØªÙˆØ§ ÙØ±Ù‚ Ø¯Ø§Ø±Ø¯Ø› Ø§Ù†ØªØ´Ø§Ø± Ù…ØªÙˆÙ‚Ù Ø´Ø¯.";
        foreach (var path in expectedPaths)
        {
            if (!approvedMedia.TryGetValue(path, out var approvedSha) || !IsGitSha(approvedSha) ||
                !currentMediaShas.TryGetValue(path, out var currentSha) || !IsGitSha(currentSha) ||
                !string.Equals(approvedSha, currentSha, StringComparison.OrdinalIgnoreCase))
                return "ÛŒÚ©ÛŒ Ø§Ø² Ø±Ø³Ø§Ù†Ù‡â€ŒÙ‡Ø§ Ù¾Ø³ Ø§Ø² ØªØ£ÛŒÛŒØ¯ ØªØºÛŒÛŒØ± Ú©Ø±Ø¯Ù‡ ÛŒØ§ Ø¯Ø± Ø¯Ø³ØªØ±Ø³ Ù†ÛŒØ³ØªØ› ØªØ£ÛŒÛŒØ¯ ØªØ§Ø²Ù‡ Ù„Ø§Ø²Ù… Ø§Ø³Øª.";
        }
        return "";
    }

    private static bool IsGitSha(string? sha) => sha is { Length: 40 } && sha.All(Uri.IsHexDigit);
}

public static class ZernioAutomationBuilder
{
    public const string InstagramAccountId = "6ab391ba8d284ffb21332381";
    public const string InstagramProfileName = "bazinopro";
    public const string InstagramLocationId = "1091945074011846";
    public const string FacebookPageId = "1091945074011846";

    public static JsonObject Build(PublishQueueItem item, string accountId, string profileId, string platformMediaId)
    {
        ArgumentNullException.ThrowIfNull(item);
        var error = item.Validate();
        if (error.Length != 0) throw new InvalidOperationException(error);
        if (item.Engagement is null) throw new InvalidOperationException("Ø¨Ø±Ø§ÛŒ Ø§ÛŒÙ† Ù…Ø­ØªÙˆØ§ Ø§ØªÙˆÙ…Ø§Ø³ÛŒÙˆÙ† Ú©Ø§Ù…Ù†ØªÛŒ ØªÙ†Ø¸ÛŒÙ… Ù†Ø´Ø¯Ù‡ Ø§Ø³Øª.");
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(profileId) || string.IsNullOrWhiteSpace(platformMediaId))
            throw new InvalidOperationException("Ø´Ù†Ø§Ø³Ù‡Ù” Ø­Ø³Ø§Ø¨ØŒ Ù¾Ø±ÙˆÙØ§ÛŒÙ„ Ùˆ media_id ÙˆØ§Ù‚Ø¹ÛŒ Instagram Ù„Ø§Ø²Ù… Ø§Ø³Øª.");

        var cfg = item.Engagement;
        if (item.ContentType != "DM" || cfg.Kind != "interactive")
            throw new InvalidOperationException("Ù¾Ø§Ø³Ø®â€ŒÚ¯ÙˆÛŒ Ø²Ø±Ù†ÛŒÙˆ ÙÙ‚Ø· Ø¨Ø±Ø§ÛŒ Ù…Ø­ØªÙˆØ§ÛŒ contentType=DM Ø³Ø§Ø®ØªÙ‡ Ù…ÛŒâ€ŒØ´ÙˆØ¯Ø› Affiliate Ø¨Ù‡ Ù¾ÙˆØ±ØªØ§Ù„ Ù…ÛŒâ€ŒØ±ÙˆØ¯.");
        var body = new JsonObject
        {
            ["profileId"] = profileId,
            ["accountId"] = accountId,
            ["platformPostId"] = platformMediaId,
            ["trigger"] = "comment",
            ["name"] = item.Title.Length > 100 ? item.Title[..100] : item.Title,
            ["postTitle"] = item.Title,
            ["keywords"] = new JsonArray(cfg.Keywords.Select(k => (JsonNode?)JsonValue.Create(k.Trim())).ToArray()),
            ["matchMode"] = "word",
            ["dmMessage"] = cfg.DmMessage,
            ["audience"] = new JsonObject { ["followerStatus"] = "follower", ["whenUnknown"] = "verify" },
            ["followGate"] = new JsonObject
            {
                ["message"] = cfg.FollowGateMessage,
                ["buttonLabel"] = cfg.FollowButtonLabel,
                ["notFollowingMessage"] = cfg.NotFollowingMessage
            }
        };
        return body;
    }
}

public static class ZernioPublishBuilder
{
    public static JsonObject BuildInstagramPost(PublishQueueItem item, string accountId, IReadOnlyList<string> publicMediaUrls)
    {
        var validation = item.Validate();
        if (validation.Length != 0) throw new InvalidOperationException(validation);
        if (string.IsNullOrWhiteSpace(accountId)) throw new InvalidOperationException("Ø´Ù†Ø§Ø³Ù‡Ù” Ø­Ø³Ø§Ø¨ Instagram Ø®Ø§Ù„ÛŒ Ø§Ø³Øª.");
        if (publicMediaUrls.Count != item.Media.Count || publicMediaUrls.Count == 0)
            throw new InvalidOperationException("ØªØ¹Ø¯Ø§Ø¯ Ù†Ø´Ø§Ù†ÛŒ Ø±Ø³Ø§Ù†Ù‡â€ŒÙ‡Ø§ÛŒ Ø¨Ø§Ø±Ú¯Ø°Ø§Ø±ÛŒâ€ŒØ´Ø¯Ù‡ Ø¨Ø§ ÙØ§ÛŒÙ„ ØªØ£ÛŒÛŒØ¯Ø´Ø¯Ù‡ ÛŒÚ©Ø³Ø§Ù† Ù†ÛŒØ³Øª.");
        var media = new JsonArray();
        for (var i = 0; i < publicMediaUrls.Count; i++)
            media.Add(new JsonObject { ["type"] = item.Media[i].Type, ["url"] = publicMediaUrls[i] });

        var platform = new JsonObject
        {
            ["platform"] = "instagram",
            ["accountId"] = accountId
        };
        // Stories are the owner's explicit location exception: do not look up, validate, or attach location data.
        if (item.Kind != "story")
            platform["platformSpecificData"] = new JsonObject { ["locationId"] = ZernioAutomationBuilder.InstagramLocationId };
        else
            platform["platformSpecificData"] = new JsonObject { ["contentType"] = "story" };
        var body = new JsonObject
        {
            ["title"] = item.Title,
            ["mediaItems"] = media,
            ["platforms"] = new JsonArray(platform),
            ["publishNow"] = true,
            ["metadata"] = new JsonObject
            {
                ["source"] = "bazino-marketing-studio",
                ["contentId"] = item.Id,
                ["engagementKind"] = item.Engagement?.Kind ?? "none",
                ["language"] = item.Language
            }
        };
        if (item.Kind != "story") body["content"] = item.Caption;
        return body;
    }
}

