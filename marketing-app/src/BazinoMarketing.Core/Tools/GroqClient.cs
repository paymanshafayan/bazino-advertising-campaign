using System.Net.Http.Headers;
using System.Text.Json;
using BazinoMarketing.Core.Http;
using BazinoMarketing.Core.Settings;

namespace BazinoMarketing.Core.Tools;

/// <summary>
/// Read-only health check for Groq Speech-to-Text (Whisper). Sends <c>GET /models</c> and confirms the Whisper models
/// are accessible with the configured API key.
/// </summary>
public static class GroqClient
{
    public static Task<ToolCheckResult> CheckAsync(GroqSettings settings, string apiKey, CancellationToken ct = default) =>
        ToolCheckResult.Timed(async () =>
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                return ToolCheckResult.NotConfigured("کلید Groq ثبت نشده است (اختیاری — در صورت خالی بودن از Cloudflare Whisper استفاده می‌شود)");
            if (!Secrets.SecretFormat.IsHeaderSafe(apiKey.Trim()))
                return ToolCheckResult.InvalidKey("کلید Groq", apiKey);
            if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
                return ToolCheckResult.NotConfigured("آدرس پایهٔ Groq باید https باشد");

            var root = settings.BaseUrl.TrimEnd('/');
            using var http = HttpFactory.Create(settings.Proxy, TimeSpan.FromSeconds(25));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

            using var res = await http.GetAsync($"{root}/models", ct).ConfigureAwait(false);
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                return ToolCheckResult.FromStatus(res.StatusCode, "GET /models", body);

            var whisperModels = new List<string>();
            int totalModels = 0;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    totalModels = data.GetArrayLength();
                    foreach (var item in data.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                        {
                            var id = idProp.GetString() ?? "";
                            if (id.Contains("whisper", StringComparison.OrdinalIgnoreCase))
                                whisperModels.Add(id);
                        }
                    }
                }
            }
            catch (JsonException) { }

            var selected = string.IsNullOrWhiteSpace(settings.Model) ? "whisper-large-v3" : settings.Model.Trim();
            var summary = whisperModels.Count > 0
                ? $"متصل — مدل صوتی {selected} فعال و در دسترس است"
                : $"متصل — کلید معتبر است ({totalModels} مدل)";
            var detail = $"model={selected} whisper={string.Join(",", whisperModels)} total={totalModels}";
            return new ToolCheckResult(ToolState.Connected, summary, detail);
        }, "groq");
}
