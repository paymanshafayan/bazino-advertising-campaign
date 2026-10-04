using BazinoMarketing.Core.Publishing;
using Xunit;

namespace BazinoMarketing.Core.Tests;

public class PublishQueueTests
{
    [Fact]
    public void QueueItem_RejectsMediaOutsideTheDedicatedQueueFolder()
    {
        var item = MakeItem();
        item.Media[0].Path = "agent-media-inbox/reused.mp4";
        Assert.Contains("Ù¾ÙˆØ´Ù‡Ù” Ø§Ù…Ù† ØµÙ Ø§Ù†ØªØ´Ø§Ø±", item.Validate());
    }

    [Fact]
    public void QueueItem_RejectsNestedOrUnsafePreviewPaths()
    {
        var item = MakeItem();
        item.Media[0].PreviewPath = "marketing-app-mailbox/publish-queue/media/../preview.jpg";
        Assert.Contains("Ù¾ÛŒØ´â€ŒÙ†Ù…Ø§ÛŒØ´", item.Validate());
    }

    [Fact]
    public void InteractiveAutomation_UsesPerPostFollowVerificationAndOwnerLanguageCopy()
    {
        var item = MakeItem();
        item.ContentType = "DM";
        item.Language = "tr";
        item.Engagement = new EngagementAutomation
        {
            Kind = "interactive",
            Language = "tr",
            Keywords = new List<string> { "HazÄ±r" },
            DmMessage = "Ä°stediÄŸin bilgiler burada.",
            FollowGateMessage = "LÃ¼tfen Ã¶nce bizi takip et, sonra aÅŸaÄŸÄ±daki dÃ¼ÄŸmeye dokun.",
            FollowButtonLabel = "Takip ettim",
            NotFollowingMessage = "Takip gÃ¶rÃ¼nmÃ¼yor. LÃ¼tfen takip et ve yeniden dokun."
        };

        var body = ZernioAutomationBuilder.Build(item, "ig-account", "profile", "17840000000000000");
        Assert.Null(body["postId"]);
        Assert.Equal("17840000000000000", body["platformPostId"]!.GetValue<string>());
        Assert.Equal("follower", body["audience"]!["followerStatus"]!.GetValue<string>());
        Assert.Equal("verify", body["audience"]!["whenUnknown"]!.GetValue<string>());
        Assert.Equal("LÃ¼tfen Ã¶nce bizi takip et, sonra aÅŸaÄŸÄ±daki dÃ¼ÄŸmeye dokun.", body["followGate"]!["message"]!.GetValue<string>());
        Assert.Equal("Takip ettim", body["followGate"]!["buttonLabel"]!.GetValue<string>());
        Assert.Equal("Ä°stediÄŸin bilgiler burada.", body["dmMessage"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("post")]
    [InlineData("carousel")]
    [InlineData("story")]
    public void AffiliateContent_RejectsEveryFormatExceptReel(string format)
    {
        var item = MakeItem();
        item.ContentType = "Affiliate";
        item.Topic = "affiliate-reel";
        item.Kind = format;
        item.Media[0].Type = format == "post" || format == "story" ? "image" : "video";
        if (format == "carousel") item.Media.Add(new PublishMediaItem { Path = "marketing-app-mailbox/publish-queue/media/second.jpg", Type = "image" });
        Assert.Contains("ÙÙ‚Ø· Ø¨Ø§ mediaFormat=reel", item.Validate());
    }

    [Fact]
    public void AffiliateContent_AcceptsOnlyExplicitReelAndNeverUsesZernioAutomation()
    {
        var item = MakeItem();
        item.ContentType = "Affiliate";
        item.Topic = "affiliate-reel";
        item.AffiliateDisclosure = "Ù‡Ù…Ú©Ø§Ø±ÛŒ Ø¯Ø± ÙØ±ÙˆØ´Ø› Ø´Ø±Ø§ÛŒØ· Ø¯Ø± ØµÙØ­Ù‡Ù” Ø±Ø³Ù…ÛŒ Ø¯Ø±Ø¬ Ø´Ø¯Ù‡ Ø§Ø³Øª.";
        Assert.Contains("Ø¨Ø§Ø²Ù†Ø´Ø± Ø¢Ù† Ø¨Ù‡ Ù…Ù‚ØµØ¯Ù‡Ø§ÛŒ Ø¯ÛŒÚ¯Ø± Ø¨Ø§ÛŒØ¯ Ø®Ø§Ù…ÙˆØ´ Ø¨Ø§Ø´Ø¯", item.Validate());
        item.RepublishToConnectedPlatforms = false;
        Assert.Equal("", item.Validate());
        item.Engagement = new EngagementAutomation { Kind = "interactive" };
        Assert.Contains("Affiliate Ø¨Ù‡ Ù¾ÙˆØ±ØªØ§Ù„ Ù…ÛŒâ€ŒØ±ÙˆØ¯", item.Validate());
    }

    [Fact]
    public void StoryPublish_OmitsEveryLocationFieldAndDoesNotRequireManusBinding()
    {
        var item = MakeItem();
        item.ContentType = "story";
        item.Topic = "story-game-interaction";
        item.Kind = "story";
        item.Caption = "";
        item.RepublishToConnectedPlatforms = false;
        item.Media[0].Type = "image";
        item.Media[0].Path = "marketing-app-mailbox/publish-queue/media/sample.jpg";
        item.Media[0].PreviewPath = null;
        Assert.Equal("", item.Validate());
        var body = ZernioPublishBuilder.BuildInstagramPost(item, "ig-account", new[] { "https://media.zernio.com/sample.jpg" });
        Assert.Null(body["platforms"]![0]!["locationId"]);
        Assert.Equal("story", body["platforms"]![0]!["platformSpecificData"]?["contentType"]?.GetValue<string>());
        Assert.Null(body["platforms"]![0]!["platformSpecificData"]?["locationId"]);
        Assert.False(body.ContainsKey("content"));
    }

    [Fact]
    public void ManusLocationGuard_RequiresExactActivePageOnTheInstagramProfile()
    {
        const string profileId = "profile-01";
        const string pageId = "1091945074011846";
        Assert.True(ManusLocationGuard.IsBound(pageId, profileId,
            new[] { new FacebookPageSelection(profileId, true, pageId) }));
        Assert.False(ManusLocationGuard.IsBound(pageId, profileId,
            new[] { new FacebookPageSelection(profileId, false, pageId) }));
        Assert.False(ManusLocationGuard.IsBound(pageId, profileId,
            new[] { new FacebookPageSelection("another-profile", true, pageId) }));
        Assert.False(ManusLocationGuard.IsBound(pageId, profileId,
            new[] { new FacebookPageSelection(profileId, true, "6aba79525ad41c33d8cdeba7") }));
    }

    [Fact]
    public void PublishIdempotencyKey_IsStableScopedAndUuidFormatted()
    {
        var first = PublishIdempotencyKey.For("post-20261002-01", "instagram");
        Assert.Equal(first, PublishIdempotencyKey.For("post-20261002-01", "instagram"));
        Assert.NotEqual(first, PublishIdempotencyKey.For("post-20261002-01", "other-platforms"));
        Assert.True(Guid.TryParse(first, out _));
    }

    [Fact]
    public void ApprovalReceipt_IsBoundToExactQueueJsonAndEveryMediaBlob()
    {
        var item = MakeItem();
        item.Media[0].PreviewPath = "marketing-app-mailbox/publish-queue/media/preview.jpg";
        var readySha = new string('a', 40);
        var videoSha = new string('b', 40);
        var previewSha = new string('c', 40);
        var approval = new PublishActionRecord(item.Id, "approved", DateTimeOffset.UtcNow,
            ReadyFileSha: readySha,
            Media: new[]
            {
                new ApprovedQueueMedia(item.Media[0].Path, videoSha),
                new ApprovedQueueMedia(item.Media[0].PreviewPath!, previewSha)
            });
        var media = new Dictionary<string, string>
        {
            [item.Media[0].Path] = videoSha,
            [item.Media[0].PreviewPath!] = previewSha
        };

        Assert.Equal("", PublishApprovalGuard.Validate(item, item.Id, approval, readySha, media));
        Assert.Contains("ÙØ§ÛŒÙ„ Ù…Ø­ØªÙˆØ§", PublishApprovalGuard.Validate(item, item.Id, approval, new string('d', 40), media));
        media[item.Media[0].Path] = new string('e', 40);
        Assert.Contains("Ø±Ø³Ø§Ù†Ù‡", PublishApprovalGuard.Validate(item, item.Id, approval, readySha, media));
    }

    [Fact]
    public void ApprovalReceipt_RejectsMissingPreviewHash()
    {
        var item = MakeItem();
        item.Media[0].PreviewPath = "marketing-app-mailbox/publish-queue/media/preview.jpg";
        var approval = new PublishActionRecord(item.Id, "approved", DateTimeOffset.UtcNow,
            ReadyFileSha: new string('a', 40),
            Media: new[] { new ApprovedQueueMedia(item.Media[0].Path, new string('b', 40)) });

        Assert.Contains("Ø±Ø³Ø§Ù†Ù‡â€ŒÙ‡Ø§ÛŒ Ø¯ÛŒØ¯Ù‡â€ŒØ´Ø¯Ù‡", PublishApprovalGuard.Validate(item, item.Id, approval,
            new string('a', 40), new Dictionary<string, string> { [item.Media[0].Path] = new string('b', 40) }));
    }

    [Fact]
    public void TrialRole_IsRestrictedToReels()
    {
        var item = MakeItem();
        item.TrialGroupId = "group-01";
        item.TrialRole = "trial";
        item.ContentType = "post";
        item.Topic = "gaming-news";
        item.Kind = "post";
        item.Media[0].Type = "image";
        item.Media[0].PreviewPath = null;
        Assert.Contains("ÙÙ‚Ø· Ø¨Ø±Ø§ÛŒ Ø±ÛŒÙ„", item.Validate());
    }

    [Fact]
    public void InstagramPublish_IncludesOfficialFacebookPageLocationId()
    {
        var item = MakeItem();
        var body = ZernioPublishBuilder.BuildInstagramPost(item, ZernioAutomationBuilder.InstagramAccountId,
            new[] { "https://media.zernio.com/one.mp4" });
        Assert.Equal("1091945074011846", body["platforms"]![0]!["platformSpecificData"]!["locationId"]!.GetValue<string>());
        Assert.Equal("true", body["publishNow"]!.ToJsonString());
    }

    [Fact]
    public void VideoWithoutTranscript_IsRejected_BecauseTheOwnerReviewsTheSpokenText()
    {
        var item = MakeItem();
        item.Media[0].Transcript = null;
        Assert.Contains("Ù…ØªÙ† Ù¾ÛŒØ§Ø¯Ù‡â€ŒØ´Ø¯Ù‡Ù” Ú¯ÙØªØ§Ø±", item.Validate());

        item.Media[0].Transcript = "   ";
        Assert.Contains("Ù…ØªÙ† Ù¾ÛŒØ§Ø¯Ù‡â€ŒØ´Ø¯Ù‡Ù” Ú¯ÙØªØ§Ø±", item.Validate());

        item.Media[0].Transcript = new string('x', 12001);
        Assert.Contains("Û±Û²Û°Û°Û°", item.Validate());

        item.Media[0].Transcript = "KonuÅŸma metni";
        Assert.Equal("", item.Validate());
    }

    [Fact]
    public void ImageItems_DoNotNeedATranscript()
    {
        var item = MakeItem();
        item.Kind = "post";
        item.ContentType = "post";
        item.Topic = "gaming-news";
        item.Media[0].Type = "image";
        item.Media[0].PreviewPath = null;
        item.Media[0].Transcript = null;
        Assert.Equal("", item.Validate());
    }

    private static PublishQueueItem MakeItem() => new()
    {
        Id = "post-20261002-01",
        Title = "Ù†Ù…ÙˆÙ†Ù‡Ù” Ù…Ø­ØªÙˆØ§ÛŒ ØªØ¹Ø§Ù…Ù„ÛŒ",
        Kind = "reel",
        ContentType = "reels",
        Topic = "daily-reels",
        GuideVersion = PublishContentCatalog.GuideVersion,
        TopicCycle = "2026-10-02",
        ContentSlot = "daily-reels-01",
        Language = "fa",
        Caption = "Ú©Ù¾Ø´Ù† Ù†Ù…ÙˆÙ†Ù‡",
        PublishAt = DateTimeOffset.Now.AddHours(2),
        TimeZoneId = TimeZoneInfo.Local.Id,
        TargetPlatform = "instagram",
        TargetAccountId = ZernioAutomationBuilder.InstagramAccountId,
        Cta = "Ø¨Ø±Ø§ÛŒ Ø¬Ø²Ø¦ÛŒØ§Øª Ù„ÛŒÙ†Ú© Ø¨ÛŒÙˆ Ø±Ø§ Ø¨Ø¨ÛŒÙ†ÛŒØ¯.",
        ProductionStatus = "final",
        PreviewReviewed = true,
        Media = new List<PublishMediaItem>
        {
            new()
            {
                Path = "marketing-app-mailbox/publish-queue/media/sample.mp4",
                Type = "video",
                PreviewPath = "marketing-app-mailbox/publish-queue/media/sample-preview.jpg",
                Transcript = "Ù†Ù…ÙˆÙ†Ù‡Ù” Ù…ØªÙ† Ú¯ÙØªØ§Ø±",
                TranscriptLanguage = "tr"
            }
        }
    };
}

