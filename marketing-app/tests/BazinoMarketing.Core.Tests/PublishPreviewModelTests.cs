using BazinoMarketing.Core.Publishing;
using Xunit;

namespace BazinoMarketing.Core.Tests;

/// <summary>
/// Acceptance evidence for PLAN-004 (2026-10-03): the review card must show every slide of a carousel, must never
/// show an empty header field, and must not add any text of its own to the copied content.
/// </summary>
public class PublishPreviewModelTests
{
    [Fact]
    public void Carousel_ExposesEveryMediaFileAsItsOwnSlide()
    {
        var card = MakeCarousel(4);
        var preview = PublishPreviewModel.From(card.Item, card.MediaFiles);

        Assert.Equal(4, preview.Slides.Count);
        Assert.True(preview.HasMultipleSlides);
        Assert.Equal(new[] { 0, 1, 2, 3 }, preview.Slides.Select(s => s.Index));
        Assert.Equal("۱ از ۴", preview.Slides[0].PositionText);
        Assert.Equal("۴ از ۴", preview.Slides[3].PositionText);
        Assert.All(preview.Slides, s => Assert.False(s.IsVideo));
        Assert.Equal(new[] { "a.jpg", "b.jpg", "c.jpg", "d.jpg" }, preview.Slides.Select(s => Path.GetFileName(s.ImagePath)));
    }

    [Fact]
    public void Reel_IsOneVideoSlideWithItsCover()
    {
        var card = MakeCarousel(1, video: true);
        var preview = PublishPreviewModel.From(card.Item, card.MediaFiles);

        Assert.Single(preview.Slides);
        Assert.False(preview.HasMultipleSlides);
        Assert.True(preview.Slides[0].IsVideo);
        Assert.Equal("۱ از ۱", preview.Slides[0].PositionText);
        Assert.Equal(9d / 16d, preview.FrameAspectRatio, 3);
    }

    [Fact]
    public void HeaderFields_AreAlwaysFilled_EvenForAnItemWithoutCycleData()
    {
        var card = MakeCarousel(4);
        card.Item.TopicCycle = "";
        card.Item.ContentSlot = "";
        var preview = PublishPreviewModel.From(card.Item, card.MediaFiles);

        Assert.Equal(new[] { "زمان انتشار", "نوع محتوا", "قالب رسانه", "موضوع", "زبان", "نوبت و چرخه" },
            preview.Fields.Select(f => f.Label));
        Assert.All(preview.Fields, f => Assert.False(string.IsNullOrWhiteSpace(f.Value)));
        Assert.Equal(PublishPreviewModel.EmptyValue, preview.Fields.Single(f => f.Label == "نوبت و چرخه").Value);
        Assert.All(preview.Fields, f => Assert.DoesNotContain("  ", f.Value));
    }

    [Fact]
    public void HeaderFields_FallBackInsteadOfGoingBlank_WhenEveryValueIsMissing()
    {
        var item = new PublishQueueItem { Id = "blank", Title = "" };
        var preview = PublishPreviewModel.From(item, Array.Empty<QueueMediaFile>());

        Assert.All(preview.Fields, f => Assert.Equal(PublishPreviewModel.EmptyValue, f.Value));
        Assert.Equal(PublishPreviewModel.EmptyValue, preview.Title);
        Assert.Empty(preview.Slides);
        Assert.False(preview.HasSlides);
        Assert.False(preview.HasMultipleSlides);
    }

    [Fact]
    public void TheItemTitleIsShownVerbatim_AndNeverReplaced()
    {
        var card = MakeCarousel(2);
        var preview = PublishPreviewModel.From(card.Item, card.MediaFiles);
        Assert.Equal("آزمون نمایشگر کاروسل", preview.Title);
    }

    [Fact]
    public void Preview_AddsNoTextOfItsOwn_ToTheCopiedContent()
    {
        var card = MakeCarousel(4);
        card.Item.ContentType = "DM";
        card.Item.Caption = "کپشن خودِ محتوا";
        card.Item.Cta = "دعوت خودِ محتوا";
        var preview = PublishPreviewModel.From(card.Item, card.MediaFiles);

        var everything = string.Join("\n", preview.Fields.Select(f => f.Label + " " + f.Value))
                         + "\n" + preview.Caption + "\n" + preview.Cta + "\n" + preview.Title;

        Assert.DoesNotContain("اتوماسیون", everything);
        Assert.DoesNotContain("بدون اتوماسیون کامنت", everything);
        Assert.DoesNotContain("دعوت به اقدام", everything);
        Assert.DoesNotContain("محتوای تأییدشده", everything);
        Assert.DoesNotContain("زرنیو", everything);
        Assert.Equal("کپشن خودِ محتوا", preview.Caption);
        Assert.Equal("دعوت خودِ محتوا", preview.Cta);
    }

