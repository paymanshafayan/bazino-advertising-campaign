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
        ?? throw new InvalidDataException("محتوای صف خوانده نشد.");

    public string Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || !Regex.IsMatch(Id, "^[a-zA-Z0-9][a-zA-Z0-9._-]{0,79}$")) return "شناسهٔ محتوا باید فقط شامل حروف لاتین، عدد، خط تیره، نقطه یا زیرخط باشد.";
        if (string.IsNullOrWhiteSpace(Title) || Title.Length > 180) return "عنوان محتوا خالی است یا بیش از حد طولانی است.";
        if (!PublishContentCatalog.IsKnownType(ContentType)) return "نوع محتوا باید صریحاً post، reels، carousel، story، DM یا Affiliate باشد.";
        if (!PublishContentCatalog.IsTopicAllowed(ContentType, Topic)) return "موضوع با نوع محتوا سازگار نیست یا در نسخهٔ راهنمای روزانه وجود ندارد.";
        if (GuideVersion != PublishContentCatalog.GuideVersion) return "نسخهٔ راهنمای تولید برای این کارت خالی یا قدیمی است.";
        if (string.IsNullOrWhiteSpace(TopicCycle) || string.IsNullOrWhiteSpace(ContentSlot)) return "چرخه و نوبت محتوایی باید پیش از صف‌گذاری ثبت شوند.";
        if (MediaFormat is not ("post" or "reel" or "carousel" or "story")) return "قالب رسانه باید صریحاً post، reel، carousel یا story باشد.";
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
                ? "محتوای Affiliate فقط با mediaFormat=reel مجاز است؛ post، carousel و story افیلیت رد می‌شوند."
                : "نوع محتوا با قالب رسانهٔ انتخاب‌شده سازگار نیست.";
        if (Language is not ("fa" or "tr" or "en")) return "زبان محتوا باید fa، tr یا en باشد.";
        if (Media is null) return "فهرست رسانهٔ محتوا خالی است.";
        if (Caption is null) return "کپشن محتوا خالی است.";
        if (string.IsNullOrWhiteSpace(Caption) && Kind != "story") return "کپشن محتوای فید باید کامل باشد.";
        if (Caption.Length > 2200) return "کپشن Instagram نباید بیش از ۲۲۰۰ نویسه باشد.";
        if (!string.IsNullOrWhiteSpace(YoutubeTitle) && YoutubeTitle.Length > 100) return "عنوان YouTube نباید بیش از ۱۰۰ نویسه باشد.";
        if (YoutubeTags is null || YoutubeTags.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > 100) || YoutubeTags.Sum(t => t.Length) > 500) return "برچسب‌های YouTube باید غیرخالی و در محدودیت طول باشند.";
        if (string.IsNullOrWhiteSpace(TargetPlatform) || TargetPlatform != "instagram" ||
            string.IsNullOrWhiteSpace(TargetAccountId) || TargetAccountId != ZernioAutomationBuilder.InstagramAccountId)
            return "پلتفرم و شناسهٔ حساب Instagram مقصد باید صریح و با حساب مصوب صف برابر باشند.";
        if (string.IsNullOrWhiteSpace(Cta) || Cta.Length > 500) return "دعوت به اقدام باید کامل و حداکثر ۵۰۰ نویسه باشد.";
        if (ProductionStatus != "final" || !PreviewReviewed) return "فقط رسانهٔ نهایی و پیش‌نمایش بازبینی‌شده وارد صف تأیید می‌شود.";
        if (ContentType == "Affiliate" && string.IsNullOrWhiteSpace(AffiliateDisclosure)) return "محتوای Affiliate باید افشای همکاری را به‌صورت کامل داشته باشد.";
        if (ContentType == "Affiliate" && RepublishToConnectedPlatforms) return "Affiliate فقط به‌صورت Reel در Instagram منتشر می‌شود؛ بازنشر آن به مقصدهای دیگر باید خاموش باشد.";
        if (PublishAt == default) return "زمان انتشار تعیین نشده است.";
        if (string.IsNullOrWhiteSpace(TimeZoneId)) return "منطقهٔ زمانی زمان انتشار باید صریح ثبت شود.";
        TimeZoneInfo timeZone;
        try { timeZone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId); }
        catch (TimeZoneNotFoundException) { return "شناسهٔ منطقهٔ زمانی معتبر نیست."; }
        catch (InvalidTimeZoneException) { return "شناسهٔ منطقهٔ زمانی معتبر نیست."; }
        if (TimeZoneInfo.ConvertTime(PublishAt, timeZone).Offset != PublishAt.Offset) return "اختلاف زمانی PublishAt با منطقهٔ زمانی ثبت‌شده سازگار نیست.";
        if (Media.Count == 0 || Media.Count > 10) return "محتوا باید یک تا ده فایل رسانه‌ای داشته باشد.";
        foreach (var item in Media)
        {
            if (item is null) return "یکی از رسانه‌های صف خالی است.";
            if (!IsQueueMediaPath(item.Path)) return "رسانه باید یک فایل مستقیم از پوشهٔ امن صف انتشار باشد.";
            if (!string.IsNullOrWhiteSpace(item.PreviewPath) && !IsQueueMediaPath(item.PreviewPath))
                return "پیش‌نمایش باید یک فایل مستقیم از پوشهٔ امن صف انتشار باشد.";
            if (item.Type is not ("image" or "video")) return "نوع یکی از فایل‌های رسانه معتبر نیست.";
            if (item.Type == "video" && string.IsNullOrWhiteSpace(item.PreviewPath)) return "برای هر ویدئو، تصویر پیش‌نمایش نهایی و بازبینی‌شده لازم است.";
            if (item.Transcript is { Length: > 12000 }) return "متن گفتار هر رسانه نباید بیش از ۱۲۰۰۰ نویسه باشد.";
            if (item.Type == "video" && string.IsNullOrWhiteSpace(item.Transcript))
                return "برای هر ویدئو، متن پیاده‌شدهٔ گفتار (transcript) هم‌راه خود ویدئو لازم است؛ ویدئوی بدون متن گفتار ناقص است (بند ۷٫۴ راهنمای تولید محتوا).";
        }
        if (Kind == "post" && (Media.Count != 1 || Media[0].Type != "image")) return "پست تصویری باید دقیقاً یک عکس داشته باشد؛ ویدئو را با قالب Reel ثبت کنید.";
        if (Kind == "reel" && (Media.Count != 1 || Media[0].Type != "video")) return "ریل باید دقیقاً یک ویدئو داشته باشد.";
        if (Kind == "carousel" && Media.Count < 2) return "کاروسل باید دست‌کم دو رسانه داشته باشد.";
        if (Kind == "story" && (Media.Count != 1 || !string.IsNullOrWhiteSpace(Caption))) return "استوری باید یک رسانه داشته باشد؛ API استوری کپشن را نمایش نمی‌دهد، متن را روی خود رسانه بگذارید.";
        if (Kind == "story" && Engagement is not null) return "اتوماسیون کامنت برای استوری در این صف پشتیبانی نمی‌شود.";
        if (Kind == "story" && RepublishToConnectedPlatforms) return "استوری فقط در Instagram منتشر می‌شود؛ بازنشر به مقصدهای دیگر باید خاموش باشد.";
        if (ContentType == "DM")
        {
            if (Engagement is null || Engagement.Kind != "interactive") return "نوع DM به اتوماسیون تعاملیِ کامل نیاز دارد.";
            if (Engagement.Keywords is null || Engagement.Keywords.Count == 0 || Engagement.Keywords.Any(k => string.IsNullOrWhiteSpace(k) || k.Length > 40))
                return "برای اتوماسیون، واژهٔ کلیدی کوتاه و روشن لازم است.";
            if (string.IsNullOrWhiteSpace(Engagement.DmMessage) || Engagement.DmMessage.Length > 1000)
                return "متن دایرکت اتوماسیون باید آماده و حداکثر ۱۰۰۰ نویسه باشد.";
            if (Engagement.Language != Language) return "زبان اتوماسیون باید با زبان محتوای منتشرشده یکسان باشد.";
            if (string.IsNullOrWhiteSpace(Engagement.FollowGateMessage) || string.IsNullOrWhiteSpace(Engagement.NotFollowingMessage) || string.IsNullOrWhiteSpace(Engagement.FollowButtonLabel))
                return "برای محتوای DM، پیام اول، دکمهٔ فالو و پیام فالو‌نبودن باید آماده باشند.";
            if (Engagement.FollowButtonLabel.Length > 20) return "نوشتهٔ دکمهٔ فالو باید حداکثر ۲۰ نویسه باشد.";
        }
        else if (Engagement is not null) return "اتوماسیون زرنیو فقط برای contentType=DM مجاز است؛ Affiliate به پورتال می‌رود.";
        if (TrialRole is not null && TrialRole is not ("trial" or "main")) return "نوع نسخه باید trial یا main باشد.";
        if (TrialRole is not null && Kind != "reel") return "قانون ترایال فقط برای ریل قابل استفاده است.";
        if (TrialRole is not null && string.IsNullOrWhiteSpace(TrialGroupId)) return "نسخهٔ ترایال و اصلی باید شناسهٔ گروه مشترک داشته باشند.";
        return "";
    }
}

