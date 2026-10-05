using System.Text.Json.Nodes;
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
        Assert.Contains("پوشهٔ امن صف انتشار", item.Validate());
    }

    [Fact]
    public void QueueItem_RejectsNestedOrUnsafePreviewPaths()
    {
        var item = MakeItem();
        item.Media[0].PreviewPath = "marketing-app-mailbox/publish-queue/media/../preview.jpg";
        Assert.Contains("پیش‌نمایش", item.Validate());
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
            Keywords = new List<string> { "Hazır" },
            DmMessage = "İstediğin bilgiler burada.",
            FollowGateMessage = "Lütfen önce bizi takip et, sonra aşağıdaki düğmeye dokun.",
            FollowButtonLabel = "Takip ettim",
            NotFollowingMessage = "Takip görünmüyor. Lütfen takip et ve yeniden dokun."
        };

        var body = ZernioAutomationBuilder.Build(item, "ig-account", "profile", "17840000000000000");
        Assert.Null(body["postId"]);
        Assert.Equal("17840000000000000", body["platformPostId"]!.GetValue<string>());
        Assert.Equal("follower", body["audience"]!["followerStatus"]!.GetValue<string>());
        Assert.Equal("verify", body["audience"]!["whenUnknown"]!.GetValue<string>());
        Assert.Equal("Lütfen önce bizi takip et, sonra aşağıdaki düğmeye dokun.", body["followGate"]!["message"]!.GetValue<string>());
        Assert.Equal("Takip ettim", body["followGate"]!["buttonLabel"]!.GetValue<string>());
        Assert.Equal("İstediğin bilgiler burada.", body["dmMessage"]!.GetValue<string>());
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
        Assert.Contains("فقط با mediaFormat=reel", item.Validate());
    }

    [Fact]
    public void AffiliateContent_AcceptsOnlyExplicitReelAndNeverUsesZernioAutomation()
    {
        var item = MakeItem();
        item.ContentType = "Affiliate";
        item.Topic = "affiliate-reel";
        item.AffiliateDisclosure = "همکاری در فروش؛ شرایط در صفحهٔ رسمی درج شده است.";
        Assert.Contains("بازنشر آن به مقصدهای دیگر باید خاموش باشد", item.Validate());
        item.RepublishToConnectedPlatforms = false;
        Assert.Equal("", item.Validate());
        item.Engagement = new EngagementAutomation { Kind = "interactive" };
        Assert.Contains("Affiliate به پورتال می‌رود", item.Validate());
    }

    [Fact]
    public void StoryPublish_SendsTheOfficialStoryFlagAndOmitsEveryLocationField()
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
        var platformData = body["platforms"]![0]!["platformSpecificData"]!;
        Assert.Equal("story", platformData["contentType"]!.GetValue<string>());
        Assert.Null(platformData["locationId"]);
        Assert.Null(body["content"]);
    }

    [Theory]
    [InlineData("post")]
    [InlineData("reel")]
    [InlineData("carousel")]
    public void FeedFormats_KeepTheOfficialLocationAndNeverSendTheStoryFlag(string format)
    {
        var item = MakeItem();
        switch (format)
        {
            case "post":
                item.Kind = "post";
                item.ContentType = "post";
                item.Topic = "gaming-news";
                item.Media[0].Type = "image";
                item.Media[0].PreviewPath = null;
                item.Media[0].Transcript = null;
                break;
            case "carousel":
                item.Kind = "carousel";
                item.ContentType = "carousel";
                item.Topic = "gaming-news";
                item.Media[0].Type = "image";
                item.Media[0].PreviewPath = null;
                item.Media[0].Transcript = null;
                item.Media.Add(new PublishMediaItem
                {
                    Path = "marketing-app-mailbox/publish-queue/media/sample-2.jpg",
                    Type = "image"
                });
                break;
        }

        Assert.Equal("", item.Validate());
        var urls = item.Media.Select((_, i) => $"https://media.zernio.com/sample-{i + 1}.jpg").ToArray();
        var body = ZernioPublishBuilder.BuildInstagramPost(item, "ig-account", urls);
        var platformData = body["platforms"]![0]!["platformSpecificData"]!;
        Assert.Null(platformData["contentType"]);
        Assert.Equal(ZernioAutomationBuilder.InstagramLocationId, platformData["locationId"]!.GetValue<string>());
        Assert.Equal(item.Caption, body["content"]!.GetValue<string>());
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
        Assert.Contains("فایل محتوا", PublishApprovalGuard.Validate(item, item.Id, approval, new string('d', 40), media));
        media[item.Media[0].Path] = new string('e', 40);
        Assert.Contains("رسانه", PublishApprovalGuard.Validate(item, item.Id, approval, readySha, media));
    }

    [Fact]
    public void ApprovalReceipt_RejectsMissingPreviewHash()
    {
        var item = MakeItem();
        item.Media[0].PreviewPath = "marketing-app-mailbox/publish-queue/media/preview.jpg";
        var approval = new PublishActionRecord(item.Id, "approved", DateTimeOffset.UtcNow,
            ReadyFileSha: new string('a', 40),
            Media: new[] { new ApprovedQueueMedia(item.Media[0].Path, new string('b', 40)) });

        Assert.Contains("رسانه‌های دیده‌شده", PublishApprovalGuard.Validate(item, item.Id, approval,
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
        Assert.Contains("فقط برای ریل", item.Validate());
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
        Assert.Contains("متن پیاده‌شدهٔ گفتار", item.Validate());

        item.Media[0].Transcript = "   ";
        Assert.Contains("متن پیاده‌شدهٔ گفتار", item.Validate());

        item.Media[0].Transcript = new string('x', 12001);
        Assert.Contains("۱۲۰۰۰", item.Validate());

        item.Media[0].Transcript = "Konuşma metni";
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

    [Fact]
    public void PublishConfirmation_NeverTreatsANonTerminalStatusAsAResult()
    {
        // Official Zernio platform states: pending | processing | uploading | published | failed | cancelled.
        Assert.Equal(ZernioPlatformState.Transient, ZernioPublishConfirmation.Classify("processing"));
        Assert.Equal(ZernioPlatformState.Transient, ZernioPublishConfirmation.Classify("uploading"));
        Assert.Equal(ZernioPlatformState.Transient, ZernioPublishConfirmation.Classify("pending"));
        Assert.Equal(ZernioPlatformState.Published, ZernioPublishConfirmation.Classify("published"));
        Assert.Equal(ZernioPlatformState.Failed, ZernioPublishConfirmation.Classify("failed"));
        Assert.Equal(ZernioPlatformState.Cancelled, ZernioPublishConfirmation.Classify("cancelled"));
        // A value we do not know yet must wait for confirmation instead of turning into a false failure.
        Assert.Equal(ZernioPlatformState.Transient, ZernioPublishConfirmation.Classify("some-new-state"));
        Assert.Equal(ZernioPlatformState.Missing, ZernioPublishConfirmation.Classify(""));
    }

    [Fact]
    public void PublishConfirmation_ReadsAStillPublishingCreateResponseAsUnconfirmed()
    {
        var body = JsonNode.Parse("""
            {"message":"Post created successfully","post":{"_id":"65f1c0a9e2b5af0012ab34cd","status":"publishing",
             "platforms":[{"platform":"instagram","status":"processing"}]}}
            """);
        var outcome = ZernioPublishConfirmation.ReadPlatform(ZernioPublishConfirmation.FindPostNode(body, null), "instagram");
        Assert.NotNull(outcome);
        Assert.Equal(ZernioPlatformState.Transient, outcome!.State);
        Assert.Equal("processing", outcome.Status);
        Assert.Empty(outcome.PlatformPostId);
    }

    [Fact]
    public void PublishConfirmation_ReadsPublishedPlatformWithMediaIdAndUrl()
    {
        var body = JsonNode.Parse("""
            {"post":{"_id":"65f1c0a9e2b5af0012ab34cd","status":"published","publishedAt":"2026-10-04T15:00:39Z",
             "platforms":[{"platform":"instagram","status":"published","platformPostId":"18134197930726215",
              "platformPostUrl":"https://www.instagram.com/p/DGx7Yk2ScAb/"}]}}
            """);
        var post = ZernioPublishConfirmation.FindPostNode(body, null);
        var outcome = ZernioPublishConfirmation.ReadPlatform(post, "instagram");
        Assert.Equal(ZernioPlatformState.Published, outcome!.State);
        var result = ZernioPublishConfirmation.ToPlatformResult(outcome);
        Assert.Equal("instagram", result.Platform);
        Assert.Equal("18134197930726215", result.PlatformPostId);
        Assert.Equal("https://www.instagram.com/p/DGx7Yk2ScAb/", result.Url);
        // The real API puts publishedAt on the post, not on the platform entry.
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 15, 0, 39, TimeSpan.Zero), outcome.PublishedAt);
    }

    [Fact]
    public void PublishConfirmation_TreatsARealMediaIdAsPublishedEvenWithoutAStatusWord()
    {
        var body = JsonNode.Parse("""
            {"post":{"_id":"65f1c0a9e2b5af0012ab34cd","publishedAt":"2026-10-04T15:00:39Z",
             "platforms":[{"platform":"instagram","platformPostId":"18134197930726215"}]}}
            """);
        var outcome = ZernioPublishConfirmation.ReadPlatform(ZernioPublishConfirmation.FindPostNode(body, null), "instagram");
        Assert.Equal(ZernioPlatformState.Published, outcome!.State);
        Assert.Equal("18134197930726215", outcome.PlatformPostId);
    }

    [Fact]
    public void PublishConfirmation_ReadsFailedPlatformWithZernioErrorFields()
    {
        var body = JsonNode.Parse("""
            {"post":{"_id":"65f1c0a9e2b5af0012ab34cd","status":"failed",
             "platforms":[{"platform":"instagram","status":"failed",
              "errorMessage":"Media processing failed: video too short for Reels",
              "errorCategory":"user_content","errorSource":"user"}]}}
            """);
        var outcome = ZernioPublishConfirmation.ReadPlatform(ZernioPublishConfirmation.FindPostNode(body, null), "instagram");
        Assert.Equal(ZernioPlatformState.Failed, outcome!.State);
        Assert.Contains("video too short", outcome.FailureDetail());
        Assert.Contains("user_content", outcome.FailureDetail());
    }

    [Fact]
    public void PublishConfirmation_FindsOnlyOurOwnPostInsideAListResponse()
    {
        var body = JsonNode.Parse("""
            {"posts":[
              {"_id":"someone-else","metadata":{"contentId":"another-item"},"platforms":[{"platform":"instagram","status":"published"}]},
              {"_id":"mine","metadata":{"contentId":"2026-10-04-post-2"},"platforms":[{"platform":"instagram","status":"published","platformPostId":"7"}]}
            ]}
            """);
        var mine = ZernioPublishConfirmation.FindPostNode(body, "2026-10-04-post-2");
        Assert.NotNull(mine);
        Assert.Equal("mine", ZernioPublishConfirmation.ReadPostId(mine));
        Assert.Null(ZernioPublishConfirmation.FindPostNode(body, "no-such-item"));
        Assert.Null(ZernioPublishConfirmation.FindPostNode(JsonNode.Parse("""{"post":{"_id":"x","metadata":{"contentId":"another-item"}}}"""), "our-item"));
        // A create response without metadata is still ours (unique Idempotency-Key), so it is accepted when no
        // contentId filter is asked for — while the recent-list search keeps requiring our own contentId.
        Assert.NotNull(ZernioPublishConfirmation.FindPostNode(JsonNode.Parse("""{"post":{"_id":"x","status":"published"}}"""), null));
    }

    private static PublishQueueItem MakeItem() => new()
    {
        Id = "post-20261002-01",
        Title = "نمونهٔ محتوای تعاملی",
        Kind = "reel",
        ContentType = "reels",
        Topic = "daily-reels",
        GuideVersion = PublishContentCatalog.GuideVersion,
        TopicCycle = "2026-10-02",
        ContentSlot = "daily-reels-01",
        Language = "fa",
        Caption = "کپشن نمونه",
        PublishAt = DateTimeOffset.Now.AddHours(2),
        TimeZoneId = TimeZoneInfo.Local.Id,
        TargetPlatform = "instagram",
        TargetAccountId = ZernioAutomationBuilder.InstagramAccountId,
        Cta = "برای جزئیات لینک بیو را ببینید.",
        ProductionStatus = "final",
        PreviewReviewed = true,
        Media = new List<PublishMediaItem>
        {
            new()
            {
                Path = "marketing-app-mailbox/publish-queue/media/sample.mp4",
                Type = "video",
                PreviewPath = "marketing-app-mailbox/publish-queue/media/sample-preview.jpg",
                Transcript = "نمونهٔ متن گفتار",
                TranscriptLanguage = "tr"
            }
        }
    };
}
