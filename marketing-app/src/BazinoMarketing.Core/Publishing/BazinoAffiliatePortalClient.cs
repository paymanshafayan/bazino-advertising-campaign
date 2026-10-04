using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BazinoMarketing.Core.Http;
using BazinoMarketing.Core.Secrets;
using BazinoMarketing.Core.Settings;

namespace BazinoMarketing.Core.Publishing;

/// <summary>One-way Affiliate Reel registration to the portal's existing ingest endpoint.</summary>
public static class BazinoAffiliatePortalClient
{
    public const string EndpointPath = "/api/integrations/instagram/published-media";
    private static readonly TimeSpan[] RetryDelays = { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5) };

    public static async Task<AffiliatePortalSyncResult> ReportPublishedReelAsync(
        BazinoPortalSettings settings,
        string token,
        string mediaId,
        DateTimeOffset publishedAt,
        CancellationToken ct = default)
    {
        var configurationError = ValidateConfiguration(settings, token);
        if (configurationError is not null) return AffiliatePortalSyncResult.Blocked(configurationError);
        if (!IsMediaId(mediaId)) return AffiliatePortalSyncResult.Blocked("media_id واقعی Instagram معتبر نیست؛ گزارش پورتال ارسال نشد.");
        var baseUri = new Uri(settings.BaseUrl.TrimEnd('/'), UriKind.Absolute);

        using var http = HttpFactory.Create(settings.Proxy, TimeSpan.FromSeconds(25), allowAutoRedirect: false);
        return await ReportPublishedReelAsync(http, baseUri, token.Trim(), mediaId, publishedAt,
            (delay, tokenCt) => Task.Delay(delay, tokenCt), ct).ConfigureAwait(false);
    }

    public static string? ValidateConfiguration(BazinoPortalSettings settings, string? token)
    {
        if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(baseUri.Host, "bazino.pro", StringComparison.OrdinalIgnoreCase) ||
            !baseUri.IsDefaultPort || !string.IsNullOrEmpty(baseUri.UserInfo) ||
            !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment) ||
            baseUri.AbsolutePath != "/")
            return "مقصد ingest باید فقط ریشهٔ HTTPS دامنهٔ رسمی bazino.pro باشد.";
        if (string.IsNullOrWhiteSpace(token) || !SecretFormat.IsHeaderSafe(token.Trim()))
            return "توکن ingest پورتال ثبت نشده یا قالب هدر آن معتبر نیست.";
        return null;
    }

    /// <summary>Testable transport overload; the supplied HttpClient remains owned by the caller.</summary>
    public static async Task<AffiliatePortalSyncResult> ReportPublishedReelAsync(
        HttpClient http,
        Uri baseUri,
        string token,
        string mediaId,
        DateTimeOffset publishedAt,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(baseUri);
        if (baseUri.Scheme != Uri.UriSchemeHttps) return AffiliatePortalSyncResult.Blocked("آدرس پایهٔ پورتال باید HTTPS باشد.");
        if (string.IsNullOrWhiteSpace(token) || !SecretFormat.IsHeaderSafe(token.Trim()))
            return AffiliatePortalSyncResult.Blocked("توکن ingest پورتال ثبت نشده یا قالب هدر آن معتبر نیست.");
        if (!IsMediaId(mediaId)) return AffiliatePortalSyncResult.Blocked("media_id واقعی Instagram معتبر نیست؛ گزارش پورتال ارسال نشد.");

        var endpoint = new Uri(baseUri.ToString().TrimEnd('/') + EndpointPath, UriKind.Absolute);
        if (!string.Equals(endpoint.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase))
            return AffiliatePortalSyncResult.Blocked("مقصد ingest پورتال معتبر نیست.");
        delay ??= (duration, tokenCt) => Task.Delay(duration, tokenCt);
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.TryAddWithoutValidation("Idempotency-Key", "instagram:" + mediaId);
                request.Content = new StringContent(new JsonObject
                {
                    ["media_id"] = mediaId,
                    ["media_type"] = "reel",
                    ["published_at"] = publishedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
                }.ToJsonString(), Encoding.UTF8, "application/json");

                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    try
                    {
                        var root = JsonNode.Parse(body);
                        var accepted = root?["accepted"]?.GetValue<bool>() == true;
                        var duplicate = root?["duplicate"]?.GetValue<bool>() == true;
                        var status = SafeStatus(root?["status"]?.GetValue<string>());
                        if (accepted)
                            return new AffiliatePortalSyncResult(true, duplicate ? "duplicate" : "registered", attempt,
                                (int)response.StatusCode, status, duplicate,
                                duplicate ? "پورتال اعلام کرد این Reel قبلاً ثبت شده است." : "Reel در پورتال ثبت شد.");
                    }
                    catch (JsonException) { }
                    catch (InvalidOperationException) { }
                    // The server may have accepted it despite a malformed body. Never repeat a 200 response.
                    return new AffiliatePortalSyncResult(false, "unconfirmed", attempt, (int)response.StatusCode, null, false,
                        "پورتال پاسخ ۲۰۰ داد اما تأیید ثبت از بدنهٔ پاسخ خوانده نشد؛ بررسی لازم است و تکرار خودکار انجام نمی‌شود.");
                }
                if (response.StatusCode == HttpStatusCode.Conflict)
                    return new AffiliatePortalSyncResult(false, "conflict", attempt, (int)response.StatusCode, null, false,
                        "پورتال تعارض نوع رسانه برای همین media_id گزارش کرد؛ تلاش دوباره انجام نشد.");
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    return new AffiliatePortalSyncResult(false, "unauthorized", attempt, (int)response.StatusCode, null, false,
                        "توکن ingest پورتال پذیرفته نشد؛ پس از ۴۰۱ تلاش دیگری انجام نشد.");

                var errorCode = await ReadSafeErrorCodeAsync(response, ct).ConfigureAwait(false);
                var message = $"پورتال پاسخ HTTP {(int)response.StatusCode} داد" +
                    (errorCode.Length == 0 ? "." : $" ({errorCode}).");
                if (attempt == maxAttempts)
                    return new AffiliatePortalSyncResult(false, "failed", attempt, (int)response.StatusCode, null, false,
                        message + " سقف سه تلاش به پایان رسید.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                if (attempt == maxAttempts)
                    return new AffiliatePortalSyncResult(false, "network-error", attempt, null, null, false,
                        "پورتال پس از سه تلاش شبکه‌ای پاسخ نداد (" + ex.GetType().Name + ").");
            }

            await delay(RetryDelays[attempt - 1], ct).ConfigureAwait(false);
        }
        return new AffiliatePortalSyncResult(false, "failed", maxAttempts, null, null, false, "ثبت پورتال ناموفق بود.");
    }

    private static bool IsMediaId(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 40 && value.All(c => c is >= '0' and <= '9');

    private static string SafeStatus(string? value) => value is "approved" or "needs_review" ? value : "unknown";

    private static async Task<string> ReadSafeErrorCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var root = JsonNode.Parse(body);
            var value = root?["error"]?.GetValue<string>();
            return value is not null && value.Length <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
                ? value
                : "";
        }
        catch { return ""; }
    }
}

public sealed record AffiliatePortalSyncResult(
    bool Success,
    string Status,
    int Attempts,
    int? HttpStatusCode,
    string? RegistryStatus,
    bool Duplicate,
    string Message)
{
    public static AffiliatePortalSyncResult Blocked(string message) =>
        new(false, "blocked", 0, null, null, false, message);
}
