using System.Text.Json;
using System.Text.Json.Nodes;
using BazinoMarketing.Core.Secrets;
using BazinoMarketing.Core.Settings;
using BazinoMarketing.Core.Tools;

namespace BazinoMarketing.Core.Publishing;

/// <summary>Collects read-only Instagram analytics and delivers one date-stamped JSON report to the agent mailbox.</summary>
public sealed class DailyInsightsService
{
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromMinutes(2);
    private readonly Func<AppSettings> _getSettings;
    private readonly ISecretStore _secrets;
    private readonly Action<string, string, string, string>? _log;

    public DailyInsightsService(Func<AppSettings> getSettings, ISecretStore secrets,
        Action<string, string, string, string>? log = null)
    {
        _getSettings = getSettings;
        _secrets = secrets;
        _log = log;
    }

    public async Task<DailyInsightsResult> RunAsync(CancellationToken ct = default)
    {
        using var mutex = new Mutex(false, "Local\\BazinoMarketingDailyInsights");
        var ownsMutex = false;
        try
        {
            try { ownsMutex = mutex.WaitOne(TimeSpan.Zero); }
            catch (AbandonedMutexException) { ownsMutex = true; }
            if (!ownsMutex) return new DailyInsightsResult(false, true, "اجرای گزارش روزانه از قبل در حال انجام است", null);

            var settings = _getSettings();
            var localNow = DateTimeOffset.Now;
            var reportDate = DateOnly.FromDateTime(localNow.DateTime);
            var toDate = reportDate.AddDays(-1);
            var fromDate = reportDate.AddDays(-30);
            var dateText = reportDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            const string mailboxRoot = "marketing-app-mailbox";
            var path = $"{mailboxRoot}/insights/daily/{dateText}.json";
            var token = _secrets.GetOrEmpty(SecretKeys.GitHubToken).Trim();
            if (string.IsNullOrWhiteSpace(token))
                return new DailyInsightsResult(false, false, "گزارش ساخته نشد: کلید GitHub در مخزن امن برنامه ثبت نیست", path);

            try
            {
                var existing = await GitHubClient.ListDirectoryAsync(settings.GitHub, token,
                    $"{mailboxRoot}/insights/daily", ct).ConfigureAwait(false);
                if (existing.Any(f => string.Equals(f.Name, dateText + ".json", StringComparison.OrdinalIgnoreCase)))
                    return new DailyInsightsResult(true, true, "گزارش امروز از قبل در صندوق ایجنت وجود دارد", path);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log?.Invoke("warning", "daily-insights-list", "بررسی گزارش تکراری در GitHub انجام نشد؛ تلاش برای ثبت گزارش ادامه دارد", ex.Message);
            }

            var accountId = ZernioAutomationBuilder.InstagramAccountId;
            var apiKey = _secrets.GetOrEmpty(SecretKeys.ZernioApiKey).Trim();
            var sections = new Dictionary<string, InsightSection>(StringComparer.Ordinal)
            {
                ["accountTotals"] = await FetchAsync(apiKey, "account_insights", requestCt => ZernioClient.GetInstagramAccountInsightsAsync(
                    settings.Zernio, apiKey, accountId, fromDate, toDate,
                    "reach,views,accounts_engaged,total_interactions,comments,likes,saves,shares,replies,reposts,follows_and_unfollows,profile_links_taps",
                    "total_value", requestCt)).ConfigureAwait(false),
                ["dailyReach"] = await FetchAsync(apiKey, "daily_reach", requestCt => ZernioClient.GetInstagramAccountInsightsAsync(
                    settings.Zernio, apiKey, accountId, fromDate, toDate, "reach", "time_series", requestCt)).ConfigureAwait(false),
                ["followers"] = await FetchAsync(apiKey, "follower_history", requestCt => ZernioClient.GetInstagramFollowerHistoryAsync(
                    settings.Zernio, apiKey, accountId, fromDate, toDate, requestCt)).ConfigureAwait(false),
                ["postPerformance"] = await FetchAsync(apiKey, "post_analytics", requestCt => ZernioClient.GetInstagramPostAnalyticsAsync(
                    settings.Zernio, apiKey, accountId, fromDate, toDate, requestCt)).ConfigureAwait(false),
                ["audienceDemographics"] = await FetchAsync(apiKey, "demographics", requestCt => ZernioClient.GetInstagramDemographicsAsync(
                    settings.Zernio, apiKey, accountId, requestCt)).ConfigureAwait(false),
                ["stories"] = await FetchStoriesAsync(settings.Zernio, apiKey, accountId, ct).ConfigureAwait(false)
            };

            var available = sections.Values.Count(s => s.Available);
            var status = available == sections.Count ? "complete" : available > 0 ? "partial" : "unavailable";
            var report = new DailyInsightsReport
            {
                SchemaVersion = 1,
                Status = status,
                ReportDate = dateText,
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                LocalTimeZone = TimeZoneInfo.Local.Id,
                Account = new DailyInsightsAccount(accountId, "instagram", "@bazinopro"),
                Period = new DailyInsightsPeriod(fromDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    toDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), 30),
                DataNotes = new[]
                {
                    "Instagram Insights can be delayed by up to 48 hours; the most recent days may be provisional.",
                    "Zernio Analytics add-on and Instagram permissions are required for some sections. Unavailable data is recorded as an error and is never replaced with zero.",
                    "Post analytics are fetched with page=1..N at 100 rows per page (up to 10,000); if any page fails or the safety ceiling is reached, that section is marked unavailable instead of silently truncated. Compare like-for-like post ages and formats.",
                    "Story details are requested for up to 20 currently active stories. Story insight source (live/cached/unavailable) is preserved; expired stories not returned by the active-story endpoint are not inferred. Meta may report counts below five as zero for privacy."
                },
                Sections = sections
            };

            var bytes = JsonSerializer.SerializeToUtf8Bytes(report, JsonUtil.Options);
            var upload = await GitHubClient.UploadContentAsync(settings.GitHub, token, path, bytes, ct,
                $"insights: daily Instagram report {dateText}").ConfigureAwait(false);
            _log?.Invoke(status == "complete" ? "success" : "warning", "daily-insights-upload",
                status == "complete" ? "گزارش کامل Insights اینستاگرام در صندوق ایجنت ثبت شد" : "گزارش Insights با بخش‌های در دسترس در صندوق ایجنت ثبت شد",
                $"date={dateText}; sections={available}/{sections.Count}; commit={upload.CommitSha}");
            return new DailyInsightsResult(true, false, $"گزارش {status} در صندوق ایجنت ثبت شد ({available}/{sections.Count} بخش در دسترس)", path);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _log?.Invoke("error", "daily-insights", "ساخت یا ارسال گزارش روزانهٔ Insights ناموفق بود", ex.Message);
            return new DailyInsightsResult(false, false, "ساخت یا ارسال گزارش روزانه ناموفق بود: " + ex.Message, null);
        }
        finally
        {
            if (ownsMutex) try { mutex.ReleaseMutex(); } catch { }
        }
    }

    private static async Task<InsightSection> FetchStoriesAsync(ZernioSettings settings, string apiKey, string accountId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return new InsightSection(false, "ZERNIO_API_KEY_NOT_CONFIGURED", null);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ApiTimeout);
            var list = await ZernioClient.GetInstagramActiveStoriesAsync(settings, apiKey, accountId, timeout.Token).ConfigureAwait(false);
            if (!list.Ok) return new InsightSection(false, CleanError(list.Error, "stories"), null);
            var stories = FindStoryArray(list.Body);
            var details = new JsonArray();
            var allInsightsAvailable = true;
            if (stories is not null)
            {
                foreach (var story in stories.Take(20))
                {
                    ct.ThrowIfCancellationRequested();
                    var storyId = ReadStoryString(story, "id", "storyId", "_id");
                    var item = new JsonObject { ["storyId"] = storyId, ["story"] = story?.DeepClone() };
                    if (string.IsNullOrWhiteSpace(storyId)) { item["error"] = "STORY_ID_MISSING"; allInsightsAvailable = false; }
                    else
                    {
                        using var storyTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        storyTimeout.CancelAfter(ApiTimeout);
                        var reply = await ZernioClient.GetInstagramStoryInsightsAsync(settings, apiKey, accountId, storyId, storyTimeout.Token).ConfigureAwait(false);
                        if (reply.Ok)
                        {
                            item["insights"] = reply.Body?.DeepClone();
                            if (string.Equals(ReadStorySource(reply.Body), "unavailable", StringComparison.OrdinalIgnoreCase))
                            { item["warning"] = "STORY_INSIGHTS_UNAVAILABLE"; allInsightsAvailable = false; }
                        }
                        else { item["error"] = CleanError(reply.Error, "story_insights"); allInsightsAvailable = false; }
                    }
                    details.Add(item);
                }
            }
            var data = new JsonObject
            {
                ["activeStories"] = list.Body?.DeepClone(),
                ["storyInsights"] = details,
                ["storyLimit"] = 20
            };
            return new InsightSection(allInsightsAvailable, allInsightsAvailable ? null : "ONE_OR_MORE_STORY_INSIGHTS_UNAVAILABLE", data);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return new InsightSection(false, "REQUEST_TIMEOUT", null); }
        catch (Exception ex) { return new InsightSection(false, CleanError(ex.Message, "stories"), null); }
    }

    private static JsonArray? FindStoryArray(JsonNode? node)
    {
        if (node is JsonArray array) return array;
        if (node is not JsonObject obj) return null;
        foreach (var name in new[] { "stories", "data", "items", "results" })
        {
            if (obj[name] is JsonArray found) return found;
            var nested = FindStoryArray(obj[name]);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static string ReadStoryString(JsonNode? node, params string[] names)
    {
        if (node is not JsonObject obj) return "";
        foreach (var name in names)
            if (obj[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)) return text;
        return "";
    }

    private static string ReadStorySource(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            if (obj["source"] is JsonValue value && value.TryGetValue<string>(out var source)) return source ?? "";
            foreach (var child in obj.Select(kv => kv.Value))
            { var found = ReadStorySource(child); if (found.Length != 0) return found; }
        }
        else if (node is JsonArray array)
            foreach (var child in array) { var found = ReadStorySource(child); if (found.Length != 0) return found; }
        return "";
    }

    private static async Task<InsightSection> FetchAsync(string apiKey, string key,
        Func<CancellationToken, Task<ZernioReply>> fetch)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return new InsightSection(false, "ZERNIO_API_KEY_NOT_CONFIGURED", null);
        try
        {
            using var timeout = new CancellationTokenSource(ApiTimeout);
            var reply = await fetch(timeout.Token).ConfigureAwait(false);
            return reply.Ok
                ? new InsightSection(true, null, reply.Body?.DeepClone())
                : new InsightSection(false, CleanError(reply.Error, key), null);
        }
        catch (OperationCanceledException) { return new InsightSection(false, "REQUEST_TIMEOUT", null); }
        catch (Exception ex) { return new InsightSection(false, CleanError(ex.Message, key), null); }
    }

    private static string CleanError(string? error, string key)
    {
        var value = (error ?? "REQUEST_FAILED").Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (value.Length > 500) value = value[..500];
        return string.IsNullOrWhiteSpace(value) ? key + "_UNAVAILABLE" : value;
    }
}

public sealed class DailyInsightsReport
{
    public int SchemaVersion { get; init; }
    public string Status { get; init; } = "unavailable";
    public string ReportDate { get; init; } = "";
    public DateTimeOffset GeneratedAtUtc { get; init; }
    public string LocalTimeZone { get; init; } = "";
    public DailyInsightsAccount Account { get; init; } = new("", "instagram", "");
    public DailyInsightsPeriod Period { get; init; } = new("", "", 30);
    public IReadOnlyList<string> DataNotes { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, InsightSection> Sections { get; init; } = new Dictionary<string, InsightSection>();
}

public sealed record DailyInsightsAccount(string AccountId, string Platform, string Handle);
public sealed record DailyInsightsPeriod(string FromDate, string ToDate, int Days);
public sealed record InsightSection(bool Available, string? Error, JsonNode? Data);
public sealed record DailyInsightsResult(bool Succeeded, bool Skipped, string Message, string? Path);
