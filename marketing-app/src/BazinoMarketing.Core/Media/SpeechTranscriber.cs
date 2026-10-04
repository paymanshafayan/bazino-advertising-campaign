using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BazinoMarketing.Core.Http;
using BazinoMarketing.Core.Secrets;
using BazinoMarketing.Core.Settings;

namespace BazinoMarketing.Core.Media;

public sealed record TranscriptSegment(double Start, double End, string Text);

public sealed record SpeechTranscriptionResult(
    bool Ok,
    string Code,
    string Message,
    string Provider,
    string Model,
    string Language,
    double? DurationSeconds,
    string Text,
    IReadOnlyList<TranscriptSegment> Segments,
    string? Vtt = null,
    long DurationMs = 0);

/// <summary>
/// Speech-to-text and speech-translation client supporting Groq Whisper (<c>whisper-large-v3</c> / <c>whisper-large-v3-turbo</c>,
/// 2,000 free requests/day) with automatic fallback to Cloudflare Workers AI (<c>@cf/openai/whisper-large-v3-turbo</c>).
/// </summary>
public static class SpeechTranscriber
{
    public const string GroqBaseUrl = "https://api.groq.com/openai/v1";
    public const string DefaultGroqModel = "whisper-large-v3";
    public const string CloudflareWhisperModel = "@cf/openai/whisper-large-v3-turbo";
    public const long MaxAudioBytes = 24 * 1024 * 1024; // 24 MiB safety margin under Groq's 25 MB free-tier limit

