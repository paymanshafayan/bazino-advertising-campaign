using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BazinoMarketing.Core.Http;
using BazinoMarketing.Core.Secrets;
using BazinoMarketing.Core.Settings;

namespace BazinoMarketing.Core.Tools;

/// <summary>GitHub repository checks and file uploads used by the marketing studio.</summary>
public sealed class GitHubClient
{
    public const string ApiBase = "https://api.github.com";

    private static HttpClient Create(ProxySettings proxy, string token, TimeSpan? timeout = null)
    {
        var http = HttpFactory.Create(proxy, timeout ?? TimeSpan.FromSeconds(25));
        http.DefaultRequestHeaders.Accept.Clear();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrWhiteSpace(token))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        return http;
    }

    public static bool IsValidRepository(string? repo) =>
        !string.IsNullOrWhiteSpace(repo) && repo.Count(c => c == '/') == 1 && !repo.StartsWith('/') && !repo.EndsWith('/');

    public static Task<ToolCheckResult> CheckAsync(GitHubSettings settings, string token, CancellationToken ct = default) =>
        ToolCheckResult.Timed(async () =>
        {
            if (string.IsNullOrWhiteSpace(token))
                return ToolCheckResult.NotConfigured("توکن GitHub ثبت نشده است");
            if (!SecretFormat.IsHeaderSafe(token.Trim()))
                return ToolCheckResult.InvalidKey("توکن GitHub", token);
            if (!IsValidRepository(settings.Repository))
                return ToolCheckResult.NotConfigured("نام مخزن باید به شکل owner/repo باشد");

            using var http = Create(settings.Proxy, token);

            using var repoResp = await http.GetAsync($"{ApiBase}/repos/{settings.Repository}", ct).ConfigureAwait(false);
            var repoBody = await repoResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!repoResp.IsSuccessStatusCode)
                return ToolCheckResult.FromStatus(repoResp.StatusCode, "GET /repos", repoBody);

            bool isPrivate = false, canPush = false;
            try
            {
                using var doc = JsonDocument.Parse(repoBody);
                isPrivate = doc.RootElement.TryGetProperty("private", out var p) && p.ValueKind == JsonValueKind.True;
                canPush = doc.RootElement.TryGetProperty("permissions", out var perms) &&
                          perms.TryGetProperty("push", out var push) && push.ValueKind == JsonValueKind.True;
            }
            catch (JsonException) { }

            var branch = Uri.EscapeDataString(settings.Branch).Replace("%2F", "/");
            using var branchResp = await http.GetAsync($"{ApiBase}/repos/{settings.Repository}/branches/{branch}", ct).ConfigureAwait(false);
            var branchOk = branchResp.IsSuccessStatusCode;

            string login = "";
            string rate = "";
            using (var rateResp = await http.GetAsync($"{ApiBase}/rate_limit", ct).ConfigureAwait(false))
            {
                if (rateResp.IsSuccessStatusCode)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(await rateResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                        var core = doc.RootElement.GetProperty("resources").GetProperty("core");
                        rate = $"{core.GetProperty("remaining").GetInt32()}/{core.GetProperty("limit").GetInt32()}";
                    }
                    catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { }
                }
            }
            using (var userResp = await http.GetAsync($"{ApiBase}/user", ct).ConfigureAwait(false))
            {
                if (userResp.IsSuccessStatusCode)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(await userResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                        login = doc.RootElement.TryGetProperty("login", out var l) ? l.GetString() ?? "" : "";
                    }
                    catch (JsonException) { }
                }
            }

            var detail = $"repo={settings.Repository} private={isPrivate} push={canPush} branch={(branchOk ? "ok" : "missing:" + (int)branchResp.StatusCode)} user={login} rate={rate}";
            if (!branchOk)
                return new ToolCheckResult(ToolState.Error, $"مخزن در دسترس است اما شاخهٔ «{settings.Branch}» پیدا نشد", detail, "branch_missing");
            if (!canPush)
                return new ToolCheckResult(ToolState.NeedsLogin, "توکن فقط دسترسی خواندن دارد؛ برای صندوق پیام دسترسی نوشتن لازم است", detail, "no_push");

            var who = string.IsNullOrEmpty(login) ? "" : $" ({login})";
            return new ToolCheckResult(ToolState.Connected, $"متصل{who} — مخزن و شاخه در دسترس‌اند", detail);
        }, "github");

    public const long MaxDirectUploadBytes = 50L * 1024 * 1024;

    /// <summary>
    /// Uploads one local file directly into the GitHub repository (without Git LFS) so the agent can read and inspect
    /// the real binary directly in the workspace. Retries up to 3 times on transient network or proxy timeouts.
    /// </summary>
    public static async Task<GitHubFileUploadResult> UploadContentAsync(
        GitHubSettings settings, string token, string path, byte[] content, CancellationToken ct = default, string? commitMessage = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(content);
        if (!IsValidRepository(settings.Repository)) throw new ArgumentException("نام مخزن GitHub باید به صورت owner/repo باشد.", nameof(settings));
        if (string.IsNullOrWhiteSpace(settings.Branch)) throw new ArgumentException("شاخهٔ GitHub تنظیم نشده است.", nameof(settings));
        if (content.LongLength == 0) throw new ArgumentException("فایل خالی است.", nameof(content));
        if (content.LongLength > MaxDirectUploadBytes)
            throw new InvalidOperationException("حجم فایل از سقف ۵۰ مگابایتی ارسال مستقیم به مخزن گیت‌هاب بیشتر است.");
        if (string.IsNullOrWhiteSpace(token) || !SecretFormat.IsHeaderSafe(token.Trim()))
            throw new InvalidOperationException("اتصال GitHub در دسترس نیست.");

        var normalizedPath = NormalizeContentPath(path);
        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        var encodedRepo = string.Join("/", settings.Repository.Split('/').Select(Uri.EscapeDataString));
        var encodedPath = string.Join("/", segments.Select(Uri.EscapeDataString));
        var uri = $"{ApiBase}/repos/{encodedRepo}/contents/{encodedPath}";
        var payload = JsonSerializer.Serialize(new
        {
            message = string.IsNullOrWhiteSpace(commitMessage) ? "media: upload " + Path.GetFileName(normalizedPath) : commitMessage.Trim(),
            content = Convert.ToBase64String(content),
            branch = settings.Branch
        });

        var watch = System.Diagnostics.Stopwatch.StartNew();
        using var http = Create(settings.Proxy, token, TimeSpan.FromMinutes(4));

        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var body = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await http.PutAsync(uri, body, ct).ConfigureAwait(false);
                var responseText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var isTransientStatus = (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests;
                    if (isTransientStatus && attempt < 3)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(1200 * attempt), ct).ConfigureAwait(false);
                        continue;
                    }

                    var message = "";
                    try
                    {
                        using var error = JsonDocument.Parse(responseText);
                        message = error.RootElement.TryGetProperty("message", out var value) ? value.GetString() ?? "" : "";
                    }
                    catch (JsonException) { }
                    if (string.IsNullOrWhiteSpace(message)) message = response.StatusCode.ToString();
                    throw new InvalidOperationException($"GitHub پاسخ {((int)response.StatusCode)} داد: {message}");
                }

                using var document = JsonDocument.Parse(responseText);
                var root = document.RootElement;
                var fileNode = root.TryGetProperty("content", out var contentNode) ? contentNode : default;
                var htmlUrl = fileNode.ValueKind == JsonValueKind.Object && fileNode.TryGetProperty("html_url", out var html) ? html.GetString() ?? "" : "";
                var sha = fileNode.ValueKind == JsonValueKind.Object && fileNode.TryGetProperty("sha", out var fileSha) ? fileSha.GetString() ?? "" : "";
                var commitSha = root.TryGetProperty("commit", out var commit) && commit.TryGetProperty("sha", out var commitValue) ? commitValue.GetString() ?? "" : "";
                watch.Stop();
                return new GitHubFileUploadResult(normalizedPath, htmlUrl, sha, commitSha, watch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested && attempt < 3)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(1200 * attempt), ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (attempt < 3)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(1200 * attempt), ct).ConfigureAwait(false);
            }
        }

        throw lastError ?? new InvalidOperationException("ارسال فایل به گیت‌هاب انجام نشد.");
    }

    /// <summary>Lists files in one repository directory using the same GitHub connection stored in the app.</summary>
    public static async Task<IReadOnlyList<GitHubContentEntry>> ListDirectoryAsync(
        GitHubSettings settings, string token, string path, CancellationToken ct = default)
    {
        var normalizedPath = NormalizeContentPath(path);
        using var http = Create(settings.Proxy, token, TimeSpan.FromSeconds(45));
        var encodedRepo = string.Join("/", settings.Repository.Split('/').Select(Uri.EscapeDataString));
        var encodedPath = string.Join("/", normalizedPath.Split('/').Select(Uri.EscapeDataString));
        var branch = Uri.EscapeDataString(settings.Branch);
        using var response = await http.GetAsync($"{ApiBase}/repos/{encodedRepo}/contents/{encodedPath}?ref={branch}", ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return Array.Empty<GitHubContentEntry>();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"GitHub پاسخ {(int)response.StatusCode} داد: {ShortGitHubError(text)}");
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<GitHubContentEntry>();
            return document.RootElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object)
                .Select(e => new GitHubContentEntry(
                    ReadString(e, "name"), ReadString(e, "path"), ReadString(e, "type"),
                    ReadString(e, "sha"), ReadString(e, "download_url")))
                .Where(e => e.Type == "file" && !string.IsNullOrWhiteSpace(e.Path)).ToArray();
        }
        catch (JsonException ex) { throw new InvalidOperationException("فهرست پوشه از GitHub خوانده نشد.", ex); }
    }

    /// <summary>Downloads a private repository file through GitHub's authenticated raw-content endpoint.</summary>
    public static async Task<byte[]> DownloadRawAsync(
        GitHubSettings settings, string token, string path, long maxBytes = 100L * 1024 * 1024, CancellationToken ct = default)
    {
        var normalizedPath = NormalizeContentPath(path);
        using var http = Create(settings.Proxy, token, TimeSpan.FromMinutes(4));
        http.DefaultRequestHeaders.Accept.Clear();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.raw"));
        var encodedRepo = string.Join("/", settings.Repository.Split('/').Select(Uri.EscapeDataString));
        var encodedPath = string.Join("/", normalizedPath.Split('/').Select(Uri.EscapeDataString));
        var branch = Uri.EscapeDataString(settings.Branch);
        using var response = await http.GetAsync($"{ApiBase}/repos/{encodedRepo}/contents/{encodedPath}?ref={branch}", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException($"بارگیری رسانه از GitHub ممکن نشد ({(int)response.StatusCode}): {ShortGitHubError(text)}");
        }
        if (response.Content.Headers.ContentLength is long size && size > maxBytes)
            throw new InvalidOperationException("حجم رسانه از سقف امن بارگیری بیشتر است.");
        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0) break;
            if (destination.Length + read > maxBytes) throw new InvalidOperationException("حجم رسانه از سقف امن بارگیری بیشتر است.");
            destination.Write(buffer, 0, read);
        }
        return destination.ToArray();
    }

    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static string ShortGitHubError(string text)
    {
        var safe = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        return safe.Length <= 240 ? safe : safe[..240] + "…";
    }

    private static string NormalizeContentPath(string path)
    {
        var normalizedPath = (path ?? "").Replace('\\', '/').Trim('/');
        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || segments.Any(s => s is "." or ".." || s.Contains(':') || s.Contains('\0')))
            throw new ArgumentException("مسیر مقصد فایل معتبر نیست.", nameof(path));
        return normalizedPath;
    }
}

public sealed record GitHubContentEntry(string Name, string Path, string Type, string Sha, string DownloadUrl);
public sealed record GitHubFileUploadResult(string Path, string HtmlUrl, string FileSha, string CommitSha, long DurationMs);
