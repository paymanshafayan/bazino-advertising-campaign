using System.Net.Http.Headers;
using System.Text.Json;
using BazinoMarketing.Core.Http;
using BazinoMarketing.Core.Settings;

namespace BazinoMarketing.Core.Tools;

/// <summary>FLUX image provider. Only read-only checks here; generation (which may cost money) is behind AllowSpend, phase 3.</summary>
public static class FluxClient
{
    public static async Task<ToolCheckResult> CheckAsync(FluxSettings settings, string apiKey, CancellationToken ct = default)
    {
        var r = await CheckCoreAsync(settings, apiKey, ct).ConfigureAwait(false);
        if (r.State == ToolState.NetworkError && string.Equals(settings.Provider, "cloudflare", StringComparison.OrdinalIgnoreCase))
            r = r with { Summary = r.Summary + " — اگر Cloudflare در منطقهٔ شما محدود است، پراکسی همین کارت را تنظیم کنید" };
        return r;
    }

    private static Task<ToolCheckResult> CheckCoreAsync(FluxSettings settings, string apiKey, CancellationToken ct) =>
        ToolCheckResult.Timed(async () =>
        {
            var provider = (settings.Provider ?? "none").Trim().ToLowerInvariant();
            if (provider == "none")
                return ToolCheckResult.NotConfigured("ارائه‌دهندهٔ FLUX انتخاب نشده است (اختیاری)");
            if (string.IsNullOrWhiteSpace(apiKey))
                return ToolCheckResult.NotConfigured("کلید FLUX ثبت نشده است");
            if (!Secrets.SecretFormat.IsHeaderSafe(apiKey.Trim()))
                return ToolCheckResult.InvalidKey("توکن FLUX", apiKey);

            using var http = HttpFactory.Create(settings.Proxy, TimeSpan.FromSeconds(25));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

            if (provider == "cloudflare")
            {
                if (string.IsNullOrWhiteSpace(settings.AccountId))
                    return ToolCheckResult.NotConfigured("شناسهٔ حساب Cloudflare ثبت نشده است");
                var root = string.IsNullOrWhiteSpace(settings.BaseUrl) ? "https://api.cloudflare.com/client/v4" : settings.BaseUrl.TrimEnd('/');
                var url = $"{root}/accounts/{Uri.EscapeDataString(settings.AccountId.Trim())}/ai/models/search?search=flux&per_page=5";
                using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return ToolCheckResult.FromStatus(resp.StatusCode, "GET ai/models/search", body);

                var models = new List<string>();
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Array)
                        foreach (var m in result.EnumerateArray())
                            if (m.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String) models.Add(n.GetString() ?? "");
                }
                catch (JsonException) { }

                var hasModel = models.Any(m => string.Equals(m, settings.Model, StringComparison.OrdinalIgnoreCase));
                var summary = hasModel ? $"متصل — مدل {settings.Model} در دسترس است"
                            : models.Count > 0 ? $"متصل — {models.Count} مدل FLUX یافت شد" : "متصل — Workers AI پاسخ داد";
                return new ToolCheckResult(ToolState.Connected, summary, $"models={string.Join(",", models)}");
            }

            // generic: a read-only GET the owner supplies (e.g. a /models or /health endpoint of a self-hosted or third-party FLUX API).
            var testUrl = string.IsNullOrWhiteSpace(settings.TestUrl) ? settings.BaseUrl : settings.TestUrl;
            if (string.IsNullOrWhiteSpace(settings.TestUrl) && LooksLikeCloudflareApiRoot(testUrl))
                return ToolCheckResult.NotConfigured("آدرس آزمایش این ارائه‌دهنده ثبت نشده — آدرس «/models» یا «/health» ارائه‌دهنده را در تنظیمات FLUX وارد کنید (آدرس پیش‌فرض Cloudflare به‌تنهایی پاسخ نمی‌دهد)");
            if (!Uri.TryCreate(testUrl, UriKind.Absolute, out var testUri) || (testUri.Scheme != Uri.UriSchemeHttps && testUri.Scheme != Uri.UriSchemeHttp))
                return ToolCheckResult.NotConfigured("آدرس آزمایش ارائه‌دهندهٔ عمومی معتبر نیست");
            using (var resp = await http.GetAsync(testUri, ct).ConfigureAwait(false))
            {
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return ToolCheckResult.FromStatus(resp.StatusCode, "GET test url", body);
                return new ToolCheckResult(ToolState.Connected, "متصل — آدرس آزمایش پاسخ داد", $"HTTP {(int)resp.StatusCode} {testUri.Host}");
            }
        }, "flux");

    /// <summary>True for the Cloudflare API root (nothing beyond <c>/client/v4</c>): a GET there always answers 404, so it is not a usable test URL.</summary>
    public static bool LooksLikeCloudflareApiRoot(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        var host = u.Host;
        var isCloudflare = host.Equals("cloudflare.com", StringComparison.OrdinalIgnoreCase) ||
                           host.EndsWith(".cloudflare.com", StringComparison.OrdinalIgnoreCase);
        if (!isCloudflare) return false;
        var path = u.AbsolutePath.TrimEnd('/');
        return path.Length == 0 || path.EndsWith("/client/v4", StringComparison.OrdinalIgnoreCase);
    }
}