    public static (string ApiKey, ProxySettings Proxy, string BaseUrl) ResolveGroqCredentials(AppSettings settings, ISecretStore secrets)
    {
        var direct = secrets.GetOrEmpty(SecretKeys.GroqApiKey).Trim();
        if (string.IsNullOrWhiteSpace(direct))
            direct = secrets.GetOrEmpty(SecretKeys.ForCustomCredential("groq")).Trim();

        var card = settings.CustomCards.FirstOrDefault(c =>
            c.Id.Contains("groq", StringComparison.OrdinalIgnoreCase) ||
            c.Name.Contains("groq", StringComparison.OrdinalIgnoreCase) ||
            c.BaseUrl.Contains("groq.com", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(direct) && card is not null)
            direct = secrets.GetOrEmpty(SecretKeys.ForCustomCredential(card.Id)).Trim();

        var proxy = settings.Groq?.Proxy ?? card?.Proxy ?? settings.GitHub.Proxy;
        var configuredBase = settings.Groq?.BaseUrl?.Trim();
        var baseUrl = !string.IsNullOrWhiteSpace(configuredBase)
            ? configuredBase.TrimEnd('/')
            : card is not null && !string.IsNullOrWhiteSpace(card.BaseUrl) && card.BaseUrl.Contains("groq.com", StringComparison.OrdinalIgnoreCase)
                ? card.BaseUrl.TrimEnd('/')
                : GroqBaseUrl;
        return (direct, proxy, baseUrl);
    }

    public static async Task<SpeechTranscriptionResult> TranscribeAsync(
        AppSettings settings,
        ISecretStore secrets,
        byte[] audioBytes,
        string fileName,
        string? language = null,
        string? prompt = null,
        bool translateToEnglish = false,
        CancellationToken ct = default)
    {
        if (audioBytes is null || audioBytes.Length == 0)
            return Fail("empty_audio", "فایل صوتی استخراج‌شده خالی است.");
        if (audioBytes.Length > MaxAudioBytes)
            return Fail("audio_too_large", "حجم فایل صوتی بیش از سقف مجاز (۲۴ مگابایت) است.");

        var mode = (settings.Media?.TranscriptionProvider ?? "auto").Trim().ToLowerInvariant();
        var lang = NormalizeLanguage(language ?? (!string.IsNullOrWhiteSpace(settings.Groq?.Language) ? settings.Groq!.Language : settings.Media?.TranscriptionLanguage));
        var (groqKey, groqProxy, groqBaseUrl) = ResolveGroqCredentials(settings, secrets);
        var hasGroq = !string.IsNullOrWhiteSpace(groqKey) && SecretFormat.IsHeaderSafe(groqKey);

        var cfKey = secrets.GetOrEmpty(SecretKeys.FluxApiKey).Trim();
        var cfAccount = (settings.Flux?.AccountId ?? "").Trim();
        var hasCloudflare = !string.IsNullOrWhiteSpace(cfKey) && SecretFormat.IsHeaderSafe(cfKey) && !string.IsNullOrWhiteSpace(cfAccount);

        if (mode == "groq" || (mode != "cloudflare" && hasGroq))
        {
            if (!hasGroq)
                return Fail("groq_key_missing", "کلید API سرویس Groq ثبت نشده است.");

            var groqModel = !string.IsNullOrWhiteSpace(settings.Groq?.Model)
                ? settings.Groq!.Model.Trim()
                : string.IsNullOrWhiteSpace(settings.Media?.GroqModel) ? DefaultGroqModel : settings.Media!.GroqModel.Trim();
            if (translateToEnglish) groqModel = "whisper-large-v3"; // Groq translations endpoint requires whisper-large-v3

            var groqResult = await TranscribeWithGroqAsync(groqBaseUrl, groqKey, groqProxy, groqModel, audioBytes, fileName, lang, prompt, translateToEnglish, ct).ConfigureAwait(false);
            if (groqResult.Ok || mode == "groq" || !hasCloudflare)
                return groqResult;
        }

        if (hasCloudflare)
        {
            var cfBase = string.IsNullOrWhiteSpace(settings.Flux?.BaseUrl) ? "https://api.cloudflare.com/client/v4" : settings.Flux!.BaseUrl.TrimEnd('/');
            return await TranscribeWithCloudflareAsync(cfBase, cfAccount, cfKey, settings.Flux?.Proxy ?? settings.GitHub.Proxy, audioBytes, lang, prompt, translateToEnglish, ct).ConfigureAwait(false);
        }

        return Fail("no_transcription_provider", "کلید Groq یا حساب Cloudflare برای پیاده‌سازی صوت به متن تنظیم نشده است.");
    }

    public static async Task<SpeechTranscriptionResult> TranscribeWithGroqAsync(
        string baseUrl,
        string apiKey,
        ProxySettings? proxy,
        string model,
        byte[] audioBytes,
        string fileName,
        string? language,
        string? prompt,
        bool translateToEnglish,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var endpoint = translateToEnglish
            ? $"{baseUrl.TrimEnd('/')}/audio/translations"
            : $"{baseUrl.TrimEnd('/')}/audio/transcriptions";

        var safeName = string.IsNullOrWhiteSpace(fileName) ? "audio.mp3" : Path.GetFileName(fileName);
        Exception? lastError = null;

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var http = HttpFactory.Create(proxy, TimeSpan.FromSeconds(75));
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

                using var form = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(audioBytes);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(GuessMimeType(safeName));
                form.Add(fileContent, "file", safeName);
                form.Add(new StringContent(model, Encoding.UTF8), "model");
                form.Add(new StringContent("verbose_json", Encoding.UTF8), "response_format");
                form.Add(new StringContent("0", Encoding.UTF8), "temperature");

                if (!translateToEnglish && !string.IsNullOrWhiteSpace(language))
                    form.Add(new StringContent(language, Encoding.UTF8), "language");
                if (!string.IsNullOrWhiteSpace(prompt))
                    form.Add(new StringContent(prompt, Encoding.UTF8), "prompt");

                using var res = await http.PostAsync(endpoint, form, ct).ConfigureAwait(false);
                var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                {
                    if ((int)res.StatusCode >= 500 && attempt < 2)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1.5), ct).ConfigureAwait(false);
                        continue;
                    }
                    sw.Stop();
                    return Fail($"groq_http_{(int)res.StatusCode}", $"خطای سرویس Groq (کد {(int)res.StatusCode}): {ExtractErrorDetail(body)}", "groq", model, sw.ElapsedMilliseconds);
                }

                sw.Stop();
                return ParseGroqResponse(body, model, langFallback: language ?? "", sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                lastError = ex;
                if (attempt < 2)
                    await Task.Delay(TimeSpan.FromSeconds(1.5), ct).ConfigureAwait(false);
            }
        }

        sw.Stop();
        return Fail("groq_network_error", "ارتباط با سرویس Groq ناموفق بود: " + (lastError?.Message ?? "timeout"), "groq", model, sw.ElapsedMilliseconds);
    }

    public static async Task<SpeechTranscriptionResult> TranscribeWithCloudflareAsync(
        string baseUrl,
        string accountId,
        string apiKey,
        ProxySettings? proxy,
        byte[] audioBytes,
        string? language,
        string? prompt,
        bool translateToEnglish,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var url = $"{baseUrl.TrimEnd('/')}/accounts/{Uri.EscapeDataString(accountId.Trim())}/ai/run/{CloudflareWhisperModel}";
        var payload = new JsonObject
        {
            ["audio"] = Convert.ToBase64String(audioBytes),
            ["task"] = translateToEnglish ? "translate" : "transcribe"
        };
        if (!translateToEnglish && !string.IsNullOrWhiteSpace(language))
            payload["language"] = language;
        if (!string.IsNullOrWhiteSpace(prompt))
            payload["initial_prompt"] = prompt;

        var jsonBody = payload.ToJsonString();
        Exception? lastError = null;

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var http = HttpFactory.Create(proxy, TimeSpan.FromSeconds(75));
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
                http.DefaultRequestHeaders.ExpectContinue = false;

                using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                using var res = await http.PostAsync(url, content, ct).ConfigureAwait(false);
                var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                {
                    if ((int)res.StatusCode >= 500 && attempt < 2)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1.5), ct).ConfigureAwait(false);
                        continue;
                    }
                    sw.Stop();
                    return Fail($"cloudflare_http_{(int)res.StatusCode}", $"خطای سرویس Cloudflare Whisper (کد {(int)res.StatusCode}): {ExtractErrorDetail(body)}", "cloudflare", CloudflareWhisperModel, sw.ElapsedMilliseconds);
                }

                sw.Stop();
                return ParseCloudflareResponse(body, langFallback: language ?? "", sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                lastError = ex;
                if (attempt < 2)
                    await Task.Delay(TimeSpan.FromSeconds(1.5), ct).ConfigureAwait(false);
            }
        }

        sw.Stop();
        return Fail("cloudflare_network_error", "ارتباط با سرویس Cloudflare Whisper ناموفق بود: " + (lastError?.Message ?? "timeout"), "cloudflare", CloudflareWhisperModel, sw.ElapsedMilliseconds);
    }

    public static SpeechTranscriptionResult ParseGroqResponse(string body, string model = DefaultGroqModel, string langFallback = "", long durationMs = 0)
    {
        if (string.IsNullOrWhiteSpace(body))
            return Fail("groq_empty_response", "پاسخ سرویس Groq خالی بود.", "groq", model, durationMs);

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var text = root.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? (t.GetString() ?? "").Trim() : "";
            var lang = root.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String ? (l.GetString() ?? "") : langFallback;
            double? duration = root.TryGetProperty("duration", out var d) && d.TryGetDouble(out var dv) ? dv : null;
            var segments = ReadSegments(root);
            return new SpeechTranscriptionResult(
                true, "ok", "صوت با موفقیت توسط Groq به متن تبدیل شد.",
                "groq", model, lang, duration, text, segments, null, durationMs);
        }
        catch (JsonException)
        {
            // Fallback when response_format="text" was returned as plain string
            return new SpeechTranscriptionResult(
                true, "ok", "صوت با موفقیت توسط Groq به متن تبدیل شد.",
                "groq", model, langFallback, null, body.Trim(), Array.Empty<TranscriptSegment>(), null, durationMs);
        }
    }

    public static SpeechTranscriptionResult ParseCloudflareResponse(string body, string langFallback = "", long durationMs = 0)
    {
        if (string.IsNullOrWhiteSpace(body))
            return Fail("cloudflare_empty_response", "پاسخ سرویس Cloudflare Whisper خالی بود.", "cloudflare", CloudflareWhisperModel, durationMs);

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("success", out var succ) && succ.ValueKind == JsonValueKind.False)
                return Fail("cloudflare_api_error", "خطای Cloudflare Whisper: " + ExtractErrorDetail(body), "cloudflare", CloudflareWhisperModel, durationMs);

            var result = root.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.Object ? r : root;
            var text = result.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? (t.GetString() ?? "").Trim() : "";
            var vtt = result.TryGetProperty("vtt", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var lang = langFallback;
            double? duration = null;
            if (result.TryGetProperty("transcription_info", out var info) && info.ValueKind == JsonValueKind.Object)
            {
                if (info.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(l.GetString()))
                    lang = l.GetString()!;
                if (info.TryGetProperty("duration", out var d) && d.TryGetDouble(out var dv))
                    duration = dv;
            }
            var segments = ReadSegments(result);
            return new SpeechTranscriptionResult(
                true, "ok", "صوت با موفقیت توسط Cloudflare Whisper به متن تبدیل شد.",
                "cloudflare", CloudflareWhisperModel, lang, duration, text, segments, vtt, durationMs);
        }
        catch (JsonException ex)
        {
            return Fail("cloudflare_bad_json", "پاسخ Cloudflare Whisper قابل خواندن نبود: " + ex.Message, "cloudflare", CloudflareWhisperModel, durationMs);
        }
    }

    public static string FormatReadableTranscript(SpeechTranscriptionResult res, string relativeFile)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"فایل: {relativeFile}");
        sb.AppendLine($"سرویس: {res.Provider} ({res.Model})");
        if (!string.IsNullOrWhiteSpace(res.Language))
            sb.AppendLine($"زبان تشخیص‌داده‌شده: {res.Language}");
        if (res.DurationSeconds is { } sec)
            sb.AppendLine($"مدت صوت: {sec.ToString("0.0", CultureInfo.InvariantCulture)} ثانیه");
        sb.AppendLine();
        sb.AppendLine("--- متن کامل ---");
        sb.AppendLine(string.IsNullOrWhiteSpace(res.Text) ? "(گفتاری در این فایل تشخیص داده نشد)" : res.Text);

        if (res.Segments.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("--- زمان‌بندی جمله‌ها ---");
            foreach (var seg in res.Segments)
            {
                sb.AppendLine($"[{FormatSeconds(seg.Start)} -> {FormatSeconds(seg.End)}] {seg.Text}");
            }
        }
        return sb.ToString();
    }

    private static string FormatSeconds(double s)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, s));
        return ts.TotalHours >= 1
            ? ts.ToString(@"hh\:mm\:ss\.f", CultureInfo.InvariantCulture)
            : ts.ToString(@"mm\:ss\.f", CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<TranscriptSegment> ReadSegments(JsonElement parent)
    {
        if (!parent.TryGetProperty("segments", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<TranscriptSegment>();

        var list = new List<TranscriptSegment>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var start = item.TryGetProperty("start", out var s) && s.TryGetDouble(out var sv) ? sv : 0;
            var end = item.TryGetProperty("end", out var e) && e.TryGetDouble(out var ev) ? ev : start;
            var text = item.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? (t.GetString() ?? "").Trim() : "";
            if (text.Length > 0)
                list.Add(new TranscriptSegment(Math.Round(start, 2), Math.Round(end, 2), text));
        }
        return list;
    }

    private static string? NormalizeLanguage(string? lang)
    {
        var v = (lang ?? "").Trim().ToLowerInvariant();
        if (v.Length == 0 || v == "auto" || v == "خودکار") return null;
        if (v == "persian" || v == "farsi" || v == "فارسی") return "fa";
        if (v == "english" || v == "انگلیسی") return "en";
        return v;
    }

    private static string GuessMimeType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".m4a" => "audio/mp4",
        ".flac" => "audio/flac",
        ".ogg" => "audio/ogg",
        ".webm" => "audio/webm",
        ".mp4" => "video/mp4",
        _ => "application/octet-stream"
    };

    private static string ExtractErrorDetail(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "پاسخ خالی";
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.String) return err.GetString() ?? body;
                if (err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                    return msg.GetString() ?? body;
            }
            if (root.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Array && errs.GetArrayLength() > 0)
            {
                var first = errs[0];
                if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                    return m.GetString() ?? body;
            }
        }
        catch (JsonException) { }
        return body.Length > 240 ? body[..240] + "…" : body;
    }

    private static SpeechTranscriptionResult Fail(string code, string message, string provider = "", string model = "", long durationMs = 0) =>
        new(false, code, message, provider, model, "", null, "", Array.Empty<TranscriptSegment>(), null, durationMs);
}
