using System.Net.Http.Headers;
using System.Text;
using BazinoMarketing.Core.Http;
using BazinoMarketing.Core.Settings;

namespace BazinoMarketing.Core.Tools;

/// <summary>Generic "custom service" card: a safe GET to the configured test path with the configured auth.</summary>
public static class CustomCardClient
{
    public static Task<ToolCheckResult> CheckAsync(CustomCard card, string credential, CancellationToken ct = default) =>
        ToolCheckResult.Timed(async () =>
        {
            if (!Uri.TryCreate(card.BaseUrl, UriKind.Absolute, out var baseUri) ||
                (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
                return ToolCheckResult.NotConfigured("آدرس پایه معتبر نیست (http/https)");

            var auth = (card.AuthType ?? "none").Trim().ToLowerInvariant();
            if (auth != "none" && string.IsNullOrWhiteSpace(credential))
                return ToolCheckResult.NotConfigured("کلید/توکن این سرویس ثبت نشده است");
            if (auth != "none" && !Secrets.SecretFormat.IsHeaderSafe(credential!.Trim()))
                return ToolCheckResult.InvalidKey("کلید این سرویس", credential);

            using var http = HttpFactory.Create(card.Proxy, TimeSpan.FromSeconds(25));
            switch (auth)
            {
                case "bearer":
                    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential.Trim());
                    break;
                case "header":
                    if (string.IsNullOrWhiteSpace(card.HeaderName))
                        return ToolCheckResult.NotConfigured("نام هدر احراز هویت خالی است");
                    http.DefaultRequestHeaders.TryAddWithoutValidation(card.HeaderName.Trim(), credential.Trim());
                    break;
                case "basic":
                    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                        Convert.ToBase64String(Encoding.UTF8.GetBytes(credential.Trim())));
                    break;
            }

            var path = string.IsNullOrWhiteSpace(card.TestPath) ? "" : card.TestPath.Trim();
            var url = path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? new Uri(path)
                : new Uri(baseUri.AbsoluteUri.TrimEnd('/') + (path.StartsWith('/') ? path : "/" + path));

            if (string.Equals(card.Kind, "mcp", StringComparison.OrdinalIgnoreCase))
            {
                // Only a reachability probe: MCP handshakes are executed by the command engine (phase 3+).
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Accept.ParseAdd("text/event-stream");
                using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                var code = (int)resp.StatusCode;
                if (code is 401 or 403) return ToolCheckResult.FromStatus(resp.StatusCode, "MCP GET", "");
                return new ToolCheckResult(ToolState.Connected, $"سرور MCP پاسخ داد (HTTP {code})", $"{url.Host} content-type={resp.Content.Headers.ContentType}");
            }

            using (var resp = await http.GetAsync(url, ct).ConfigureAwait(false))
            {
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return ToolCheckResult.FromStatus(resp.StatusCode, $"GET {url.AbsolutePath}", body);
                return new ToolCheckResult(ToolState.Connected, $"متصل — HTTP {(int)resp.StatusCode}", $"{url.Host}{url.AbsolutePath} {ToolCheckResult.Trim(body.Replace('\n', ' '), 120)}");
            }
        }, "custom:" + card.Id);
}
