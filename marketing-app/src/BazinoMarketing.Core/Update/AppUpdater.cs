using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using BazinoMarketing.Core.Http;
using BazinoMarketing.Core.Settings;

namespace BazinoMarketing.Core.Update;

public sealed record ReleaseInfo(string Tag, string Name, bool Prerelease, DateTimeOffset? PublishedAt, long ExeAssetId, long ExeSize, long SumsAssetId, string HtmlUrl);

public sealed record UpdateDownload(ReleaseInfo Release, string ExePath, string Sha256, long Bytes, long DurationMs);

public sealed class UpdateException : Exception
{
    public string Code { get; }
    public UpdateException(string code, string message, Exception? inner = null) : base(message, inner) { Code = code; }
}

/// <summary>
/// Fetches a build of this app from the GitHub Releases of the configured repository (private repo → token), verifies it
/// against <c>SHA256SUMS.txt</c> and hands the verified file to the host, which swaps the running EXE and restarts.
/// </summary>
public static class AppUpdater
{
    public const string ExeAssetName = "BazinoMarketing.exe";
    public const string SumsAssetName = "SHA256SUMS.txt";
    public const string TagPrefix = "marketing-app-dev-";

    /// <summary>Picks the release: an explicit tag, or the newest release whose tag starts with <see cref="TagPrefix"/>.</summary>
    public static async Task<ReleaseInfo> FindReleaseAsync(GitHubSettings settings, string token, string? tag, CancellationToken ct)
    {
        using var http = Api(settings, token, TimeSpan.FromSeconds(40));
        JsonObject release;
        if (!string.IsNullOrWhiteSpace(tag))
        {
            release = await GetJsonObjectAsync(http, $"{Tools.GitHubClient.ApiBase}/repos/{settings.Repository}/releases/tags/{Uri.EscapeDataString(tag.Trim())}", ct).ConfigureAwait(false);
        }
        else
        {
            using var res = await http.GetAsync($"{Tools.GitHubClient.ApiBase}/repos/{settings.Repository}/releases?per_page=30", ct).ConfigureAwait(false);
            await EnsureAsync(res, "list releases").ConfigureAwait(false);
            var list = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false)) as JsonArray
                       ?? throw new UpdateException("bad_response", "releases list is not an array");
            release = list.OfType<JsonObject>()
                          .Where(r => (r["tag_name"]?.GetValue<string>() ?? "").StartsWith(TagPrefix, StringComparison.Ordinal) && r["draft"]?.GetValue<bool>() != true)
                          .OrderByDescending(r => RunNumber(r["tag_name"]?.GetValue<string>() ?? ""))
                          .FirstOrDefault()
                      ?? throw new UpdateException("no_release", $"no release with tag prefix '{TagPrefix}'");
        }
        return Describe(release);
    }

    private static long RunNumber(string tag) => long.TryParse(tag.AsSpan(TagPrefix.Length), out var n) ? n : -1;

    private static ReleaseInfo Describe(JsonObject r)
    {
        long exeId = 0, exeSize = 0, sumsId = 0;
        foreach (var a in (r["assets"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            var name = a["name"]?.GetValue<string>() ?? "";
            if (name == ExeAssetName) { exeId = a["id"]?.GetValue<long>() ?? 0; exeSize = a["size"]?.GetValue<long>() ?? 0; }
            else if (name == SumsAssetName) sumsId = a["id"]?.GetValue<long>() ?? 0;
        }
        var tag = r["tag_name"]?.GetValue<string>() ?? "";
        if (exeId == 0) throw new UpdateException("no_exe", $"release {tag} has no {ExeAssetName}");
        if (sumsId == 0) throw new UpdateException("no_sums", $"release {tag} has no {SumsAssetName}");
        DateTimeOffset? published = null;
        if (r["published_at"]?.GetValue<string>() is { } p && DateTimeOffset.TryParse(p, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var dto)) published = dto;
        return new ReleaseInfo(tag, r["name"]?.GetValue<string>() ?? tag, r["prerelease"]?.GetValue<bool>() ?? false, published, exeId, exeSize, sumsId, r["html_url"]?.GetValue<string>() ?? "");
    }

    /// <summary>Downloads the EXE + checksums into <paramref name="downloadDir"/> and verifies the SHA-256. Returns the verified file.</summary>
    public static async Task<UpdateDownload> DownloadAsync(GitHubSettings settings, string token, ReleaseInfo release, string downloadDir, CancellationToken ct, IProgress<long>? progress = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Directory.CreateDirectory(downloadDir);
        var sumsText = System.Text.Encoding.UTF8.GetString(await DownloadAssetAsync(settings, token, release.SumsAssetId, ct, null).ConfigureAwait(false));
        if (!TryParseSums(sumsText, ExeAssetName, out var expected))
            throw new UpdateException("bad_sums", $"{SumsAssetName} has no entry for {ExeAssetName}");

        var target = Path.Combine(downloadDir, $"BazinoMarketing-{release.Tag}.exe");
        var tmp = target + ".part";
        var bytes = await DownloadAssetAsync(settings, token, release.ExeAssetId, ct, progress).ConfigureAwait(false);
        await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(tmp); } catch { }
            throw new UpdateException("checksum", $"SHA-256 mismatch: expected {expected[..12]}…, got {actual[..12]}…");
        }
        File.Move(tmp, target, overwrite: true);
        return new UpdateDownload(release, target, actual, bytes.LongLength, sw.ElapsedMilliseconds);
    }

    public static bool TryParseSums(string text, string fileName, out string hash)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length < 66) continue;
            var h = line[..64];
            if (!h.All(Uri.IsHexDigit)) continue;
            var name = line[64..].Trim().TrimStart('*');
            if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)) { hash = h.ToLowerInvariant(); return true; }
        }
        hash = "";
        return false;
    }

    /// <summary>
    /// Private-repo asset download: GitHub answers the authenticated request with a 302 to a signed storage URL that must be
    /// fetched WITHOUT the Authorization header. Redirects are therefore followed by hand.
    /// </summary>
    private static async Task<byte[]> DownloadAssetAsync(GitHubSettings settings, string token, long assetId, CancellationToken ct, IProgress<long>? progress)
    {
        var url = $"{Tools.GitHubClient.ApiBase}/repos/{settings.Repository}/releases/assets/{assetId}";
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var api = Api(settings, token, TimeSpan.FromSeconds(45), allowRedirect: false);
                using var first = new HttpRequestMessage(HttpMethod.Get, url);
                first.Headers.Accept.Clear();
                first.Headers.Accept.ParseAdd("application/octet-stream");
                using var res1 = await api.SendAsync(first, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if ((int)res1.StatusCode is >= 300 and < 400 && res1.Headers.Location is { } location)
                {
                    using var plain = HttpFactory.Create(settings.Proxy, TimeSpan.FromMinutes(4));
                    plain.DefaultRequestHeaders.Accept.Clear();
                    using var res2 = await plain.GetAsync(location, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    await EnsureAsync(res2, "download asset").ConfigureAwait(false);
                    return await ReadAllAsync(res2, ct, progress).ConfigureAwait(false);
                }
                await EnsureAsync(res1, "download asset").ConfigureAwait(false);
                return await ReadAllAsync(res1, ct, progress).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < 3 && (ex is IOException or HttpRequestException or TaskCanceledException or OperationCanceledException))
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct).ConfigureAwait(false);
            }
        }
        throw new UpdateException("download_failed", "Download stream failed after 3 attempts: " + (lastError?.Message ?? "stalled"), lastError);
    }

    private static async Task<byte[]> ReadAllAsync(HttpResponseMessage res, CancellationToken ct, IProgress<long>? progress)
    {
        await using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var ms = new MemoryStream(res.Content.Headers.ContentLength is { } len && len < int.MaxValue ? (int)len : 64 * 1024 * 1024);
        var buffer = new byte[256 * 1024];
        long total = 0;
        while (true)
        {
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readCts.CancelAfter(TimeSpan.FromSeconds(35));
            var n = await stream.ReadAsync(buffer, readCts.Token).ConfigureAwait(false);
            if (n <= 0) break;
            ms.Write(buffer, 0, n);
            total += n;
            progress?.Report(total);
        }
        return ms.ToArray();
    }

    private static HttpClient Api(GitHubSettings settings, string token, TimeSpan timeout, bool allowRedirect = true)
    {
        HttpClient http;
        if (allowRedirect)
        {
            http = HttpFactory.Create(settings.Proxy, timeout);
        }
        else
        {
            var handler = new SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All, ConnectTimeout = TimeSpan.FromSeconds(20) };
            var mode = (settings.Proxy?.Mode ?? "system").Trim().ToLowerInvariant();
            if (mode == "none") handler.UseProxy = false;
            else if (mode == "custom" && Uri.TryCreate(settings.Proxy?.Url, UriKind.Absolute, out var proxyUri)) { handler.UseProxy = true; handler.Proxy = new WebProxy(proxyUri) { BypassProxyOnLocal = true }; }
            http = new HttpClient(handler, disposeHandler: true) { Timeout = timeout };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(HttpFactory.UserAgent);
        }
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        return http;
    }

    private static async Task<JsonObject> GetJsonObjectAsync(HttpClient http, string url, CancellationToken ct)
    {
        using var res = await http.GetAsync(url, ct).ConfigureAwait(false);
        await EnsureAsync(res, "get " + url).ConfigureAwait(false);
        return JsonNode.Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false)) as JsonObject
               ?? throw new UpdateException("bad_response", "expected a JSON object");
    }

    private static async Task EnsureAsync(HttpResponseMessage res, string what)
    {
        if (res.IsSuccessStatusCode) return;
        var body = "";
        try { body = await res.Content.ReadAsStringAsync().ConfigureAwait(false); } catch { }
        var detail = "";
        try { using var doc = JsonDocument.Parse(body); if (doc.RootElement.TryGetProperty("message", out var m)) detail = m.GetString() ?? ""; } catch (JsonException) { }
        throw new UpdateException("http_" + (int)res.StatusCode, $"{what}: HTTP {(int)res.StatusCode}" + (detail.Length > 0 ? " — " + detail : ""));
    }
}
