using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using BazinoMarketing.Core.Settings;

namespace BazinoMarketing.Core.Browser;

/// <summary>
/// The browser bridge that lives inside the marketing app. It drives the owner's own Chrome through
/// the Chrome DevTools Protocol on a local port: no separate program, no cloud relay, no VPN.
/// The owner's control stays physical — closing the app or closing Chrome ends the session.
/// </summary>
public sealed class BrowserBridge
{
    private static readonly HttpClient Http = CreateHttpClient();

    private readonly Func<BrowserSettings> _settings;
    private readonly Action<string, string, string, string> _log;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private CdpSession? _session;

    public BrowserBridge(Func<BrowserSettings> settings, Action<string, string, string, string> log)
    {
        _settings = settings;
        _log = log;
    }

    /// <summary>The tab the agent is currently attached to, or null when none is selected.</summary>
    public string? SelectedTargetId => _session?.TargetId;

    private static HttpClient CreateHttpClient()
    {
        // The debugging port is on this machine: never send it through a system or configured proxy.
        var handler = new SocketsHttpHandler { UseProxy = false };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
    }

    private string BaseUrl => $"http://127.0.0.1:{_settings().Port}";

    // ---------------------------------------------------------------- discovery

    /// <summary>True when Chrome is answering on the debugging port.</summary>
    public async Task<bool> IsChromeRunningAsync(CancellationToken ct)
    {
        try
        {
            using var response = await Http.GetAsync($"{BaseUrl}/json/version", ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<string> GetStringAsync(string path, CancellationToken ct)
    {
        using var response = await Http.GetAsync(BaseUrl + path, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"مرورگر پاسخ «{response.StatusCode}» داد. احتمالها کروم با پورت اشکال‌یابی باز نشده است.");
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Makes sure Chrome answers on the debugging port. Chrome only opens that port when it starts with it, so a
    /// running everyday Chrome does not count: the bridge then starts Chrome itself with the Bazino profile.
    /// </summary>
    public async Task<bool> EnsureChromeRunningAsync(CancellationToken ct)
    {
        if (await IsChromeRunningAsync(ct).ConfigureAwait(false)) return true;
        if (!OperatingSystem.IsWindows()) return false;
        if (TryLaunchChrome() is null) return false;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            await Task.Delay(250, ct).ConfigureAwait(false);
            if (await IsChromeRunningAsync(ct).ConfigureAwait(false)) return true;
        }
        return false;
    }

    /// <summary>Opens every readable tab Chrome currently has.</summary>
    public async Task<List<BrowserTarget>> ListTargetsAsync(CancellationToken ct)
    {
        var json = await GetStringAsync("/json/list", ct).ConfigureAwait(false);
        return BrowserGuard.ParseTargets(json);
    }

    // ---------------------------------------------------------------- launching

    /// <summary>
    /// Starts Chrome with the debugging port and a dedicated profile, so the owner's everyday profile is untouched.
    /// Returns the process, or null when Chrome is already running or could not be found.
    /// </summary>
    public Process? TryLaunchChrome()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var settings = _settings();
        var exe = ResolveChromePath(settings);
        if (exe is null) return null;

        var profile = string.IsNullOrWhiteSpace(settings.UserDataDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BazinoMarketing", "chrome-profile")
            : settings.UserDataDir;
        try { Directory.CreateDirectory(profile); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _log("browser", "launch", "پوشهٔ پروفایل مرورگر ساخته نشد", ex.Message);
        }

        var args = $"--remote-debugging-port={settings.Port} --user-data-dir=\"{profile}\" --new-window about:blank";
        var start = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? ""
        };
        _log("browser", "launch", "کروم با پورت اشکال‌یابی باز شد", $"exe={exe} port={settings.Port}");
        return Process.Start(start);
    }

    private static string? ResolveChromePath(BrowserSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ChromePath) && File.Exists(settings.ChromePath)) return settings.ChromePath;
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    // ---------------------------------------------------------------- commands

    /// <summary>Attaches to a tab. Detaches from the previous one first.</summary>
    public Task<BrowserResult> SelectAsync(string targetId, CancellationToken ct) =>
        WithLockAsync(() => AttachAsync(targetId, ct), ct);

    /// <summary>Opens any ordinary HTTP(S) URL in a new tab and attaches to it.</summary>
    public async Task<BrowserResult> OpenAsync(string url, CancellationToken ct)
    {
        var refused = BrowserGuard.CheckUrl(url);
        if (refused is not null)
        {
            _log("browser", "nav", "باز کردن نشانی رد شد", $"host={HostSummary(url)} reason={refused}");
            return BrowserResult.Fail(refused);
        }
        if (!await EnsureChromeRunningAsync(ct).ConfigureAwait(false))
            return BrowserResult.Fail("کروم با پورت اشکال‌یابی باز نشد. دکمهٔ «باز کردن کروم» را بزنید یا مسیر chrome.exe را در تنظیمات صفحه بنویسید.");

        return await WithLockAsync(async () =>
        {
            try
            {
                var versionJson = await GetStringAsync("/json/version", ct).ConfigureAwait(false);
                var browserWs = BrowserGuard.ParseBrowserWebSocketUrl(versionJson);
                if (browserWs is null) return BrowserResult.Fail("پورت اشکال‌یابی، آدرس اتصال مرورگر را نداد.");

                // Target.createTarget goes to the browser-level socket, not to a tab socket.
                await using var browser = new CdpSession("browser");
                await browser.ConnectAsync(browserWs, ct).ConfigureAwait(false);
                var created = await browser.SendAsync("Target.createTarget",
                    new JsonObject { ["url"] = url }, TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
                var targetId = created?["targetId"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(targetId)) return BrowserResult.Fail("مرورگر تب تازه را نساخت.");

                // A brand-new tab needs a moment before Chrome lists it.
                for (var attempt = 0; attempt < 10; attempt++)
                {
                    var targets = await ListTargetsAsync(ct).ConfigureAwait(false);
                    if (targets.Any(t => string.Equals(t.Id, targetId, StringComparison.Ordinal)))
                        return await AttachAsync(targetId, ct).ConfigureAwait(false);
                    await Task.Delay(150, ct).ConfigureAwait(false);
                }
                return BrowserResult.Fail("تب تازه ساخته شد ولی آمادهٔ اتصال نشد.");
            }
            catch (Exception ex)
            {
                return BrowserResult.Fail(ex.Message);
            }
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Navigates the selected tab to any ordinary HTTP(S) address.</summary>
    public async Task<BrowserResult> NavigateAsync(string url, CancellationToken ct)
    {
        var refused = BrowserGuard.CheckUrl(url);
        if (refused is not null)
        {
            _log("browser", "nav", "رفتن به نشانی رد شد", $"host={HostSummary(url)} reason={refused}");
            return BrowserResult.Fail(refused);
        }
        CdpSession? session;
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try { session = _session; }
        finally { _lock.Release(); }
        if (session is null) return BrowserResult.Fail("هیچ تبی انتخاب نشده. اول browser.open را بفرست.");
        try
        {
            var result = await session.SendAsync("Page.navigate", new JsonObject { ["url"] = url }, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            await WaitForLoadAsync(session, ct).ConfigureAwait(false);
            _log("browser", "nav", "تب به نشانی رفت", $"host={HostSummary(url)}");
            return BrowserResult.Good(new JsonObject { ["url"] = url, ["frameId"] = result?["frameId"]?.GetValue<string>() });
        }
        catch (Exception ex)
        {
            return BrowserResult.Fail(ex.Message);
        }
    }

    /// <summary>Reads the visible text of the selected tab. This is the only page-reading command.</summary>
    public async Task<BrowserResult> ReadAsync(int maxChars, CancellationToken ct)
    {
        var session = _session;
        if (session is null) return BrowserResult.Fail("هیچ تبی انتخاب نشده. اول browser.open را بفرست.");
        try
        {
            var raw = await session.SendAsync("Runtime.evaluate",
                new JsonObject
                {
                    ["expression"] = BrowserGuard.ReadableTextExpression,
                    ["returnByValue"] = true,
                    ["awaitPromise"] = true
                },
                TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);

            var value = raw?["result"]?["value"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(value)) return BrowserResult.Fail("مرورگر متن صفحه را برنگرداند.");
            var parsed = JsonNode.Parse(value);
            var text = parsed?["text"]?.GetValue<string>() ?? "";
            var (trimmed, total, truncated) = BrowserGuard.Trim(text, maxChars);
            _log("browser", "read", "متن یک صفحه خوانده شد", $"host={HostSummary(parsed?["url"]?.GetValue<string>())} chars={total}");
            return BrowserResult.Good(new JsonObject
            {
                ["url"] = parsed?["url"]?.GetValue<string>() ?? "",
                ["title"] = parsed?["title"]?.GetValue<string>() ?? "",
                ["text"] = trimmed,
                ["totalChars"] = total,
                ["truncated"] = truncated
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return BrowserResult.Fail(ex.Message);
        }
    }

    /// <summary>Runs a JavaScript expression on the selected tab, after the guard rejects anything that reads stored credentials.</summary>
    public async Task<BrowserResult> EvaluateAsync(string expression, CancellationToken ct)
    {
        var refused = BrowserGuard.CheckExpression(expression);
        if (refused is not null)
        {
            _log("browser", "eval", "عبارت رد شد", $"reason={refused}");
            return BrowserResult.Fail(refused);
        }
        var session = _session;
        if (session is null) return BrowserResult.Fail("هیچ تبی انتخاب نشده. اول browser.open را بفرست.");
        try
        {
            var result = await session.SendAsync("Runtime.evaluate",
                new JsonObject
                {
                    ["expression"] = expression,
                    ["returnByValue"] = true,
                    ["awaitPromise"] = true
                },
                TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            if (result?["exceptionDetails"] is JsonObject details)
                return BrowserResult.Fail($"عبارت روی صفحه خطا داد: {details["text"]?.GetValue<string>() ?? "دلیل نامشخص"}");

            // Keep whatever the page returned as text: never re-parse it, so a cut-off value can't break the reply.
            var value = result?["result"]?["value"];
            var text = value is null ? "" : SafeToJson(value);
            var (trimmed, total, truncated) = BrowserGuard.Trim(text, 20000);
            _log("browser", "eval", "یک عبارت روی صفحه اجرا شد", $"chars={total}");
            return BrowserResult.Good(new JsonObject
            {
                ["value"] = JsonValue.Create<string>(trimmed),
                ["totalChars"] = total,
                ["truncated"] = truncated
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return BrowserResult.Fail(ex.Message);
        }
    }

    private static string SafeToJson(JsonNode value)
    {
        try { return value.ToJsonString(); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return value.ToString(); }
    }

    /// <summary>Detaches from the tab. Does not close it — the owner's tabs stay his.</summary>
    public async Task DetachAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_session is not null)
            {
                await _session.DisposeAsync().ConfigureAwait(false);
                _session = null;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Everything the owner and the agent need to know about the bridge right now.</summary>
    public async Task<BrowserResult> StatusAsync(CancellationToken ct)
    {
        var settings = _settings();
        var running = await IsChromeRunningAsync(ct).ConfigureAwait(false);
        var tabs = new List<BrowserTarget>();
        if (running)
        {
            try { tabs = await ListTargetsAsync(ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                tabs = new List<BrowserTarget>();
            }
        }
        return BrowserResult.Good(new JsonObject
        {
            ["chromeRunning"] = running,
            ["port"] = settings.Port,
            ["selectedTargetId"] = SelectedTargetId,
            ["tabCount"] = tabs.Count,
            ["tabs"] = new JsonArray(tabs.Select(t => (JsonNode)new JsonObject
            {
                ["targetId"] = t.Id,
                ["title"] = t.Title,
                ["url"] = t.Url
            }).ToArray()),
            ["urlPolicy"] = "any-http-https"
        });
    }

    // ---------------------------------------------------------------- internals

    private static string HostSummary(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "unknown";

    /// <summary>Runs <paramref name="body"/> while holding the bridge lock (the tab can only be attached by one caller at a time).</summary>
    private async Task<BrowserResult> WithLockAsync(Func<Task<BrowserResult>> body, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await body().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Every caller wants a reply, not an exception: the agent explains a refusal, it does not crash.
            return BrowserResult.Fail(ex.Message);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Finds the tab, drops the previous session and connects to the new one. The caller holds the lock.</summary>
    private async Task<BrowserResult> AttachAsync(string targetId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(targetId)) return BrowserResult.Fail("شناسهٔ تب خالی است.");
        var targets = await ListTargetsAsync(ct).ConfigureAwait(false);
        var target = targets.FirstOrDefault(t => string.Equals(t.Id, targetId, StringComparison.Ordinal));
        if (target is null) return BrowserResult.Fail("این تب دیگر باز نیست.");
        if (target.WebSocketDebuggerUrl is null) return BrowserResult.Fail("این تب آدرس اتصال ندارد.");

        if (_session is not null)
        {
            await _session.DisposeAsync().ConfigureAwait(false);
            _session = null;
        }

        var session = new CdpSession(target.Id);
        await session.ConnectAsync(target.WebSocketDebuggerUrl, ct).ConfigureAwait(false);
        // Bring it to the front so the owner can see what the agent is doing.
        await session.SendAsync("Page.bringToFront", null, TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        _session = session;
        _log("browser", "select", "به یک تب مرورگر وصل شد", $"host={HostSummary(target.Url)}");
        return BrowserResult.Good(new JsonObject
        {
            ["targetId"] = target.Id,
            ["title"] = target.Title,
            ["url"] = target.Url
        });
    }

    /// <summary>Waits until the page stops loading, with a hard cap. Never throws on timeout — the page may simply be slow.</summary>
    private static async Task WaitForLoadAsync(CdpSession session, CancellationToken ct)
    {
        try
        {
            await session.SendAsync("Page.enable", null, TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(12);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(400, ct).ConfigureAwait(false);
                var state = await session.SendAsync("Runtime.evaluate",
                    new JsonObject
                    {
                        ["expression"] = "document.readyState",
                        ["returnByValue"] = true
                    },
                    TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                var value = state?["result"]?["value"]?.GetValue<string>();
                if (string.Equals(value, "complete", StringComparison.OrdinalIgnoreCase)) return;
            }
        }
        catch (TimeoutException)
        {
            // A page that never settles is still readable; nothing to report.
        }
    }
}
