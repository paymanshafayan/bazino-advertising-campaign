using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BazinoMarketing.Core.Logging;
using BazinoMarketing.Core.Media;
using BazinoMarketing.Core.Secrets;
using BazinoMarketing.Core.Settings;
using BazinoMarketing.Core.Tools;

namespace BazinoMarketing.Core.Publishing;

/// <summary>
/// Shared branch-backed queue for the Windows review screen and its in-app scheduler.
/// The app must be open for timed publishing; only items approved in the Instagram-like screen are eligible.
/// </summary>
public sealed class PublishQueueService
{
    public const string QueueRoot = "marketing-app-mailbox/publish-queue";
    private const string ReadyDir = QueueRoot + "/ready";
    private const string ApprovedDir = QueueRoot + "/approved";
    private const string FeedbackDir = QueueRoot + "/feedback";
    private const string PublishedDir = QueueRoot + "/published";
    private const string FailedDir = QueueRoot + "/failed";
    private const string AttemptedDir = QueueRoot + "/attempted";
    private const string MediaRoot = QueueRoot + "/media/";
    private const string MediaDir = QueueRoot + "/media";
    /// <summary>How long one run polls Zernio before a publish is reported as unconfirmed instead of failed.</summary>
    private static readonly TimeSpan InstagramConfirmationWindow = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan AttemptConfirmationMinAge = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan AttemptConfirmationMaxAge = TimeSpan.FromHours(6);
    private readonly Func<AppSettings> _settings;
    private readonly ISecretStore _secrets;
    private readonly JsonlLogStore _log;
    private readonly MediaService _media;
    private readonly string _cacheRoot;
    private readonly SemaphoreSlim _schedulerGate = new(1, 1);

    public PublishQueueService(Func<AppSettings> settings, ISecretStore secrets, JsonlLogStore log, MediaService media)
    {
        _settings = settings;
        _secrets = secrets;
        _log = log;
        _media = media;
        _cacheRoot = Path.Combine(media.CurrentFolder, "publish-queue-cache");
    }

    public async Task<IReadOnlyList<PublishQueueCard>> LoadReadyAsync(CancellationToken ct = default)
    {
        var s = GetGitHub();
        var token = GetGitHubToken();
        var dirs = await Task.WhenAll(
            GitHubClient.ListDirectoryAsync(s, token, ReadyDir, ct),
            GitHubClient.ListDirectoryAsync(s, token, ApprovedDir, ct),
            GitHubClient.ListDirectoryAsync(s, token, FeedbackDir, ct),
            GitHubClient.ListDirectoryAsync(s, token, PublishedDir, ct),
            GitHubClient.ListDirectoryAsync(s, token, FailedDir, ct),
            GitHubClient.ListDirectoryAsync(s, token, MediaDir, ct),
            GitHubClient.ListDirectoryAsync(s, token, AttemptedDir, ct)).ConfigureAwait(false);
        var mediaByPath = dirs[5].Where(f => !string.IsNullOrWhiteSpace(f.Path))
            .ToDictionary(f => f.Path, f => f, StringComparer.Ordinal);
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in dirs[1].Concat(dirs[2]).Concat(dirs[3]).Concat(dirs[4]).Concat(dirs[6]))
            done.Add(Path.GetFileNameWithoutExtension(entry.Name));