public sealed class PublishMediaItem
{
    public string Path { get; set; } = "";
    public string Type { get; set; } = "image";
    public string? PreviewPath { get; set; }
    /// <summary>
    /// Spoken-text transcript of the media file. Owner law (2026-10-03, daily-content guide §7.4): the video must
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
            throw new ArgumentException("شناسهٔ محتوا و مقصد برای کلید انتشار لازم‌اند.");
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
            return "رسید تأیید با شناسهٔ محتوا برابر نیست؛ انتشار متوقف شد.";
        if (!IsGitSha(approval.ReadyFileSha) || !IsGitSha(currentReadyFileSha) ||
            !string.Equals(approval.ReadyFileSha, currentReadyFileSha, StringComparison.OrdinalIgnoreCase))
            return "فایل محتوا پس از تأیید تغییر کرده یا نسخهٔ رسید قدیمی است؛ زمان/تأیید تازه لازم است.";

        var expectedPaths = item.Media.SelectMany(m => string.IsNullOrWhiteSpace(m.PreviewPath)
                ? new[] { m.Path }
                : new[] { m.Path, m.PreviewPath! })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
        if (approval.Media is null)
            return "رسید تأیید فهرست رسانه‌های دیده‌شده را ندارد؛ انتشار متوقف شد.";
        var approvedMedia = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var media in approval.Media)
        {
            if (media is null || string.IsNullOrWhiteSpace(media.Path) || !IsGitSha(media.Sha))
                return "رسید تأیید یکی از شناسه‌های رسانه یا SHA معتبر را ندارد؛ انتشار متوقف شد.";
            if (!approvedMedia.TryAdd(media.Path, media.Sha))
                return "رسید تأیید فهرست رسانه‌های تکراری دارد؛ انتشار متوقف شد.";
        }
        if (approvedMedia.Count != expectedPaths.Length ||
            !expectedPaths.SequenceEqual(approvedMedia.Keys.OrderBy(p => p, StringComparer.Ordinal), StringComparer.Ordinal))
            return "رسید تأیید فهرست رسانه‌های دیده‌شده را ندارد یا با محتوا فرق دارد؛ انتشار متوقف شد.";
        foreach (var path in expectedPaths)
        {
            if (!approvedMedia.TryGetValue(path, out var approvedSha) || !IsGitSha(approvedSha) ||
                !currentMediaShas.TryGetValue(path, out var currentSha) || !IsGitSha(currentSha) ||
                !string.Equals(approvedSha, currentSha, StringComparison.OrdinalIgnoreCase))
                return "یکی از رسانه‌ها پس از تأیید تغییر کرده یا در دسترس نیست؛ تأیید تازه لازم است.";
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
        if (item.Engagement is null) throw new InvalidOperationException("برای این محتوا اتوماسیون کامنتی تنظیم نشده است.");
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(profileId) || string.IsNullOrWhiteSpace(platformMediaId))
            throw new InvalidOperationException("شناسهٔ حساب، پروفایل و media_id واقعی Instagram لازم است.");

        var cfg = item.Engagement;
        if (item.ContentType != "DM" || cfg.Kind != "interactive")
            throw new InvalidOperationException("پاسخ‌گوی زرنیو فقط برای محتوای contentType=DM ساخته می‌شود؛ Affiliate به پورتال می‌رود.");
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
        if (string.IsNullOrWhiteSpace(accountId)) throw new InvalidOperationException("شناسهٔ حساب Instagram خالی است.");
        if (publicMediaUrls.Count != item.Media.Count || publicMediaUrls.Count == 0)
            throw new InvalidOperationException("تعداد نشانی رسانه‌های بارگذاری‌شده با فایل تأییدشده یکسان نیست.");
        var media = new JsonArray();
        for (var i = 0; i < publicMediaUrls.Count; i++)
            media.Add(new JsonObject { ["type"] = item.Media[i].Type, ["url"] = publicMediaUrls[i] });

        var platform = new JsonObject
        {
            ["platform"] = "instagram",
            ["accountId"] = accountId
        };
        // Stories are the owner's explicit location exception: do not look up, validate, or attach location data.
        // Official Zernio contract: a Story publishes only with platformSpecificData.contentType = "story";
        // without the flag Zernio publishes the media as a regular feed post (owner incident 2026-10-04).
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
