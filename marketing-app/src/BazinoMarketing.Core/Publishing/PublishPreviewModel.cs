using System.Globalization;
using System.Text;

namespace BazinoMarketing.Core.Publishing;

/// <summary>
/// One slide of the Instagram-like review card: exactly one media file of the queue item.
/// A carousel hands over every media file in order; a reel hands over its single video.
/// </summary>
public sealed record PublishPreviewSlide(
    int Index,
    int Total,
    string MediaType,
    string? ImagePath,
    string? VideoPath,
    string PlaceholderText,
    string? Transcript = null,
    string? TranscriptLanguage = null)
{
    /// <summary>1-based slide number in Persian digits, e.g. «۲».</summary>
    public string NumberText => PublishPreviewText.PersianDigits(Index + 1);

    public string PositionText => $"{NumberText} از {PublishPreviewText.PersianDigits(Total)}";

    public bool IsVideo => string.Equals(MediaType, "video", StringComparison.OrdinalIgnoreCase);

    /// <summary>Owner law 2026-10-03 (§7.4): the video is always reviewed together with its spoken text.</summary>
    public bool HasTranscript => !string.IsNullOrWhiteSpace(Transcript);

    public string TranscriptText => Transcript ?? "";

    /// <summary>Label of the transcript block; the language is named when the item declares it.</summary>
    public string TranscriptHeader => HasTranscript
        ? string.IsNullOrWhiteSpace(TranscriptLanguage)
            ? "متن گفتار (transcript)"
            : "متن گفتار (transcript) — زبان: " + PublishPreviewText.Language(TranscriptLanguage)
        : "";
}

/// <summary>One labelled row of the card header. Value is never empty: it falls back to «—».</summary>
public sealed record PublishPreviewField(string Label, string Value);

/// <summary>
/// Presentation-independent model behind the «محتوای آماده انتشار» card. It is built from the queue item and the
/// media files that were really downloaded, so it can be unit-tested without WPF. It produces no text of its own:
/// every value comes from the item (title, caption, cta, media) or from the fixed labels/fallback in this file.
/// </summary>
public sealed class PublishPreviewModel
{
    /// <summary>Shown when the item carries no value for a field. The card must never show an empty field.</summary>
    public const string EmptyValue = "—";

    private PublishPreviewModel(
        PublishQueueItem item,
        IReadOnlyList<PublishPreviewSlide> slides,
        IReadOnlyList<PublishPreviewField> fields)
    {
        Item = item;
        Slides = slides;
        Fields = fields;
    }

    public PublishQueueItem Item { get; }
    public IReadOnlyList<PublishPreviewSlide> Slides { get; }
    public IReadOnlyList<PublishPreviewField> Fields { get; }

    public bool HasMultipleSlides => Slides.Count > 1;
    public bool HasSlides => Slides.Count > 0;
    public string Title => PublishPreviewText.OrFallback(Item.Title);
    public string Caption => Item.Caption ?? "";
    public string Cta => Item.Cta ?? "";
    public string AffiliateDisclosure => Item.AffiliateDisclosure ?? "";
    public bool HasAffiliateDisclosure => !string.IsNullOrWhiteSpace(Item.AffiliateDisclosure);

    /// <summary>Media aspect ratio of the frame: 4:5 for carousels/posts and stories keep 9:16 with the reel.</summary>
    public double FrameAspectRatio => Slides.Any(s => s.IsVideo) && !HasMultipleSlides ? 9d / 16d : 4d / 5d;

    public static PublishPreviewModel From(PublishQueueItem item, IReadOnlyList<QueueMediaFile> mediaFiles)
    {
        ArgumentNullException.ThrowIfNull(item);
        var files = mediaFiles ?? Array.Empty<QueueMediaFile>();
        var slides = new List<PublishPreviewSlide>(files.Count);
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var isVideo = string.Equals(file.Type, "video", StringComparison.OrdinalIgnoreCase);
            string? image = isVideo ? file.PreviewLocalPath : file.LocalPath;
            string? video = isVideo ? file.LocalPath : null;
            var placeholder = isVideo
                ? $"پیش‌نمایش ریل روی این رایانه ذخیره نشده است — «بازخوانی فهرست» را بزنید."
                : $"تصویر اسلاید {PublishPreviewText.PersianDigits(i + 1)} روی این رایانه ذخیره نشده است — «بازخوانی فهرست» را بزنید.";
            slides.Add(new PublishPreviewSlide(i, files.Count, isVideo ? "video" : "image", image, video, placeholder,
                file.Transcript, file.TranscriptLanguage));
        }
        return new PublishPreviewModel(item, slides, BuildFields(item));
    }

    public static IReadOnlyList<PublishPreviewField> BuildFields(PublishQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new[]
        {
            new PublishPreviewField("زمان انتشار", PublishPreviewText.PublishTime(item.PublishAt)),
            new PublishPreviewField("نوع محتوا", PublishPreviewText.ContentType(item.ContentType)),
            new PublishPreviewField("قالب رسانه", PublishPreviewText.MediaFormat(item.MediaFormat)),
            new PublishPreviewField("موضوع", PublishPreviewText.Topic(item.Topic)),
            new PublishPreviewField("زبان", PublishPreviewText.Language(item.Language)),
            new PublishPreviewField("نوبت و چرخه", PublishPreviewText.Cycle(item.TopicCycle, item.ContentSlot))
        };
    }
}