    [Fact]
    public void SlidePlaceholder_IsASpecificPersianMessage_PerMediaType()
    {
        var card = MakeCarousel(2);
        var preview = PublishPreviewModel.From(card.Item, card.MediaFiles);

        Assert.Contains("اسلاید ۱", preview.Slides[0].PlaceholderText);
        Assert.Contains("بازخوانی فهرست", preview.Slides[0].PlaceholderText);

        var reel = PublishPreviewModel.From(card.Item, new[] { new QueueMediaFile("m.mp4", "", "video", null) });
        Assert.Contains("پیش‌نمایش ریل", reel.Slides[0].PlaceholderText);
    }

    [Fact]
    public void PersianDigits_AreUsedForDatesAndCounters()
    {
        // The comparisons are ordinal on purpose: a culture-sensitive Contains on some runtimes (ICU on the Linux
        // CI runner) treats the Persian digit "۲" as equal to the ASCII "2", which made this test fail even though
        // the produced text contains no ASCII digit at all.
        Assert.Equal("۱۴۰۵", PublishPreviewText.PersianDigits(1405), StringComparer.Ordinal);
        Assert.Equal("۱ از ۴", PublishPreviewText.PersianDigits(1) + " از " + PublishPreviewText.PersianDigits(4), StringComparer.Ordinal);
        Assert.DoesNotContain("2", PublishPreviewText.PersianDigits("2026/10/03 12:35"), StringComparison.Ordinal);
        Assert.DoesNotContain("0", PublishPreviewText.PersianDigits("2026/10/03 12:35"), StringComparison.Ordinal);
        Assert.DoesNotContain("1", PublishPreviewText.PersianDigits("2026/10/03 12:35"), StringComparison.Ordinal);
        Assert.DoesNotContain("9", PublishPreviewText.PersianDigits("1405/09/09 09:09"), StringComparison.Ordinal);
        Assert.All(PublishPreviewText.PersianDigits("0123456789"), c => Assert.InRange(c, '۰', '۹'));

        var time = PublishPreviewText.PublishTime(new DateTimeOffset(2026, 10, 3, 17, 30, 0, TimeSpan.Zero));
        Assert.Contains(":", time, StringComparison.Ordinal);
        Assert.DoesNotContain("2026", time, StringComparison.Ordinal);
        Assert.Equal(PublishPreviewModel.EmptyValue, PublishPreviewText.PublishTime(default));
    }

    [Fact]
    public void Labels_MapQueueCodesToPersianWords()
    {
        Assert.Equal("ریلز", PublishPreviewText.ContentType("reels"));
        Assert.Equal("کاروسل", PublishPreviewText.MediaFormat("carousel"));
        Assert.Equal("فان گیمینگ", PublishPreviewText.Topic("daily-game"));
        Assert.Equal("ترکی", PublishPreviewText.Language("tr"));
        Assert.Equal("ناشناس", PublishPreviewText.ContentType("ناشناس"));
        Assert.Equal(PublishPreviewModel.EmptyValue, PublishPreviewText.Cycle(null, " "));
    }

    [Fact]
    public void Reel_ExposesTheTranscriptOfTheVideo_ForReview()
    {
        var card = MakeCarousel(1, video: true);
        var media = new[] { card.MediaFiles[0] with { Transcript = "Konuşma metni", TranscriptLanguage = "tr" } };
        var preview = PublishPreviewModel.From(card.Item, media);

        Assert.True(preview.Slides[0].HasTranscript);
        Assert.Equal("Konuşma metni", preview.Slides[0].TranscriptText);
        Assert.Contains("ترکی", preview.Slides[0].TranscriptHeader);
    }

    [Fact]
    public void SlidesWithoutTranscript_ExposeNoTranscriptBlock()
    {
        var card = MakeCarousel(3);
        var preview = PublishPreviewModel.From(card.Item, card.MediaFiles);
        Assert.All(preview.Slides, s => Assert.False(s.HasTranscript));
        Assert.All(preview.Slides, s => Assert.Equal("", s.TranscriptHeader));
    }

    private static PublishQueueCard MakeCarousel(int count, bool video = false)
    {
        var item = new PublishQueueItem
        {
            Id = "preview-test",
            Title = "آزمون نمایشگر کاروسل",
            MediaFormat = count == 1 && video ? "reel" : "carousel",
            ContentType = count == 1 && video ? "reels" : "carousel",
            Topic = "gaming-news",
            Language = "fa",
            GuideVersion = PublishContentCatalog.GuideVersion,
            TopicCycle = "2026-10-03",
            ContentSlot = "slot-1",
            Caption = "کپشن",
            Cta = "دعوت",
            PublishAt = new DateTimeOffset(2026, 10, 3, 17, 30, 0, TimeSpan.Zero),
            ProductionStatus = "final",
            PreviewReviewed = true
        };
        var names = new[] { "a.jpg", "b.jpg", "c.jpg", "d.jpg" };
        var media = Enumerable.Range(0, count)
            .Select(i => video && count == 1
                ? new QueueMediaFile("media/a.mp4", "C:/cache/a.mp4", "video", "C:/cache/a-preview.jpg")
                : new QueueMediaFile("media/" + names[i], "C:/cache/" + names[i], "image", null))
            .ToArray();
        return new PublishQueueCard(item, "ready/preview-test.json", media);
    }
}