        var cards = new List<PublishQueueCard>();
        foreach (var file in dirs[0].Where(f => f.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            var id = Path.GetFileNameWithoutExtension(file.Name);
            if (done.Contains(id)) continue;
            var readyBytes = await GitHubClient.DownloadRawAsync(s, token, file.Path, 2 * 1024 * 1024, ct).ConfigureAwait(false);
            if (!string.Equals(GitBlobSha(readyBytes), file.Sha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("فایل صف هنگام بارگذاری تغییر کرد؛ فهرست را تازه کنید و دوباره بررسی کنید.");
            var json = Encoding.UTF8.GetString(readyBytes);
            var item = PublishQueueItem.Parse(json);
            var invalid = item.Validate();
            if (invalid.Length != 0) throw new InvalidDataException($"محتوای «{item.Title}» معتبر نیست: {invalid}");
            cards.Add(new PublishQueueCard(item, file.Path,
                await EnsureMediaAsync(item, mediaByPath, s, token, ct).ConfigureAwait(false), file.Sha));
        }
        return cards.OrderBy(c => c.Item.PublishAt).ToArray();
    }

    public async Task<IReadOnlyList<PublishQueueReportEntry>> LoadRecentResultsAsync(CancellationToken ct = default)
    {
        var s = GetGitHub();
        var token = GetGitHubToken();
        var dirs = await Task.WhenAll(
            GitHubClient.ListDirectoryAsync(s, token, PublishedDir, ct),
            GitHubClient.ListDirectoryAsync(s, token, FailedDir, ct),
            GitHubClient.ListDirectoryAsync(s, token, AttemptedDir, ct)).ConfigureAwait(false);
        var reports = new List<PublishQueueReportEntry>();
        foreach (var file in dirs[0].Where(f => f.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var json = Encoding.UTF8.GetString(await GitHubClient.DownloadRawAsync(s, token, file.Path, 2 * 1024 * 1024, ct).ConfigureAwait(false));
                var record = JsonSerializer.Deserialize<PublishResultRecord>(json, JsonUtil.Options);
                if (record is null) continue;
                var destinations = string.Join("، ", record.Platforms.Select(p => $"{p.Platform}: {p.Status}"));
                if (record.AffiliatePortalSync is { } portal)
                    destinations += (destinations.Length == 0 ? "" : "؛ ") + "ثبت افیلیت در پورتال: " + portal.Status +
                        (string.IsNullOrWhiteSpace(portal.RegistryStatus) ? "" : " (" + portal.RegistryStatus + ")");
                var warning = record.Warnings.Count == 0 ? "" : "هشدار: " + string.Join(" | ", record.Warnings);
                if (record.AffiliatePortalSync is { Success: true, Duplicate: true } duplicate)
                    warning = (warning.Length == 0 ? "" : warning + " | ") + duplicate.Message;
                reports.Add(new PublishQueueReportEntry(record.ItemId, record.PublishedAt, true,
                    destinations.Length == 0 ? "انتشار Instagram تأیید شد" : destinations, record.InstagramUrl, warning));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { /* Ignore malformed history entries; never let old report files block queue review. */ }
        }
        foreach (var file in dirs[1].Where(f => f.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var json = Encoding.UTF8.GetString(await GitHubClient.DownloadRawAsync(s, token, file.Path, 2 * 1024 * 1024, ct).ConfigureAwait(false));
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var id = root.TryGetProperty("itemId", out var idNode) ? idNode.GetString() ?? Path.GetFileNameWithoutExtension(file.Name) : Path.GetFileNameWithoutExtension(file.Name);
                var message = root.TryGetProperty("message", out var msgNode) ? msgNode.GetString() ?? "انتشار ناموفق بود." : "انتشار ناموفق بود.";
                var at = root.TryGetProperty("failedAt", out var timeNode) && timeNode.TryGetDateTimeOffset(out var timestamp) ? timestamp : DateTimeOffset.MinValue;
                reports.Add(new PublishQueueReportEntry(id, at, false, "انتشار انجام نشد", null, message));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { /* Ignore malformed history entries. */ }
        }
        var terminalIds = new HashSet<string>(dirs[0].Concat(dirs[1])
            .Select(f => Path.GetFileNameWithoutExtension(f.Name)), StringComparer.OrdinalIgnoreCase);
        foreach (var file in dirs[2].Where(f => f.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            var id = Path.GetFileNameWithoutExtension(file.Name);
            if (terminalIds.Contains(id)) continue;
            try
            {
                var json = Encoding.UTF8.GetString(await GitHubClient.DownloadRawAsync(s, token, file.Path, 256 * 1024, ct).ConfigureAwait(false));
                var attempt = JsonSerializer.Deserialize<PublishAttemptRecord>(json, JsonUtil.Options);
                var at = attempt?.StartedAt ?? DateTimeOffset.MinValue;
                reports.Add(new PublishQueueReportEntry(attempt?.ItemId ?? id, at, false,
                    "نتیجهٔ ارسال نامشخص است؛ بررسی دستی لازم است",
                    null,
                    "ممکن است درخواست به زرنیو رسیده باشد. برای جلوگیری از انتشار تکراری، این محتوا خودکار دوباره ارسال نمی‌شود؛ ابتدا وضعیت پست در زرنیو/شبکه بررسی شود."));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { /* Keep malformed attempts out of the user-facing report. */ }
        }
        return reports.OrderByDescending(r => r.At).Take(8).ToArray();
    }

    public async Task ApproveAsync(string itemId, CancellationToken ct = default)
    {
        var card = await FindReadyAsync(itemId, ct).ConfigureAwait(false);
        var error = card.Item.Validate();
        if (error.Length != 0) throw new InvalidDataException(error);
        if (card.Item.PublishAt <= DateTimeOffset.UtcNow.AddMinutes(-1))
            throw new InvalidOperationException("زمان انتشار این محتوا گذشته است؛ پیش از تأیید، زمان تازه‌ای در فایل محتوا ثبت شود.");
        if (!IsValidGitSha(card.ReadyFileSha) || card.MediaFiles.Any(m =>
                !IsValidGitSha(m.RepositorySha) ||
                (!string.IsNullOrWhiteSpace(m.PreviewLocalPath) &&
                 (!IsValidGitSha(m.PreviewRepositorySha) || string.IsNullOrWhiteSpace(m.PreviewRepositoryPath)))))
            throw new InvalidDataException("نسخهٔ دقیق فایل/رسانه از GitHub تأیید نشد؛ محتوا قابل تأیید نیست.");
        var approvedMedia = card.MediaFiles
            .SelectMany(m => string.IsNullOrWhiteSpace(m.PreviewRepositoryPath)
                ? new[] { new ApprovedQueueMedia(m.RepositoryPath, m.RepositorySha) }
                : new[] { new ApprovedQueueMedia(m.RepositoryPath, m.RepositorySha), new ApprovedQueueMedia(m.PreviewRepositoryPath, m.PreviewRepositorySha!) })
            .GroupBy(m => m.Path, StringComparer.Ordinal).Select(g => g.First())
            .ToArray();
        await WriteActionAsync(ApprovedDir, new PublishActionRecord(itemId, "approved", DateTimeOffset.UtcNow,
            ReadyFileSha: card.ReadyFileSha, Media: approvedMedia), ct).ConfigureAwait(false);
        _log.Append(LogLevel.Success, "publishing", "approved", "مالک محتوا را برای زمان تعیین‌شده تأیید کرد", $"id={itemId}; at={card.Item.PublishAt:O}");
    }

    public async Task SubmitFeedbackAsync(string itemId, string feedback, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(feedback)) throw new ArgumentException("نظر خالی است.", nameof(feedback));
        if (feedback.Trim().Length > 3000) throw new ArgumentException("نظر حداکثر ۳۰۰۰ نویسه باشد.", nameof(feedback));
        _ = await FindReadyAsync(itemId, ct).ConfigureAwait(false);
        await WriteActionAsync(FeedbackDir, new PublishActionRecord(itemId, "feedback", DateTimeOffset.UtcNow, feedback.Trim()), ct).ConfigureAwait(false);
        _log.Append(LogLevel.Info, "publishing", "feedback", "نظر مالک برای ویرایش محتوا ثبت شد", $"id={itemId}; chars={feedback.Trim().Length}");
    }

    public async Task RunSchedulerAsync(CancellationToken ct)
    {
        // Do not publish outside the running Windows app. Missed items remain visible in the result report instead of
        // silently being handed to Zernio with a past scheduledFor (which Zernio would publish immediately).
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try { await ProcessDueAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        catch (Exception ex) { _log.Append(LogLevel.Warning, "publishing", "scheduler", "بررسی زمان‌بندی انتشار انجام نشد", ex.Message); }
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            try { await ProcessDueAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { _log.Append(LogLevel.Warning, "publishing", "scheduler", "بررسی زمان‌بندی انتشار انجام نشد", ex.Message); }
        }
    }

    public async Task ProcessDueAsync(CancellationToken ct = default)
    {
        if (!await _schedulerGate.WaitAsync(0, ct).ConfigureAwait(false)) return;
        try
        {
            var s = GetGitHub();
            var token = GetGitHubToken();
            var dirs = await Task.WhenAll(
                GitHubClient.ListDirectoryAsync(s, token, ApprovedDir, ct),
                GitHubClient.ListDirectoryAsync(s, token, PublishedDir, ct),
                GitHubClient.ListDirectoryAsync(s, token, FailedDir, ct),
                GitHubClient.ListDirectoryAsync(s, token, AttemptedDir, ct),
                GitHubClient.ListDirectoryAsync(s, token, ReadyDir, ct),
                GitHubClient.ListDirectoryAsync(s, token, MediaDir, ct)).ConfigureAwait(false);
            var approved = dirs[0];
            var published = dirs[1];
            var failed = dirs[2];
            var attempted = dirs[3];
            var readyById = dirs[4].Where(f => f.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(f => Path.GetFileNameWithoutExtension(f.Name), StringComparer.OrdinalIgnoreCase);
            var mediaEntriesByPath = dirs[5].Where(f => !string.IsNullOrWhiteSpace(f.Path))
                .ToDictionary(f => f.Path, f => f, StringComparer.Ordinal);
            var mediaByPath = mediaEntriesByPath.ToDictionary(kv => kv.Key, kv => kv.Value.Sha, StringComparer.Ordinal);
            var done = new HashSet<string>(published.Concat(failed).Concat(attempted)
                .Select(f => Path.GetFileNameWithoutExtension(f.Name)), StringComparer.OrdinalIgnoreCase);
            // Finish attempts whose outcome was still unclear when they ran (owner incident 2026-10-04: Zernio
            // published while the app reported a failure). This never creates a post; it only reads the truth.
            await ResolveUnconfirmedAttemptsAsync(s, token, attempted, published, failed, readyById, ct).ConfigureAwait(false);
            foreach (var entry in approved.Where(f => f.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                ct.ThrowIfCancellationRequested();
                var id = Path.GetFileNameWithoutExtension(entry.Name);
                if (done.Contains(id)) continue;
                PublishQueueItem item;
                try
                {
                    if (!readyById.TryGetValue(id, out var readyEntry))
                        throw new InvalidDataException("فایل محتوای تأییدشده در پوشهٔ ready پیدا نشد.");
                    var readyBytes = await GitHubClient.DownloadRawAsync(s, token, readyEntry.Path, 2 * 1024 * 1024, ct).ConfigureAwait(false);
                    if (!string.Equals(GitBlobSha(readyBytes), readyEntry.Sha, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("فایل ready هنگام خواندن تغییر کرد؛ انتشار متوقف شد.");
                    var readyJson = Encoding.UTF8.GetString(readyBytes);
                    item = PublishQueueItem.Parse(readyJson);
                    var itemError = item.Validate();
                    if (itemError.Length != 0) throw new InvalidDataException(itemError);
                    var approvalBytes = await GitHubClient.DownloadRawAsync(s, token, entry.Path, 256 * 1024, ct).ConfigureAwait(false);
                    if (!string.Equals(GitBlobSha(approvalBytes), entry.Sha, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("رسید تأیید هنگام خواندن تغییر کرد؛ انتشار متوقف شد.");
                    var approvalJson = Encoding.UTF8.GetString(approvalBytes);
                    var approval = JsonSerializer.Deserialize<PublishActionRecord>(approvalJson, JsonUtil.Options)
                        ?? throw new InvalidDataException("رسید تأیید خوانده نشد.");
                    var approvalError = PublishApprovalGuard.Validate(item, id, approval, readyEntry.Sha, mediaByPath);
                    if (approvalError.Length != 0) throw new InvalidDataException(approvalError);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { await RecordFailureAsync(id, "تأیید یا فایل منبع معتبر نیست: " + ex.Message, ct).ConfigureAwait(false); continue; }
                if (item.PublishAt > DateTimeOffset.UtcNow) continue;
                if (item.PublishAt < DateTimeOffset.UtcNow.AddMinutes(-5))
                {
                    await RecordFailureAsync(id, "برنامه هنگام زمان تعیین‌شده باز نبود؛ برای جلوگیری از انتشار دیرهنگام، نیاز به زمان تازه و تأیید دوباره دارد.", ct).ConfigureAwait(false);
                    continue;
                }
                if (item.TrialRole == "main")
                {
                    var trial = await CheckTrialDependencyAsync(item, s, token, ct).ConfigureAwait(false);
                    if (trial == "wait") continue;
                    if (trial.Length != 0) { await RecordFailureAsync(id, trial, ct).ConfigureAwait(false); continue; }
                }
                try
                {
                    var media = await EnsureMediaAsync(item, mediaEntriesByPath, s, token, ct).ConfigureAwait(false);
                    var result = await PublishOneAsync(item, media, readyById[id].Sha, ct).ConfigureAwait(false);
                    await WriteJsonAsync($"{PublishedDir}/{id}.json", result, ct).ConfigureAwait(false);
                    _log.Append(result.Warnings.Count == 0 ? LogLevel.Success : LogLevel.Warning, "publishing", "published",
                        result.Warnings.Count == 0 ? "انتشار اینستاگرام و پردازش مقصدهای انتخاب‌شده پایان یافت" : "انتشار انجام شد اما بخشی از مقصدها/اتوماسیون نیاز به بررسی دارد",
                        $"id={id}; mediaId={result.InstagramMediaId ?? "missing"}; url={result.InstagramUrl ?? "missing"}; platforms={string.Join(",", result.Platforms.Select(p => p.Platform + ":" + p.Status))}; warnings={string.Join(" | ", result.Warnings)}");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (PublishOutcomeUnknownException ex)
                {
                    // Owner law: never report "failed" for something that may have been published. The attempt record
                    // already shows this item as «نتیجهٔ ارسال نامشخص است؛ بررسی دستی لازم است» and a later run finishes it.
                    _log.Append(LogLevel.Warning, "publishing", "publish-unconfirmed",
                        "نتیجهٔ انتشار هنوز نامشخص است؛ به‌جای ثبت «ناموفق»، وضعیت «نیاز به بررسی» می‌ماند", $"id={id}; {ex.Message}");
                }
                catch (Exception ex)
                {
                    await RecordFailureAsync(id, ex.Message, ct).ConfigureAwait(false);
                }
            }
        }
        finally { _schedulerGate.Release(); }
    }

    private async Task<PublishResultRecord> PublishOneAsync(PublishQueueItem item, IReadOnlyList<QueueMediaFile> localMedia, string readyFileSha, CancellationToken ct)
    {
        var settings = _settings();
        string? portalToken = null;
        if (item.ContentType == "Affiliate")
        {
            portalToken = _secrets.GetOrEmpty(SecretKeys.BazinoPortalIngestToken);
            var configurationError = BazinoAffiliatePortalClient.ValidateConfiguration(settings.BazinoPortal, portalToken);
            if (configurationError is not null)
                throw new InvalidOperationException("Affiliate Reel پیش از انتشار متوقف شد: " + configurationError);
        }
        var apiKey = _secrets.GetOrEmpty(SecretKeys.ZernioApiKey);
        var accountReply = await ZernioClient.ListAccountsAsync(settings.Zernio, apiKey, ct).ConfigureAwait(false);
        var healthReply = await ZernioClient.ListAccountHealthAsync(settings.Zernio, apiKey, ct).ConfigureAwait(false);
        if (!accountReply.Ok || !healthReply.Ok) throw new InvalidOperationException(accountReply.Error ?? healthReply.Error ?? "فهرست حساب‌های زرنیو خوانده نشد.");
        var accounts = ParseAccounts(accountReply.Body, healthReply.Body);
        var instagram = accounts.FirstOrDefault(a => a.Platform == "instagram" && a.AccountId == ZernioAutomationBuilder.InstagramAccountId && a.Active && a.CanPost);
        if (instagram is null) throw new InvalidOperationException("حساب Instagram تعیین‌شده فعال یا آمادهٔ انتشار نیست؛ هیچ پستی ارسال نشد.");
        if (item.Kind != "story")
        {
            var manusPageConnected = ManusLocationGuard.IsBound(ZernioAutomationBuilder.FacebookPageId, instagram.ProfileId,
                accounts.Where(a => a.Platform == "facebook")
                    .Select(a => new FacebookPageSelection(a.ProfileId, a.Active, a.SelectedPageId)));
            if (!manusPageConnected)
                throw new InvalidOperationException("Page ID رسمی Manus در اتصال Facebook فعالِ همین profile زرنیو تأیید نشد؛ انتشار متوقف شد.");
        }
        if (item.Kind == "reel")
        {
            var video = localMedia.SingleOrDefault();
            if (video is null || video.Type != "video") throw new InvalidOperationException("ریل باید یک ویدئوی قابل بررسی داشته باشد.");
            var relative = Path.GetRelativePath(_media.CurrentFolder, video.LocalPath).Replace(Path.DirectorySeparatorChar, '/');
            var probe = await _media.ProbeAsync(relative, ct).ConfigureAwait(false);
            if (probe?.Width is null || probe.Height is null || probe.DurationSeconds is null ||
                !IsNineBySixteen(probe.Width.Value, probe.Height.Value) || probe.DurationSeconds > 90)
                throw new InvalidOperationException("ریل برای Instagram تأیید نشد: نسبت ۹:۱۶ و مدت حداکثر ۹۰ ثانیه لازم است؛ برش خودکار انجام نشد.");
        }
        var mediaUrls = new List<string>();
        foreach (var f in localMedia)
            mediaUrls.Add((await ZernioClient.UploadMediaAsync(settings.Zernio, apiKey, f.LocalPath, ct).ConfigureAwait(false)).PublicUrl);

        var igBody = ZernioPublishBuilder.BuildInstagramPost(item, instagram.AccountId, mediaUrls);
        var instagramIdempotencyKey = PublishIdempotencyKey.For(item.Id, "instagram");
        var otherPlatformsIdempotencyKey = PublishIdempotencyKey.For(item.Id, "other-platforms");
        var attemptMedia = localMedia
            .SelectMany(m => string.IsNullOrWhiteSpace(m.PreviewRepositoryPath)
                ? new[] { new ApprovedQueueMedia(m.RepositoryPath, m.RepositorySha) }
                : new[] { new ApprovedQueueMedia(m.RepositoryPath, m.RepositorySha), new ApprovedQueueMedia(m.PreviewRepositoryPath, m.PreviewRepositorySha!) })
            .GroupBy(m => m.Path, StringComparer.Ordinal).Select(g => g.First()).ToArray();
        var attempt = new PublishAttemptRecord(item.Id, DateTimeOffset.UtcNow, readyFileSha, attemptMedia,
            item.RepublishToConnectedPlatforms
                ? new[] { instagramIdempotencyKey, otherPlatformsIdempotencyKey }
                : new[] { instagramIdempotencyKey });
        await WriteJsonAsync($"{AttemptedDir}/{item.Id}.json", attempt, ct).ConfigureAwait(false);
        var confirmed = await PublishInstagramAsync(settings.Zernio, apiKey, item, igBody, instagramIdempotencyKey, ct).ConfigureAwait(false);
        var igPlatform = confirmed.Platform;

        var platforms = new List<PublishPlatformResult> { igPlatform };
        var warnings = new List<string>();
        var instagramMediaId = igPlatform.PlatformPostId;
        var zernioPostId = confirmed.ZernioPostId;
        if (string.IsNullOrWhiteSpace(instagramMediaId)) warnings.Add("پاسخ زرنیو برای پست Instagram، media_id قابل‌اعتماد نداشت؛ ثبت پورتال/اتوماسیون انجام نشد.");
        var publishedAt = igPlatform.PublishedAt ?? DateTimeOffset.UtcNow;
        AffiliatePortalSyncResult? affiliatePortalSync = null;
        if (item.ContentType == "Affiliate")
        {
            try
            {
                affiliatePortalSync = string.IsNullOrWhiteSpace(instagramMediaId)
                    ? AffiliatePortalSyncResult.Blocked("media_id واقعی در پاسخ انتشار نبود؛ ثبت پورتال انجام نشد.")
                    : await BazinoAffiliatePortalClient.ReportPublishedReelAsync(settings.BazinoPortal, portalToken!,
                        instagramMediaId, publishedAt, ct).ConfigureAwait(false);
                if (!affiliatePortalSync.Success) warnings.Add("ثبت Affiliate Reel در پورتال: " + affiliatePortalSync.Message);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                affiliatePortalSync = new AffiliatePortalSyncResult(false, "network-error", 0, null, null, false,
                    "ارسال ثبت پورتال با خطای " + ex.GetType().Name + " متوقف شد؛ وضعیت Instagram تغییر نکرد.");
                warnings.Add("ثبت Affiliate Reel در پورتال: " + affiliatePortalSync.Message);
            }
        }

        if (item.RepublishToConnectedPlatforms && item.ContentType != "Affiliate" && item.Kind != "story")
        {
            var targets = new List<JsonObject>();
            JsonObject? tiktokSettings = null;
            foreach (var account in accounts.Where(a => a.Active && a.CanPost && (a.Platform is "facebook" or "tiktok" or "youtube" or "telegram")))
            {
                if (account.Platform == "tiktok")
                {
                    var isVideo = localMedia.Count == 1 && localMedia[0].Type == "video";
                    var isPhotoSet = localMedia.Count > 0 && localMedia.All(m => m.Type == "image");
                    if (!isVideo && !isPhotoSet)
                    { warnings.Add("TikTok رد شد: قالب رسانه برای انتشار TikTok معتبر نیست."); continue; }
                    var mediaType = isVideo ? "video" : "photo";
                    var creator = await ZernioClient.GetTikTokCreatorInfoAsync(settings.Zernio, apiKey, account.AccountId, mediaType, ct).ConfigureAwait(false);
                    var privacyLevels = creator.Ok ? ReadStringArray(creator.Body, "privacyLevels") : Array.Empty<string>();
                    if (!creator.Ok || !privacyLevels.Contains("PUBLIC_TO_EVERYONE", StringComparer.Ordinal))
                    { warnings.Add("TikTok رد شد: سطح انتشار عمومی در تنظیمات زندهٔ سازنده مجاز/قابل تأیید نبود."); continue; }
                    tiktokSettings ??= isVideo
                        ? new JsonObject
                        {
                            ["privacy_level"] = "PUBLIC_TO_EVERYONE",
                            ["allow_comment"] = true,
                            ["allow_duet"] = false,
                            ["allow_stitch"] = false,
                            ["content_preview_confirmed"] = true,
                            ["express_consent_given"] = true
                        }
                        : new JsonObject
                        {
                            ["privacy_level"] = "PUBLIC_TO_EVERYONE",
                            ["allow_comment"] = true,
                            ["media_type"] = "photo",
                            ["photo_cover_index"] = 0,
                            ["description"] = item.Caption,
                            ["content_preview_confirmed"] = true,
                            ["express_consent_given"] = true
                        };
                }
                if (account.Platform == "youtube")
                {
                    if (localMedia.Count != 1 || localMedia[0].Type != "video")
                    { warnings.Add("YouTube رد شد: محتوای یک ویدئوی عمودی ندارد."); continue; }
                    var relative = Path.GetRelativePath(_media.CurrentFolder, localMedia[0].LocalPath).Replace(Path.DirectorySeparatorChar, '/');
                    var probe = await _media.ProbeAsync(relative, ct).ConfigureAwait(false);
                    if (probe?.Width is null || probe.Height is null || probe.DurationSeconds is null ||
                        !IsNineBySixteen(probe.Width.Value, probe.Height.Value) || probe.DurationSeconds > 180)
                    { warnings.Add("YouTube رد شد: نسبت عمودی ۹:۱۶ و مدت حداکثر سه دقیقه تأیید نشد؛ برش خودکار انجام نشد."); continue; }
                }
                var target = new JsonObject { ["platform"] = account.Platform, ["accountId"] = account.AccountId };
                if (account.Platform == "youtube")
                {
                    target["platformSpecificData"] = new JsonObject
                    {
                        ["title"] = string.IsNullOrWhiteSpace(item.YoutubeTitle) ? item.Title : item.YoutubeTitle,
                        ["visibility"] = "public",
                        ["madeForKids"] = false
                    };
                }
                targets.Add(target);
            }
            if (targets.Count != 0)
            {
                var media = new JsonArray();
                for (var i = 0; i < mediaUrls.Count; i++) media.Add(new JsonObject { ["type"] = item.Media[i].Type, ["url"] = mediaUrls[i] });
                var body = new JsonObject
                {
                    ["title"] = item.Title,
                    ["mediaItems"] = media,
                    ["platforms"] = new JsonArray(targets.Cast<JsonNode?>().ToArray()),
                    ["publishNow"] = true,
                    ["metadata"] = new JsonObject { ["source"] = "bazino-marketing-studio", ["contentId"] = item.Id, ["afterInstagram"] = true, ["engagementKind"] = item.Engagement?.Kind ?? "none", ["language"] = item.Language }
                };
                if (item.Kind != "story") body["content"] = item.Caption;
                if (tiktokSettings is not null) body["tiktokSettings"] = tiktokSettings;
                if (item.Kind != "story" && item.YoutubeTags.Count != 0) body["tags"] = new JsonArray(item.YoutubeTags.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());
                var other = await ZernioClient.CreatePostAsync(settings.Zernio, apiKey, body, otherPlatformsIdempotencyKey, ct).ConfigureAwait(false);
                if (!other.Ok) warnings.Add("انتشار در شبکه‌های دیگر کامل نشد: " + other.Error);
                else
                {
                    foreach (var target in targets)
                    {
                        var outcome = FindPlatformResult(other.Body, target["platform"]!.GetValue<string>());
                        if (outcome is null) warnings.Add("پاسخ نهایی مقصد " + target["platform"]!.GetValue<string>() + " در دسترس نبود.");
                        else platforms.Add(outcome);
                    }
                }
            }
        }

        string? automationId = null;
        if (item.Engagement is not null)
        {
            if (string.IsNullOrWhiteSpace(instagram.ProfileId) || string.IsNullOrWhiteSpace(instagramMediaId))
                warnings.Add("اتوماسیون کامنت فعال نشد: profileId یا media_id واقعی Instagram در پاسخ منتشرشده نبود.");
            else
            {
                if (string.IsNullOrWhiteSpace(zernioPostId)) warnings.Add("شناسهٔ داخلی پست زرنیو برای گزارش در دسترس نبود؛ API اتوماسیون برای پست منتشرشده فقط media_id را می‌خواهد.");
                try
                {
                    var automation = ZernioAutomationBuilder.Build(item, instagram.AccountId, instagram.ProfileId, instagramMediaId);
                    var created = await ZernioClient.CreateCommentAutomationAsync(settings.Zernio, apiKey, automation, ct).ConfigureAwait(false);
                    if (!created.Ok) warnings.Add("ساخت اتوماسیون کامنت ناموفق بود: " + created.Error);
                    else automationId = FindDeepString(created.Body, "id");
                }
                catch (Exception ex) { warnings.Add("ساخت اتوماسیون کامنت متوقف شد: " + ex.Message); }
            }
        }
        return new PublishResultRecord(item.Id, publishedAt, zernioPostId.Length == 0 ? null : zernioPostId,
            instagramMediaId, igPlatform.Url, platforms, warnings, automationId,
            item.Kind == "story" ? null : ZernioAutomationBuilder.InstagramLocationId,
            item.Engagement?.Kind, item.Language, affiliatePortalSync, item.ContentType, item.Topic, item.MediaFormat);
    }

    private sealed record InstagramConfirmation(PublishPlatformResult Platform, string ZernioPostId);

    /// <summary>
    /// Publishes one approved item to Instagram and confirms the terminal state before anything is reported.
    /// Zernio's official contract keeps a platform entry in pending/processing/uploading while Instagram still works,
    /// and a retry of the same Idempotency-Key can only ever return the original post. A non-terminal answer is
    /// therefore never a failure: it is polled (GET /v1/posts/{id}, falling back to the recent list matched by our own
    /// metadata.contentId) and, when still unconfirmed, raised as <see cref="PublishOutcomeUnknownException"/> so the
    /// item shows «نیاز به بررسی» instead of a false «ناموفق» (owner incident 2026-10-04).
    /// </summary>
    private async Task<InstagramConfirmation> PublishInstagramAsync(ZernioSettings settings, string apiKey,
        PublishQueueItem item, JsonObject body, string idempotencyKey, CancellationToken ct)
    {
        JsonObject? post = null;
        try
        {
            var reply = await ZernioClient.CreatePostAsync(settings, apiKey, body, idempotencyKey, ct,
                TimeSpan.FromMinutes(3)).ConfigureAwait(false);
            // Prefer the post whose metadata.contentId is this very queue item; if Zernio returns the post without
            // that metadata, the create call was still ours (unique Idempotency-Key), so its single post is ours too.
            post = ZernioPublishConfirmation.FindPostNode(reply.Body, item.Id)
                ?? ZernioPublishConfirmation.FindPostNode(reply.Body, null);
            if (!reply.Ok)
            {
                // Only a deterministic local refusal (status 0 = guard) or a 4xx that is not a duplicate/conflict
                // proves nothing was created. A timeout, a 5xx or a 409 idempotency conflict may have created the
                // post, so it must be verified before deciding.
                if (reply.StatusCode == 0 || (reply.StatusCode is >= 400 and < 500 && reply.StatusCode != 409))
                    throw new InvalidOperationException("انتشار Instagram ناموفق بود: " + reply.Error);
                post ??= await FindCreatedPostAsync(settings, apiKey, item.Id, ct).ConfigureAwait(false);
                if (post is null)
                    throw new PublishOutcomeUnknownException(
                        "ژینوس برای این درخواست پاسخ قطعی نداد و پستی با شناسهٔ همین محتوا در فهرست اخیر پیدا نشد؛ نتیجهٔ ارسال نامشخص است. علت: " + reply.Error);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (PublishOutcomeUnknownException) { throw; }
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException or IOException)
        {
            post = await FindCreatedPostAsync(settings, apiKey, item.Id, ct).ConfigureAwait(false)
                ?? throw new PublishOutcomeUnknownException(
                    "درخواست انتشار بدون پاسخ کامل پایان یافت و پستی با شناسهٔ همین محتوا در فهرست اخیر پیدا نشد؛ نتیجهٔ ارسال نامشخص است. علت: " + ex.Message, ex);
        }

        var postId = ZernioPublishConfirmation.ReadPostId(post);
        var deadline = DateTimeOffset.UtcNow + InstagramConfirmationWindow;
        while (true)
        {
            var outcome = ZernioPublishConfirmation.ReadPlatform(post, "instagram");
            if (outcome is not null && outcome.State == ZernioPlatformState.Published)
                return new InstagramConfirmation(ZernioPublishConfirmation.ToPlatformResult(outcome), postId);
            if (outcome is not null && outcome.State is ZernioPlatformState.Failed or ZernioPlatformState.Cancelled)
                throw new InvalidOperationException("انتشار Instagram در ژینوس " +
                    (outcome.State == ZernioPlatformState.Cancelled ? "لغو شد: " : "ناموفق ثبت شد: ") + outcome.FailureDetail());
            if (DateTimeOffset.UtcNow >= deadline)
                throw new PublishOutcomeUnknownException(
                    "زرنیو انتشار Instagram را در مهلت بررسی نهایی نکرد" +
                    (outcome is null ? " و پاسخ، وضعیت پست Instagram را نداشت" : " (آخرین وضعیت: " + outcome.Status + ")") +
                    "؛ نتیجهٔ ارسال نامشخص است و بررسی دستی لازم است.");
            await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            var fresh = await ReadZernioPostAsync(settings, apiKey, postId, item.Id, ct).ConfigureAwait(false);
            if (fresh is null) continue;
            post = fresh;
            postId = FirstNonEmpty(ZernioPublishConfirmation.ReadPostId(fresh), postId);
        }
    }

    /// <summary>GET /v1/posts/{id} first, then the recent list matched by the app's own metadata.contentId.</summary>
    private static async Task<JsonObject?> ReadZernioPostAsync(ZernioSettings settings, string apiKey, string? postId, string contentId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(postId))
        {
            try
            {
                var reply = await ZernioClient.GetPostAsync(settings, apiKey, postId, ct).ConfigureAwait(false);
                var node = reply.Ok ? ZernioPublishConfirmation.FindPostNode(reply.Body, null) : null;
                if (node is not null) return node;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { /* A failed read means "not confirmed yet", never "failed to publish". */ }
        }
        return await FindCreatedPostAsync(settings, apiKey, contentId, ct).ConfigureAwait(false);
    }

    private static async Task<JsonObject?> FindCreatedPostAsync(ZernioSettings settings, string apiKey, string contentId, CancellationToken ct)
    {
        try
        {
            var list = await ZernioClient.ListPostsAsync(settings, apiKey, new ZernioPostQuery(null, null, 25, 1, null, null), ct).ConfigureAwait(false);
            return list.Ok ? ZernioPublishConfirmation.FindPostNode(list.Body, contentId) : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    /// <summary>
    /// Later runs finish attempts whose outcome stayed unclear: the post is looked up by the app's own contentId and,
    /// once Zernio reports a terminal state, the normal published/failed record is written. Nothing is ever re-published.
    /// </summary>
    private async Task ResolveUnconfirmedAttemptsAsync(GitHubSettings settings, string token,
        IReadOnlyList<GitHubContentEntry> attempted, IReadOnlyList<GitHubContentEntry> published,
        IReadOnlyList<GitHubContentEntry> failed, IReadOnlyDictionary<string, GitHubContentEntry> readyById, CancellationToken ct)
    {
        var current = _settings();
        var apiKey = _secrets.GetOrEmpty(SecretKeys.ZernioApiKey);
        if (string.IsNullOrWhiteSpace(apiKey)) return;
        var terminal = new HashSet<string>(published.Concat(failed)
            .Select(f => Path.GetFileNameWithoutExtension(f.Name)), StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;
        var checks = 0;
        foreach (var file in attempted.Where(f => f.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            if (checks >= 5) break;
            ct.ThrowIfCancellationRequested();
            var id = Path.GetFileNameWithoutExtension(file.Name);
            if (terminal.Contains(id) || !readyById.TryGetValue(id, out var readyEntry)) continue;
            PublishAttemptRecord? attempt;
            try
            {
                var json = Encoding.UTF8.GetString(await GitHubClient.DownloadRawAsync(settings, token, file.Path, 256 * 1024, ct).ConfigureAwait(false));
                attempt = JsonSerializer.Deserialize<PublishAttemptRecord>(json, JsonUtil.Options);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { continue; }
            if (attempt is null) continue;
            var age = now - attempt.StartedAt;
            if (age < AttemptConfirmationMinAge || age > AttemptConfirmationMaxAge) continue;
            checks++;
            PublishQueueItem item;
            try { item = await ReadItemAsync(readyEntry.Path, readyEntry.Sha, settings, token, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { continue; }
            var post = await FindCreatedPostAsync(current.Zernio, apiKey, item.Id, ct).ConfigureAwait(false);
            if (post is null) continue;
            var outcome = ZernioPublishConfirmation.ReadPlatform(post, "instagram");
            if (outcome is null || outcome.State == ZernioPlatformState.Transient) continue;
            if (outcome.State is ZernioPlatformState.Failed or ZernioPlatformState.Cancelled)
            {
                await RecordFailureAsync(item.Id, "انتشار Instagram در ژینوس " +
                    (outcome.State == ZernioPlatformState.Cancelled ? "لغو شد: " : "ناموفق ثبت شد: ") + outcome.FailureDetail(), ct).ConfigureAwait(false);
                continue;
            }
            var warnings = new List<string>
            {
                "تأیید نهایی این انتشار در بررسی دیرهنگام انجام شد؛ بازنشر به شبکه‌های دیگر و اتوماسیون کامنت در همان اجرا ساخته نشده و در صورت نیاز باید دستی بررسی شود."
            };
            AffiliatePortalSyncResult? affiliatePortalSync = null;
            if (item.ContentType == "Affiliate")
            {
                try
                {
                    var portalToken = _secrets.GetOrEmpty(SecretKeys.BazinoPortalIngestToken);
                    affiliatePortalSync = string.IsNullOrWhiteSpace(outcome.PlatformPostId)
                        ? AffiliatePortalSyncResult.Blocked("media_id واقعی در پاسخ انتشار نبود؛ ثبت پورتال انجام نشد.")
                        : await BazinoAffiliatePortalClient.ReportPublishedReelAsync(current.BazinoPortal, portalToken,
                            outcome.PlatformPostId, outcome.PublishedAt ?? now, ct).ConfigureAwait(false);
                    if (!affiliatePortalSync.Success) warnings.Add("ثبت Affiliate Reel در پورتال: " + affiliatePortalSync.Message);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    affiliatePortalSync = new AffiliatePortalSyncResult(false, "network-error", 0, null, null, false,
                        "ارسال ثبت پورتال با خطای " + ex.GetType().Name + " متوقف شد؛ وضعیت Instagram تغییر نکرد.");
                    warnings.Add("ثبت Affiliate Reel در پورتال: " + affiliatePortalSync.Message);
                }
            }
            var zernioPostId = ZernioPublishConfirmation.ReadPostId(post);
            var record = new PublishResultRecord(item.Id, outcome.PublishedAt ?? now,
                string.IsNullOrWhiteSpace(zernioPostId) ? null : zernioPostId,
                outcome.PlatformPostId, outcome.Url,
                new[] { ZernioPublishConfirmation.ToPlatformResult(outcome) }, warnings, null,
                item.Kind == "story" ? null : ZernioAutomationBuilder.InstagramLocationId,
                item.Engagement?.Kind, item.Language, affiliatePortalSync, item.ContentType, item.Topic, item.MediaFormat);
            await WriteJsonAsync($"{PublishedDir}/{item.Id}.json", record, ct).ConfigureAwait(false);
            _log.Append(LogLevel.Success, "publishing", "published-late", "انتشار اینستاگرام با بررسی دیرهنگام تأیید شد",
                $"id={item.Id}; url={outcome.Url}; mediaId={outcome.PlatformPostId}");
        }
    }

    private static IReadOnlyList<LiveZernioAccount> ParseAccounts(JsonNode? accountBody, JsonNode? healthBody)
    {
        var list = FindArray(accountBody, "accounts");
        var health = FindArray(healthBody, "accounts")
            .Select(a => new { Id = FirstNonEmpty(ReadString(a, "accountId"), ReadString(a, "id"), ReadString(a, "_id")), Node = a })
            .Where(a => a.Id.Length != 0)
            .GroupBy(a => a.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Node, StringComparer.Ordinal);
        return list.OfType<JsonObject>().Select(a =>
        {
            var id = FirstNonEmpty(ReadString(a, "_id"), ReadString(a, "id"), ReadString(a, "accountId"));
            var status = health.GetValueOrDefault(id);
            var profile = a["profileId"];
            var profileId = profile is JsonValue ? profile.GetValue<string>() : ReadString(profile, "_id");
            var active = a["isActive"]?.GetValue<bool>() == true && a["needsReconnection"]?.GetValue<bool>() != true;
            var selectedPageId = ReadString(a["metadata"], "selectedPageId");
            var canPost = false;
            if (status is JsonObject healthStatus)
            {
                var healthState = ReadString(healthStatus, "status");
                var tokenValid = healthStatus["tokenValid"] is JsonValue tokenNode && tokenNode.TryGetValue<bool>(out var tokenFlag)
                    ? tokenFlag
                    : string.Equals(healthState, "healthy", StringComparison.OrdinalIgnoreCase);
                canPost = healthStatus["canPost"]?.GetValue<bool>() == true && tokenValid &&
                    !string.Equals(healthState, "error", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(healthState, "disconnected", StringComparison.OrdinalIgnoreCase) &&
                    healthStatus["needsReconnect"]?.GetValue<bool>() != true && healthStatus["needsReconnection"]?.GetValue<bool>() != true;
            }
            return new LiveZernioAccount(id, ReadString(a, "platform").ToLowerInvariant(), profileId, active, canPost, ReadString(a, "username"), selectedPageId);
        }).Where(a => a.AccountId.Length != 0 && a.Platform.Length != 0).ToArray();
    }

    private static JsonArray FindArray(JsonNode? root, string key)
    {
        if (root is JsonArray array) return array;
        if (root is not JsonObject obj) return new JsonArray();
        if (obj[key] is JsonArray direct) return direct;
        foreach (var value in obj.Select(kv => kv.Value).OfType<JsonObject>())
        {
            var found = FindArray(value, key);
            if (found.Count != 0) return found;
        }
        return new JsonArray();
    }

    private static string[] ReadStringArray(JsonNode? node, string name) => FindArray(node, name)
        .OfType<JsonValue>()
        .Select(value => value.TryGetValue<string>(out var text) ? text ?? "" : "")
        .Where(value => value.Length != 0)
        .ToArray();

    private static string ReadString(JsonNode? node, string name) =>
        node is JsonObject obj && obj[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text ?? "" : "";

    private static string FindDeepString(JsonNode? node, string name)
    {
        if (node is JsonObject obj)
        {
            if (obj[name] is JsonValue value && value.TryGetValue<string>(out var text)) return text ?? "";
            foreach (var child in obj.Select(kv => kv.Value)) { var found = FindDeepString(child, name); if (found.Length != 0) return found; }
        }
        else if (node is JsonArray array)
            foreach (var child in array) { var found = FindDeepString(child, name); if (found.Length != 0) return found; }
        return "";
    }

    private static PublishPlatformResult? FindPlatformResult(JsonNode? body, string platform)
    {
        var list = FindArray(body?["post"], "platforms");
        var node = list.FirstOrDefault(p => string.Equals(ReadString(p, "platform"), platform, StringComparison.OrdinalIgnoreCase));
        if (node is null) return null;
        DateTimeOffset? publishedAt = DateTimeOffset.TryParse(ReadString(node, "publishedAt"), out var parsed)
            ? parsed.ToUniversalTime()
            : null;
        return new PublishPlatformResult(platform, ReadString(node, "status"),
            ReadString(node, "platformPostUrl"), FirstNonEmpty(ReadString(node, "platformPostId"), ReadString(node, "mediaId")), publishedAt);
    }

    /// <returns>Empty when the one-hour delay is valid, "wait" while the trial is not yet posted, otherwise a blocking reason.</returns>
    private async Task<string> CheckTrialDependencyAsync(PublishQueueItem main, GitHubSettings settings, string token, CancellationToken ct)
    {
        var ready = await GitHubClient.ListDirectoryAsync(settings, token, ReadyDir, ct).ConfigureAwait(false);
        PublishQueueItem? trial = null;
        foreach (var file in ready.Where(f => f.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            var candidate = await ReadItemAsync(file.Path, file.Sha, settings, token, ct).ConfigureAwait(false);
            if (candidate.TrialRole == "trial" && string.Equals(candidate.TrialGroupId, main.TrialGroupId, StringComparison.Ordinal))
            { trial = candidate; break; }
        }
        if (trial is null) return "نسخهٔ اصلی متوقف شد: نسخهٔ ترایالِ هم‌گروه پیدا نشد.";
        var failed = await GitHubClient.ListDirectoryAsync(settings, token, FailedDir, ct).ConfigureAwait(false);
        if (failed.Any(f => string.Equals(Path.GetFileNameWithoutExtension(f.Name), trial.Id, StringComparison.OrdinalIgnoreCase)))
            return "نسخهٔ اصلی متوقف شد: انتشار نسخهٔ ترایال ناموفق بود.";
        var published = await GitHubClient.ListDirectoryAsync(settings, token, PublishedDir, ct).ConfigureAwait(false);
        var trialResult = published.FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f.Name), trial.Id, StringComparison.OrdinalIgnoreCase));
        if (trialResult is null) return "wait";
        var json = Encoding.UTF8.GetString(await GitHubClient.DownloadRawAsync(settings, token, trialResult.Path, 2 * 1024 * 1024, ct).ConfigureAwait(false));
        var record = JsonSerializer.Deserialize<PublishResultRecord>(json, JsonUtil.Options);
        if (record is null) return "نسخهٔ اصلی متوقف شد: زمان انتشار ترایال از گزارش آن خوانده نشد.";
        if (main.PublishAt < record.PublishedAt.AddHours(1))
            return "نسخهٔ اصلی متوقف شد: زمان آن باید دست‌کم یک ساعت پس از انتشار موفق ترایال باشد.";
        return "";
    }

    private async Task<IReadOnlyList<QueueMediaFile>> EnsureMediaAsync(PublishQueueItem item,
        IReadOnlyDictionary<string, GitHubContentEntry> mediaEntries, GitHubSettings settings, string token, CancellationToken ct)
    {
        var folder = Path.Combine(_cacheRoot, item.Id);
        Directory.CreateDirectory(folder);
        var files = new List<QueueMediaFile>();
        foreach (var media in item.Media)
        {
            if (!mediaEntries.TryGetValue(media.Path, out var mediaEntry) || !IsValidGitSha(mediaEntry.Sha))
                throw new InvalidDataException($"رسانهٔ {media.Path} در مخزن پیدا نشد یا SHA معتبر ندارد.");
            var local = await CacheOneAsync(media.Path, mediaEntry.Sha, folder, settings, token, ct).ConfigureAwait(false);
            string? preview = null;
            string? previewSha = null;
            if (!string.IsNullOrWhiteSpace(media.PreviewPath))
            {
                if (!mediaEntries.TryGetValue(media.PreviewPath, out var previewEntry) || !IsValidGitSha(previewEntry.Sha))
                    throw new InvalidDataException($"پیش‌نمایش {media.PreviewPath} در مخزن پیدا نشد یا SHA معتبر ندارد.");
                previewSha = previewEntry.Sha;
                preview = await CacheOneAsync(media.PreviewPath, previewSha, folder, settings, token, ct).ConfigureAwait(false);
            }
            files.Add(new QueueMediaFile(media.Path, local, media.Type, preview, mediaEntry.Sha,
                media.PreviewPath, previewSha, media.Transcript, media.TranscriptLanguage));
        }
        return files;
    }

    private async Task<string> CacheOneAsync(string path, string expectedSha, string folder, GitHubSettings settings, string token, CancellationToken ct)
    {
        if (!path.StartsWith(MediaRoot, StringComparison.Ordinal) || !IsValidGitSha(expectedSha))
            throw new InvalidDataException("مسیر یا نسخهٔ SHA رسانه معتبر نیست.");
        var basename = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(basename) || basename.Contains("..", StringComparison.Ordinal)) throw new InvalidDataException("نام فایل رسانه معتبر نیست.");
        var localName = Path.GetFileNameWithoutExtension(basename) + "." + expectedSha[..12] + Path.GetExtension(basename);
        var local = Path.Combine(folder, localName);
        if (File.Exists(local) && string.Equals(await GitBlobShaAsync(local, ct).ConfigureAwait(false), expectedSha, StringComparison.OrdinalIgnoreCase))
            return local;

        if (File.Exists(local)) File.Delete(local);
        var bytes = await GitHubClient.DownloadRawAsync(settings, token, path, 50L * 1024 * 1024, ct).ConfigureAwait(false);
        if (!string.Equals(GitBlobSha(bytes), expectedSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"نسخهٔ دریافت‌شدهٔ رسانهٔ {basename} با SHA مخزن برابر نیست.");
        var temp = local + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, ct).ConfigureAwait(false);
            File.Move(temp, local, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        return local;
    }

    private static bool IsValidGitSha(string? sha) =>
        sha is { Length: 40 } && sha.All(Uri.IsHexDigit);

    private static string GitBlobSha(ReadOnlySpan<byte> content)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(Encoding.ASCII.GetBytes($"blob {content.Length}\0"));
        hash.AppendData(content);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static async Task<string> GitBlobShaAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(Encoding.ASCII.GetBytes($"blob {stream.Length}\0"));
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) != 0)
            hash.AppendData(buffer, 0, read);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private async Task<PublishQueueCard> FindReadyAsync(string id, CancellationToken ct)
    {
        var card = (await LoadReadyAsync(ct).ConfigureAwait(false)).FirstOrDefault(c => string.Equals(c.Item.Id, id, StringComparison.OrdinalIgnoreCase));
        return card ?? throw new InvalidOperationException("این محتوا دیگر در فهرست آماده نیست.");
    }

    private async Task<PublishQueueItem> ReadItemAsync(string path, string expectedSha, GitHubSettings settings, string token, CancellationToken ct)
    {
        var bytes = await GitHubClient.DownloadRawAsync(settings, token, path, 2 * 1024 * 1024, ct).ConfigureAwait(false);
        if (!string.Equals(GitBlobSha(bytes), expectedSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("فایل صف هنگام دریافت تغییر کرد؛ دادهٔ تازه لازم است.");
        var item = PublishQueueItem.Parse(Encoding.UTF8.GetString(bytes));
        var error = item.Validate();
        if (error.Length != 0) throw new InvalidDataException(error);
        return item;
    }

    private async Task WriteActionAsync(string directory, PublishActionRecord record, CancellationToken ct)
    {
        var path = $"{directory}/{record.ItemId}.json";
        await WriteJsonAsync(path, record, ct).ConfigureAwait(false);
    }

    private async Task WriteJsonAsync<T>(string path, T value, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(value, JsonUtil.Options);
        await GitHubClient.UploadContentAsync(GetGitHub(), GetGitHubToken(), path, Encoding.UTF8.GetBytes(json + "\n"), ct).ConfigureAwait(false);
    }

    private async Task RecordFailureAsync(string id, string message, CancellationToken ct)
    {
        var record = new { itemId = id, failedAt = DateTimeOffset.UtcNow, message = message.Length > 2000 ? message[..2000] : message };
        try { await WriteJsonAsync($"{FailedDir}/{id}.json", record, ct).ConfigureAwait(false); }
        catch (Exception ex) { message += "؛ ثبت نتیجه هم انجام نشد: " + ex.Message; }
        _log.Append(LogLevel.Error, "publishing", "failed", "انتشار انجام نشد؛ محتوا دوباره خودکار ارسال نمی‌شود", $"id={id}; {message}");
    }

    private GitHubSettings GetGitHub() => _settings().GitHub;
    private string GetGitHubToken()
    {
        var token = _secrets.GetOrEmpty(SecretKeys.GitHubToken);
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("برای خواندن صف، اتصال GitHub برنامه لازم است.");
        return token;
    }

    private static bool IsNineBySixteen(int width, int height) =>
        width > 0 && height > 0 && Math.Abs((double)width / height - (9d / 16d)) <= 0.04;

    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

    private sealed record LiveZernioAccount(string AccountId, string Platform, string ProfileId, bool Active, bool CanPost, string Username, string SelectedPageId);
}

public sealed record PublishQueueReportEntry(string ItemId, DateTimeOffset At, bool Published, string Summary, string? Url, string Detail);

/// <summary>
/// Publishing may have reached Zernio while the API has not confirmed a terminal state yet. Such an outcome is never
/// recorded as failed; the attempt stays visible as «نتیجهٔ ارسال نامشخص است؛ بررسی دستی لازم است» and is resolved later.
/// </summary>
public sealed class PublishOutcomeUnknownException : Exception
{
    public PublishOutcomeUnknownException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed record PublishAttemptRecord(string ItemId, DateTimeOffset StartedAt, string ReadyFileSha,
    IReadOnlyList<ApprovedQueueMedia> Media, IReadOnlyList<string> IdempotencyKeys);

public sealed record PublishPlatformResult(string Platform, string Status, string Url, string PlatformPostId,
    DateTimeOffset? PublishedAt = null);

public sealed record PublishResultRecord(
    string ItemId,
    DateTimeOffset PublishedAt,
    string? ZernioPostId,
    string? InstagramMediaId,
    string? InstagramUrl,
    IReadOnlyList<PublishPlatformResult> Platforms,
    IReadOnlyList<string> Warnings,
    string? CommentAutomationId,
    string? InstagramLocationId,
    string? EngagementKind,
    string Language,
    AffiliatePortalSyncResult? AffiliatePortalSync = null,
    string? ContentType = null,
    string? Topic = null,
    string? MediaFormat = null);