/// <summary>
/// The single place that renders queue values as human Persian. Every method returns «—» instead of an empty string,
/// because an empty header field was one of the owner's four complaints (2026-10-03).
/// </summary>
public static class PublishPreviewText
{
    public static string OrFallback(string? value) =>
        string.IsNullOrWhiteSpace(value) ? PublishPreviewModel.EmptyValue : value!.Trim();

    public static string PersianDigits(long number) => PersianDigits(number.ToString(CultureInfo.InvariantCulture));

    public static string PersianDigits(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
            builder.Append(ch switch
            {
                '0' => '۰', '1' => '۱', '2' => '۲', '3' => '۳', '4' => '۴',
                '5' => '۵', '6' => '۶', '7' => '۷', '8' => '۸', '9' => '۹',
                _ => ch
            });
        return builder.ToString();
    }

    public static string PublishTime(DateTimeOffset publishAt)
    {
        if (publishAt == default) return PublishPreviewModel.EmptyValue;
        string date;
        try
        {
            var offset = TimeZoneInfo.Local.GetUtcOffset(publishAt);
            var localTime = publishAt.ToOffset(offset).DateTime;
            date = $"{PersianDigits(localTime.Year)}/{PersianDigits(localTime.Month.ToString("00"))}/{PersianDigits(localTime.Day.ToString("00"))} — {PersianDigits(localTime.Hour.ToString("00"))}:{PersianDigits(localTime.Minute.ToString("00"))}";
        }
        catch (ArgumentException)
        {
            date = PersianDigits(publishAt.ToLocalTime().ToString("yyyy/MM/dd — HH:mm", CultureInfo.InvariantCulture));
        }
        return date;
    }

    public static string ContentType(string? contentType) => contentType switch
    {
        "reels" => "ریلز",
        "carousel" => "کاروسل",
        "story" => "استوری",
        "DM" => "دایرکت تعاملی",
        "Affiliate" => "همکاری در فروش — فقط ریل",
        "post" => "پست",
        _ => OrFallback(contentType)
    };

    public static string MediaFormat(string? mediaFormat) => mediaFormat switch
    {
        "reel" => "ریل",
        "carousel" => "کاروسل",
        "story" => "استوری",
        "post" => "پست",
        _ => OrFallback(mediaFormat)
    };

    public static string Topic(string? topic) => topic switch
    {
        "daily-reels" => "ریلز روزانهٔ برند",
        "daily-didactic" => "آموزش و ترفند",
        "daily-game" => "فان گیمینگ",
        "gaming-news" => "خبر گیم",
        "active-tournaments" => "مسابقات فعال",
        "bazino-safe" => "خانواده و امنیت BAZINO SAFE",
        "affiliate-reel" => "همکاری در فروش (Affiliate Reel)",
        "story-game-interaction" => "تعامل بازی در Story",
        "story-news-reshare" => "بازنشر خبر در Story",
        "story-club-link" => "حال‌وهوای باشگاه و لینک در Story",
        "story-night-interaction" => "پرسش‌وپاسخ یا شمارش معکوس در Story",
        _ => OrFallback(topic)
    };

    public static string Language(string? language) => language switch
    {
        "fa" => "فارسی",
        "tr" => "ترکی",
        "en" => "انگلیسی",
        _ => OrFallback(language)
    };

    public static string Cycle(string? topicCycle, string? contentSlot)
    {
        var parts = new[] { topicCycle, contentSlot }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .ToArray();
        return parts.Length == 0 ? PublishPreviewModel.EmptyValue : string.Join(" · ", parts);
    }
}
