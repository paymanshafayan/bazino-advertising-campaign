using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using BazinoMarketing.Core.Http;
using BazinoMarketing.Core.Settings;

namespace BazinoMarketing.Core.Tools;

/// <summary>Zernio client for live account checks, safe post publishing, per-post automations and read-only analytics.</summary>
public static class ZernioClient
{
    public static Task<ToolCheckResult> CheckAsync(ZernioSettings settings, string apiKey, CancellationToken ct = default) =>
        ToolCheckResult.Timed(async () =>
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                return ToolCheckResult.NotConfigured("کلید Zernio ثبت نشده است");
            if (!Secrets.SecretFormat.IsHeaderSafe(apiKey.Trim()))
                return ToolCheckResult.InvalidKey("کلید Zernio", apiKey);
            if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
                return ToolCheckResult.NotConfigured("آدرس پایهٔ Zernio باید https باشد");

            var root = settings.BaseUrl.TrimEnd('/');
            using var http = HttpFactory.Create(settings.Proxy, TimeSpan.FromSeconds(25));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

            using var health = await http.GetAsync($"{root}/v1/accounts/health", ct).ConfigureAwait(false);
            var healthBody = await health.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!health.IsSuccessStatusCode)
                return ToolCheckResult.FromStatus(health.StatusCode, "GET /v1/accounts/health", healthBody);

            int? connected = null;
            var platforms = new List<string>();
            using var accounts = await http.GetAsync($"{root}/v1/accounts?status=connected", ct).ConfigureAwait(false);
            if (accounts.IsSuccessStatusCode)
            {
                try
                {
                    using var doc = JsonDocument.Parse(await accounts.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                    var arr = FindArray(doc.RootElement);
                    if (arr is { } list)
                    {
                        connected = list.GetArrayLength();
                        foreach (var a in list.EnumerateArray())
                        {
                            foreach (var name in new[] { "platform", "provider", "type", "network" })
                                if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty(name, out var pv) && pv.ValueKind == JsonValueKind.String)
                                { platforms.Add(pv.GetString() ?? ""); break; }
                        }
                    }
                }
                catch (JsonException) { }
            }

