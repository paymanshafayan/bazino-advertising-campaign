using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BazinoMarketing.Core.Http;
using BazinoMarketing.Core.Settings;

namespace BazinoMarketing.Core.Mailbox;

public sealed record RemoteFile(string Name, string Path, string Sha, long Size);

public sealed class MailboxTransportException : Exception
{
    public HttpStatusCode? Status { get; }
    public bool IsTransient { get; }
    public MailboxTransportException(string message, HttpStatusCode? status = null, bool transient = false, Exception? inner = null)
        : base(message, inner) { Status = status; IsTransient = transient; }
}

/// <summary>
/// Minimal GitHub Contents API client for the mailbox folder: conditional listing (ETag), read, create/update and delete.
/// Every write is one small commit on the configured branch; the token only ever touches paths under the mailbox folder.
/// </summary>
public sealed class GitHubMailboxTransport : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _repo;
    private readonly string _branch;
    private readonly string _root;

    public GitHubMailboxTransport(GitHubSettings settings, string token, TimeSpan? timeout = null)
    {
        if (!Tools.GitHubClient.IsValidRepository(settings.Repository)) throw new ArgumentException("repository", nameof(settings));
        _repo = settings.Repository.Trim();
        _branch = string.IsNullOrWhiteSpace(settings.Branch) ? "main" : settings.Branch.Trim();
        _root = (settings.MailboxPath ?? "").Trim().Trim('/');
        if (_root.Length == 0) throw new ArgumentException("mailbox path", nameof(settings));
        _http = HttpFactory.Create(settings.Proxy, timeout ?? TimeSpan.FromSeconds(30));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
    }

    public string Root => _root;
    public string Branch => _branch;

    private string ContentsUrl(string relativePath)
    {
        var path = relativePath.Length == 0 ? _root : _root + "/" + relativePath.Trim('/');
        var escaped = string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
        return $"{Tools.GitHubClient.ApiBase}/repos/{_repo}/contents/{escaped}";
    }

    /// <summary>Lists a folder. Returns null when the ETag still matches (nothing changed) and an empty list when the folder does not exist.</summary>
    public async Task<IReadOnlyList<RemoteFile>?> ListAsync(string dir, string? etag, Action<string?> etagOut, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, ContentsUrl(dir) + "?ref=" + Uri.EscapeDataString(_branch));
        if (!string.IsNullOrEmpty(etag)) req.Headers.TryAddWithoutValidation("If-None-Match", etag);
        using var res = await SendAsync(req, ct).ConfigureAwait(false);
        if (res.StatusCode == HttpStatusCode.NotModified) return null;
        if (res.StatusCode == HttpStatusCode.NotFound) { etagOut(null); return Array.Empty<RemoteFile>(); }
        await EnsureSuccess(res, "list " + dir).ConfigureAwait(false);
        etagOut(res.Headers.ETag?.ToString());
        var node = await res.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct).ConfigureAwait(false);
        if (node is not JsonArray arr) return Array.Empty<RemoteFile>();
        var list = new List<RemoteFile>();
        foreach (var item in arr.OfType<JsonObject>())
        {
            if (item["type"]?.GetValue<string>() != "file") continue;
            list.Add(new RemoteFile(item["name"]?.GetValue<string>() ?? "", item["path"]?.GetValue<string>() ?? "",
                item["sha"]?.GetValue<string>() ?? "", item["size"]?.GetValue<long>() ?? 0));
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return list;
    }

    /// <summary>Reads a text file; null when it does not exist. When <paramref name="etag"/> matches, returns null and reports <c>notModified</c>.</summary>
    public async Task<(string? Text, string? Sha, string? ETag, bool NotModified)> ReadAsync(string relativePath, string? etag, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, ContentsUrl(relativePath) + "?ref=" + Uri.EscapeDataString(_branch));
        if (!string.IsNullOrEmpty(etag)) req.Headers.TryAddWithoutValidation("If-None-Match", etag);
        using var res = await SendAsync(req, ct).ConfigureAwait(false);
        if (res.StatusCode == HttpStatusCode.NotModified) return (null, null, etag, true);
        if (res.StatusCode == HttpStatusCode.NotFound) return (null, null, null, false);
        await EnsureSuccess(res, "read " + relativePath).ConfigureAwait(false);
        var node = await res.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct).ConfigureAwait(false)
                   ?? throw new MailboxTransportException("empty contents response for " + relativePath);
        var sha = node["sha"]?.GetValue<string>();
        var encoding = node["encoding"]?.GetValue<string>();
        var content = node["content"]?.GetValue<string>() ?? "";
        if (encoding != "base64")
            throw new MailboxTransportException($"unexpected encoding '{encoding}' for {relativePath}");
        var bytes = Convert.FromBase64String(content.Replace("\n", "").Replace("\r", ""));
        return (Encoding.UTF8.GetString(bytes), sha, res.Headers.ETag?.ToString(), false);
    }

    /// <summary>Creates or updates a file (pass the current blob sha to update). Returns the new blob sha.</summary>
    public async Task<string> WriteAsync(string relativePath, string text, string message, string? sha, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["message"] = message,
            ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(text)),
            ["branch"] = _branch
        };
        if (!string.IsNullOrEmpty(sha)) body["sha"] = sha;
        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage res;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Put, ContentsUrl(relativePath))
                {
                    Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
                };
                res = await SendAsync(req, ct).ConfigureAwait(false);
            }
            catch (MailboxTransportException ex) when (ex.IsTransient && attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(400 * (attempt + 1)), ct).ConfigureAwait(false);
                continue;
            }
            using (res)
            {
                if ((res.StatusCode == HttpStatusCode.Conflict || res.StatusCode == (HttpStatusCode)422) && attempt < 2)
                {
                    // Someone else touched the file (or a stale sha): refresh and retry once or twice.
                    await Task.Delay(TimeSpan.FromSeconds(1 + attempt), ct).ConfigureAwait(false);
                    var (_, currentSha, _, _) = await ReadAsync(relativePath, null, ct).ConfigureAwait(false);
                    if (currentSha is null) body.Remove("sha"); else body["sha"] = currentSha;
                    continue;
                }
                await EnsureSuccess(res, "write " + relativePath).ConfigureAwait(false);
                var node = await res.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct).ConfigureAwait(false);
                return node?["content"]?["sha"]?.GetValue<string>() ?? "";
            }
        }
    }

    public async Task DeleteAsync(string relativePath, string sha, string message, CancellationToken ct)
    {
        var body = new JsonObject { ["message"] = message, ["sha"] = sha, ["branch"] = _branch };
        using var req = new HttpRequestMessage(HttpMethod.Delete, ContentsUrl(relativePath))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        using var res = await SendAsync(req, ct).ConfigureAwait(false);
        if (res.StatusCode == HttpStatusCode.NotFound) return; // already gone
        await EnsureSuccess(res, "delete " + relativePath).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        try
        {
            return await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new MailboxTransportException("network: " + ex.Message, ex.StatusCode, transient: true, inner: ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new MailboxTransportException("timeout", null, transient: true, inner: ex);
        }
    }

    private static async Task EnsureSuccess(HttpResponseMessage res, string what)
    {
        if (res.IsSuccessStatusCode) return;
        string detail = "";
        try
        {
            var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("message", out var m)) detail = m.GetString() ?? "";
        }
        catch (JsonException) { }
        var transient = (int)res.StatusCode >= 500 || res.StatusCode == HttpStatusCode.TooManyRequests
                        || (res.StatusCode == HttpStatusCode.Forbidden && detail.Contains("rate limit", StringComparison.OrdinalIgnoreCase));
        throw new MailboxTransportException($"{what}: HTTP {(int)res.StatusCode}" + (detail.Length > 0 ? " — " + detail : ""), res.StatusCode, transient);
    }

    public void Dispose() => _http.Dispose();
}