            var summary = connected is null
                ? "متصل — سرویس سالم است"
                : connected == 0
                    ? "متصل — هیچ حساب اجتماعی وصل نیست"
                    : $"متصل — {connected} حساب اجتماعی وصل است";
            var detail = $"health={(int)health.StatusCode} accounts={(connected?.ToString() ?? "n/a")} platforms={string.Join(",", platforms.Distinct())}";
            return new ToolCheckResult(ToolState.Connected, summary, detail);
        }, "zernio");

    /// <summary>Every account the key can see: id, platform, username, status. Read-only.</summary>
    public static async Task<ZernioReply> ListAccountsAsync(ZernioSettings settings, string apiKey, CancellationToken ct = default)
    {
        var guard = Guard(settings, apiKey);
        if (guard is not null) return guard;
        var http = CreateAuthorized(settings, apiKey);
        try { return await GetJsonAsync(http, $"{settings.BaseUrl.TrimEnd('/')}/v1/accounts", ct).ConfigureAwait(false); }
        finally { http.Dispose(); }
    }

    /// <summary>
    /// Lists posts. <c>source=external</c> returns the posts synced from the platform itself (the account's existing
    /// history); <c>source=zernio</c> (default) returns the posts authored inside Zernio, including drafts.
    /// Read-only: nothing is created, changed or deleted here.
    /// </summary>
    public static async Task<ZernioReply> ListPostsAsync(ZernioSettings settings, string apiKey, ZernioPostQuery query, CancellationToken ct = default)
    {
        var guard = Guard(settings, apiKey);
        if (guard is not null) return guard;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(query.Source)) parts.Add("source=" + Uri.EscapeDataString(query.Source.Trim()));
        if (!string.IsNullOrWhiteSpace(query.Status)) parts.Add("status=" + Uri.EscapeDataString(query.Status.Trim()));
        if (!string.IsNullOrWhiteSpace(query.Search)) parts.Add("search=" + Uri.EscapeDataString(query.Search.Trim()));
        if (!string.IsNullOrWhiteSpace(query.AccountId)) parts.Add("accountId=" + Uri.EscapeDataString(query.AccountId.Trim()));
        parts.Add("limit=" + Math.Clamp(query.Limit, 1, 100));
        parts.Add("page=" + Math.Max(1, query.Page));

        var http = CreateAuthorized(settings, apiKey);
        try
        {
            var url = $"{settings.BaseUrl.TrimEnd('/')}/v1/posts?{string.Join("&", parts)}";
            return await GetJsonAsync(http, url, ct).ConfigureAwait(false);
        }
        finally { http.Dispose(); }
    }

    /// <summary>Read-only Instagram account insights. Only reach supports the time_series metric type.</summary>
    public static Task<ZernioReply> GetInstagramAccountInsightsAsync(
        ZernioSettings settings, string apiKey, string accountId, DateOnly fromDate, DateOnly toDate,
        string metrics, string metricType, CancellationToken ct = default) =>
        GetAnalyticsJsonAsync(settings, apiKey, "/v1/analytics/instagram/account-insights", new Dictionary<string, string>
        {
            ["accountId"] = accountId,
            ["fromDate"] = fromDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["toDate"] = toDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["metrics"] = metrics,
            ["metricType"] = metricType
        }, ct);

    /// <summary>Read-only daily running follower counts and gains/losses; Zernio retains at most 89 days.</summary>
    public static Task<ZernioReply> GetInstagramFollowerHistoryAsync(
        ZernioSettings settings, string apiKey, string accountId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default) =>
        GetAnalyticsJsonAsync(settings, apiKey, "/v1/analytics/instagram/follower-history", new Dictionary<string, string>
        {
            ["accountId"] = accountId,
            ["fromDate"] = fromDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["toDate"] = toDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["metricType"] = "time_series"
        }, ct);

    /// <summary>Read-only Instagram audience demographics. Instagram requires at least 100 followers.</summary>
    public static Task<ZernioReply> GetInstagramDemographicsAsync(
        ZernioSettings settings, string apiKey, string accountId, CancellationToken ct = default) =>
        GetAnalyticsJsonAsync(settings, apiKey, "/v1/analytics/instagram/demographics", new Dictionary<string, string>
        {
            ["accountId"] = accountId,
            ["metric"] = "follower_demographics",
            ["breakdown"] = "age,city,country,gender",
            ["timeframe"] = "this_month"
        }, ct);

    /// <summary>Read-only list of currently active Instagram Stories.</summary>
    public static Task<ZernioReply> GetInstagramActiveStoriesAsync(
        ZernioSettings settings, string apiKey, string accountId, CancellationToken ct = default) =>
        GetAccountPathJsonAsync(settings, apiKey,
            $"/v1/accounts/{Uri.EscapeDataString(accountId)}/instagram/stories", ct);

    /// <summary>Read-only live or cached metrics for one Instagram Story.</summary>
    public static Task<ZernioReply> GetInstagramStoryInsightsAsync(
        ZernioSettings settings, string apiKey, string accountId, string storyId, CancellationToken ct = default) =>
        GetAccountPathJsonAsync(settings, apiKey,
            $"/v1/accounts/{Uri.EscapeDataString(accountId)}/instagram/stories/{Uri.EscapeDataString(storyId)}/insights", ct);

    private static async Task<ZernioReply> GetAccountPathJsonAsync(
        ZernioSettings settings, string apiKey, string path, CancellationToken ct)
    {
        var guard = Guard(settings, apiKey);
        if (guard is not null) return guard;
        if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
            return new ZernioReply(false, "آدرس پایهٔ Zernio باید https باشد", null);
        using var http = CreateAuthorized(settings, apiKey);
        return await GetJsonAsync(http, $"{settings.BaseUrl.TrimEnd('/')}{path}", ct).ConfigureAwait(false);
    }

    /// <summary>Read-only Instagram post analytics, collecting every page (100 rows per page, at most 100 pages).</summary>
    public static async Task<ZernioReply> GetInstagramPostAnalyticsAsync(
        ZernioSettings settings, string apiKey, string accountId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        const int pageSize = 100;
        const int maxPages = 100;
        var query = new Dictionary<string, string>
        {
            ["platform"] = "instagram",
            ["accountId"] = accountId,
            ["fromDate"] = fromDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["toDate"] = toDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["sortBy"] = "engagement",
            ["limit"] = pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        var pageBodies = new List<JsonObject>();
        for (var page = 1; page <= maxPages; page++)
        {
            ct.ThrowIfCancellationRequested();
            query["page"] = page.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var reply = await GetAnalyticsJsonAsync(settings, apiKey, "/v1/analytics", query, ct).ConfigureAwait(false);
            if (!reply.Ok)
                return new ZernioReply(false, $"خواندن صفحهٔ {page} از post analytics ناموفق بود: {reply.Error}",
                    CombineInstagramPostAnalyticsPages(pageBodies, pageSize, complete: false).Body);
            if (reply.Body is not JsonObject body || !TryFindArray(body, "posts", out var posts, out _))
                return new ZernioReply(false, "پاسخ analytics آرایهٔ posts ندارد؛ صفحه‌بندی کامل قابل تأیید نیست.",
                    CombineInstagramPostAnalyticsPages(pageBodies, pageSize, complete: false).Body);
            pageBodies.Add((JsonObject)body.DeepClone());

            var reportedTotalPages = ReadPaginationInt(body, "totalPages", "pageCount");
            var reportedTotal = ReadPaginationInt(body, "total");
            var reportedHasMore = ReadPaginationBool(body, "hasMore", "hasNextPage");
            var hasMore = reportedTotalPages is int totalPages ? page < totalPages
                : reportedTotal is int total ? page * pageSize < total
                : reportedHasMore ?? posts.Count >= pageSize;
            if (!hasMore) break;
            if (page == maxPages)
                return new ZernioReply(false, $"post analytics بیش از سقف امن {maxPages * pageSize} ردیف دارد؛ گزارش کامل ساخته نشد.",
                    CombineInstagramPostAnalyticsPages(pageBodies, pageSize, complete: false).Body);
        }
        return CombineInstagramPostAnalyticsPages(pageBodies, pageSize, complete: true);
    }

    /// <summary>Combines analytics list responses without losing the first page's overview metadata.</summary>
    public static ZernioReply CombineInstagramPostAnalyticsPages(
        IReadOnlyList<JsonObject> pageBodies, int pageSize, bool complete)
    {
        if (pageBodies.Count == 0 || pageSize < 1)
            return new ZernioReply(false, "post analytics پاسخی نداشت یا اندازهٔ صفحه معتبر نبود.", null);
        var combined = (JsonObject)pageBodies[0].DeepClone();
        var collectedPosts = new JsonArray();
        foreach (var body in pageBodies)
        {
            if (!TryFindArray(body, "posts", out var posts, out _))
                return new ZernioReply(false, "یکی از صفحه‌های analytics آرایهٔ posts ندارد.", combined);
            foreach (var post in posts) collectedPosts.Add(post?.DeepClone());
        }
        if (!TryFindArray(combined, "posts", out _, out var parent))
            return new ZernioReply(false, "پاسخ analytics آرایهٔ posts ندارد.", combined);
        parent["posts"] = collectedPosts;
        combined["_bazinoCollection"] = new JsonObject
        {
            ["pagesFetched"] = pageBodies.Count,
            ["postsCollected"] = collectedPosts.Count,
            ["complete"] = complete,
            ["pageSize"] = pageSize
        };
        return new ZernioReply(complete, complete ? null : "صفحه‌بندی post analytics کامل نشد.", combined);
    }

    private static bool TryFindArray(JsonNode? node, string key, out JsonArray array, out JsonObject parent)
    {
        if (node is JsonObject obj)
        {
            if (obj[key] is JsonArray found)
            {
                array = found;
                parent = obj;
                return true;
            }
            foreach (var child in obj.Select(kv => kv.Value))
                if (TryFindArray(child, key, out array, out parent)) return true;
        }
        else if (node is JsonArray list)
        {
            foreach (var child in list)
                if (TryFindArray(child, key, out array, out parent)) return true;
        }
        array = null!;
        parent = null!;
        return false;
    }

    private static int? ReadPaginationInt(JsonObject body, params string[] names)
    {
        var sources = new[] { body["pagination"] as JsonObject, body };
        foreach (var source in sources)
        foreach (var name in names)
            if (source?[name] is JsonValue value && value.TryGetValue<int>(out var number) && number > 0)
                return number;
        return null;
    }

    private static bool? ReadPaginationBool(JsonObject body, params string[] names)
    {
        var sources = new[] { body["pagination"] as JsonObject, body };
        foreach (var source in sources)
        foreach (var name in names)
            if (source?[name] is JsonValue value && value.TryGetValue<bool>(out var flag))
                return flag;
        return null;
    }

    private static async Task<ZernioReply> GetAnalyticsJsonAsync(
        ZernioSettings settings, string apiKey, string path, IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        var guard = Guard(settings, apiKey);
        if (guard is not null) return guard;
        if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
            return new ZernioReply(false, "آدرس پایهٔ Zernio باید https باشد", null);
        if (string.IsNullOrWhiteSpace(query.GetValueOrDefault("accountId")))
            return new ZernioReply(false, "شناسهٔ حساب Instagram تنظیم نشده است", null);
        var queryString = string.Join("&", query.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        using var http = CreateAuthorized(settings, apiKey);
        return await GetJsonAsync(http, $"{settings.BaseUrl.TrimEnd('/')}{path}?{queryString}", ct).ConfigureAwait(false);
    }

    /// <summary>Read-only TikTok creator capabilities used to choose an allowed public privacy level before posting.</summary>
    public static async Task<ZernioReply> GetTikTokCreatorInfoAsync(
        ZernioSettings settings, string apiKey, string accountId, string mediaType, CancellationToken ct = default)
    {
        var guard = Guard(settings, apiKey);
        if (guard is not null) return guard;
        if (string.IsNullOrWhiteSpace(accountId) || mediaType is not ("video" or "photo"))
            return new ZernioReply(false, "شناسهٔ حساب TikTok یا نوع رسانه معتبر نیست.", null);
        var http = CreateAuthorized(settings, apiKey);
        try
        {
            var url = $"{settings.BaseUrl.TrimEnd('/')}/v1/accounts/{Uri.EscapeDataString(accountId)}/tiktok/creator-info?mediaType={Uri.EscapeDataString(mediaType)}";
            return await GetJsonAsync(http, url, ct).ConfigureAwait(false);
        }
        finally { http.Dispose(); }
    }

    /// <summary>Read-only account health used immediately before any publishing write.</summary>
    public static async Task<ZernioReply> ListAccountHealthAsync(ZernioSettings settings, string apiKey, CancellationToken ct = default)
    {
        var guard = Guard(settings, apiKey);
        if (guard is not null) return guard;
        var http = CreateAuthorized(settings, apiKey);
        try { return await GetJsonAsync(http, $"{settings.BaseUrl.TrimEnd('/')}/v1/accounts/health", ct).ConfigureAwait(false); }
        finally { http.Dispose(); }
    }

    /// <summary>Creates a post with a stable idempotency key. The caller must first discover accounts and validate targets.</summary>
    public static async Task<ZernioReply> CreatePostAsync(
        ZernioSettings settings, string apiKey, JsonObject body, string idempotencyKey, CancellationToken ct = default)
    {
        var guard = Guard(settings, apiKey);
        if (guard is not null) return guard;
        if (body["publishNow"]?.GetValue<bool>() != true)
            return new ZernioReply(false, "انتشار خودکار فقط با زمان‌بندی تأییدشده یا publishNow مجاز است.", null);
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 255)
            return new ZernioReply(false, "شناسهٔ جلوگیری از انتشار تکراری معتبر نیست.", null);
        var http = CreateAuthorized(settings, apiKey);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{settings.BaseUrl.TrimEnd('/')}/v1/posts");
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
            request.Content = new StringContent(body.ToJsonString(BazinoMarketing.Core.Settings.JsonUtil.Compact), System.Text.Encoding.UTF8, "application/json");
            return await SendJsonAsync(http, request, ct).ConfigureAwait(false);
        }
        finally { http.Dispose(); }
    }

    /// <summary>Creates a per-post comment automation; account-wide automations are intentionally rejected.</summary>
    public static async Task<ZernioReply> CreateCommentAutomationAsync(
        ZernioSettings settings, string apiKey, JsonObject body, CancellationToken ct = default)
    {
        var guard = Guard(settings, apiKey);
        if (guard is not null) return guard;
        if (string.IsNullOrWhiteSpace(body["accountId"]?.GetValue<string>()) ||
            string.IsNullOrWhiteSpace(body["profileId"]?.GetValue<string>()) ||
            string.IsNullOrWhiteSpace(body["platformPostId"]?.GetValue<string>()))
            return new ZernioReply(false, "اتوماسیون برای پست منتشرشده به accountId، profileId و media_id واقعی Instagram نیاز دارد.", null);
        if (body["keywords"] is not JsonArray keywords || keywords.Count == 0 ||
            keywords.Any(k => string.IsNullOrWhiteSpace(k?.GetValue<string>()) || (k!.GetValue<string>()?.Length ?? 0) > 40))
            return new ZernioReply(false, "اتوماسیون به یک واژهٔ کلیدی کوتاه و غیرخالی نیاز دارد.", null);
        if (string.IsNullOrWhiteSpace(body["dmMessage"]?.GetValue<string>()))
            return new ZernioReply(false, "متن دایرکت باید پیش از فعال‌سازی آماده باشد.", null);
        var http = CreateAuthorized(settings, apiKey);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{settings.BaseUrl.TrimEnd('/')}/v1/comment-automations");
            request.Content = new StringContent(body.ToJsonString(BazinoMarketing.Core.Settings.JsonUtil.Compact), System.Text.Encoding.UTF8, "application/json");
            return await SendJsonAsync(http, request, ct).ConfigureAwait(false);
        }
        finally { http.Dispose(); }
    }

    /// <summary>Uploads one local image/video through Zernio's presigned media route and returns its public URL.</summary>
    public static async Task<ZernioMediaUploadResult> UploadMediaAsync(
        ZernioSettings settings, string apiKey, string filePath, CancellationToken ct = default)
    {
        var guard = Guard(settings, apiKey);
        if (guard is not null) throw new InvalidOperationException(guard.Error);
        var info = new FileInfo(filePath);
        if (!info.Exists || info.Length <= 0 || info.Length > 50L * 1024 * 1024)
            throw new InvalidOperationException("رسانه باید موجود، غیرخالی و حداکثر ۵۰ مگابایت باشد.");
        var contentType = Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".mp4" or ".m4v" => "video/mp4",
            ".mov" => "video/quicktime",
            ".webm" => "video/webm",
            _ => throw new InvalidOperationException("قالب رسانه برای بارگذاری زرنیو پشتیبانی نمی‌شود.")
        };
        var root = settings.BaseUrl.TrimEnd('/');
        using var auth = CreateAuthorized(settings, apiKey);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{root}/v1/media/presign");
        request.Content = new StringContent(JsonSerializer.Serialize(new { filename = info.Name, contentType, size = info.Length }), System.Text.Encoding.UTF8, "application/json");
        using var response = await auth.SendAsync(request, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"درخواست بارگذاری زرنیو رد شد ({(int)response.StatusCode}): {Short(text)}");
        using var doc = JsonDocument.Parse(text);
        var uploadUrl = ReadDeepString(doc.RootElement, "uploadUrl");
        var publicUrl = ReadDeepString(doc.RootElement, "publicUrl");
        if (!Uri.TryCreate(uploadUrl, UriKind.Absolute, out var uploadUri) || uploadUri.Scheme != Uri.UriSchemeHttps ||
            !(uploadUri.Host.Equals("media.zernio.com", StringComparison.OrdinalIgnoreCase) || uploadUri.Host.EndsWith(".r2.cloudflarestorage.com", StringComparison.OrdinalIgnoreCase)) ||
            !Uri.TryCreate(publicUrl, UriKind.Absolute, out var publicUri) || publicUri.Scheme != Uri.UriSchemeHttps ||
            !publicUri.Host.Equals("media.zernio.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("نشانی بارگذاری زرنیو نامعتبر بود؛ رسانه ارسال نشد.");

        var bytes = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);
        using var uploadClient = HttpFactory.Create(settings.Proxy, TimeSpan.FromMinutes(4));
        using var uploadContent = new ByteArrayContent(bytes);
        uploadContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var uploadResponse = await uploadClient.PutAsync(uploadUri, uploadContent, ct).ConfigureAwait(false);
        if (!uploadResponse.IsSuccessStatusCode)
            throw new InvalidOperationException($"رسانه در فضای زرنیو بارگذاری نشد ({(int)uploadResponse.StatusCode}).");
        return new ZernioMediaUploadResult(publicUri.ToString(), info.Length, contentType);
    }

    private static async Task<ZernioReply> SendJsonAsync(HttpClient http, HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return new ZernioReply(false, $"Zernio پاسخ {(int)response.StatusCode} داد: {Short(body)}", null);
        try { return new ZernioReply(true, null, JsonNode.Parse(body)); }
        catch (JsonException ex) { return new ZernioReply(false, "پاسخ Zernio خوانده نشد: " + ex.Message, null); }
    }

    private static string ReadDeepString(JsonElement root, string name)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String) return value.GetString() ?? "";
            foreach (var child in root.EnumerateObject())
                if (child.Value.ValueKind == JsonValueKind.Object)
                { var found = ReadDeepString(child.Value, name); if (found.Length != 0) return found; }
        }
        return "";
    }

    private static ZernioReply? Guard(ZernioSettings settings, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return new ZernioReply(false, "کلید Zernio در برنامه ثبت نشده است. مالک باید آن را در تنظیمات Zernio وارد کند.", null);
        if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
            return new ZernioReply(false, "آدرس پایهٔ Zernio باید https باشد.", null);
        return null;
    }

    private static HttpClient CreateAuthorized(ZernioSettings settings, string apiKey)
    {
        var http = HttpFactory.Create(settings.Proxy, TimeSpan.FromSeconds(30));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        return http;
    }

    private static async Task<ZernioReply> GetJsonAsync(HttpClient http, string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return new ZernioReply(false, $"Zernio پاسخ {(int)response.StatusCode} داد: {Short(body)}", null);
        try { return new ZernioReply(true, null, JsonNode.Parse(body)); }
        catch (JsonException ex) { return new ZernioReply(false, "پاسخ Zernio خوانده نشد: " + ex.Message, null); }
    }

    private static string Short(string text)
    {
        var oneLine = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        return oneLine.Length <= 300 ? oneLine : oneLine[..300] + "…";
    }

    private static JsonElement? FindArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array) return root;
        if (root.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in new[] { "data", "accounts", "items", "results" })
            if (root.TryGetProperty(name, out var el))
            {
                if (el.ValueKind == JsonValueKind.Array) return el;
                if (el.ValueKind == JsonValueKind.Object) { var inner = FindArray(el); if (inner is not null) return inner; }
            }
        return null;
    }
}

/// <summary>Outcome of a read-only Zernio call: either the JSON body or a Persian reason it failed.</summary>
public sealed record ZernioReply(bool Ok, string? Error, JsonNode? Body);
public sealed record ZernioMediaUploadResult(string PublicUrl, long SizeBytes, string ContentType);

/// <summary>Optional filters for GET /v1/posts. Everything empty is simply not sent.</summary>
public sealed record ZernioPostQuery(
    string? Source,
    string? Status,
    int Limit,
    int Page,
    string? Search,
    string? AccountId);
