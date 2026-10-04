using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BazinoMarketing.Core.Settings;

namespace BazinoMarketing.Core.Tools;

/// <summary>Where the official Kling CLI and Node were found (or not).</summary>
public sealed record KlingLocation(string? CliPath, string? NodePath, string Source)
{
    public bool CliFound => !string.IsNullOrEmpty(CliPath);
    public bool NodeFound => !string.IsNullOrEmpty(NodePath);
}

public sealed record CliResult(int ExitCode, string StdOut, string StdErr, bool TimedOut, long DurationMs);

/// <summary>Parsed view of a non-TTY Kling CLI reply: <c>{ ok, status, body }</c>.</summary>
public sealed record KlingReply(bool Ok, int Status, JsonElement? Body, string Raw)
{
    public string? FindString(params string[] names)
    {
        if (Body is not { } body) return null;
        foreach (var n in names)
        {
            var v = FindProperty(body, n, 0);
            if (v is { ValueKind: JsonValueKind.String } s) return s.GetString();
            if (v is { ValueKind: JsonValueKind.Number } num) return num.ToString();
        }
        return null;
    }

    public IReadOnlyList<string> FindStringArray(string name)
    {
        var list = new List<string>();
        if (Body is not { } body) return list;
        var el = FindProperty(body, name, 0);
        if (el is { ValueKind: JsonValueKind.Array } arr)
            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String) list.Add(item.GetString() ?? "");
                else if (item.ValueKind == JsonValueKind.Object)
                    foreach (var key in new[] { "name", "model", "id" })
                        if (item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String) { list.Add(v.GetString() ?? ""); break; }
            }
        return list;
    }

    private static JsonElement? FindProperty(JsonElement el, string name, int depth)
    {
        if (depth > 6) return null;
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in el.EnumerateObject())
                    if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
                foreach (var p in el.EnumerateObject())
                {
                    var inner = FindProperty(p.Value, name, depth + 1);
                    if (inner is not null) return inner;
                }
                return null;
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                {
                    var inner = FindProperty(item, name, depth + 1);
                    if (inner is not null) return inner;
                }
                return null;
            default:
                return null;
        }
    }
}

/// <summary>Pure helpers (testable on any OS).</summary>
public static class KlingOutput
{
    private static readonly Regex SemVer = new(@"(\d+)\.(\d+)\.(\d+)", RegexOptions.Compiled);
    public static readonly Version MinimumVersion = new(0, 2, 0);

    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = SemVer.Match(text);
        return m.Success ? new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)) : null;
    }

    public static KlingReply? ParseReply(string? stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout)) return null;
        var text = stdout.Trim();
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            using var doc = JsonDocument.Parse(text[start..(end + 1)]);
            var root = doc.RootElement.Clone();
            if (root.ValueKind != JsonValueKind.Object) return null;
            var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
            var status = root.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.Number && st.TryGetInt32(out var s) ? s : (ok ? 200 : 0);
            JsonElement? body = root.TryGetProperty("body", out var b) ? b : null;
            return new KlingReply(ok, status, body, text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool LooksLikeNetworkFailure(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var t = text.ToLowerInvariant();
        return t.Contains("fetch failed") || t.Contains("connect timeout") || t.Contains("und_err_") || t.Contains("econnrefused") ||
               t.Contains("enotfound") || t.Contains("etimedout") || t.Contains("econnreset") || t.Contains("network error");
    }

    public static bool LooksLikeNotLoggedIn(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var t = text.ToLowerInvariant();
        return t.Contains("not logged in") || t.Contains("kling login") || t.Contains("unauthorized") ||
               t.Contains("login required") || t.Contains("no credentials") || t.Contains("401") || t.Contains("please login") || t.Contains("please log in");
    }
}

/// <summary>Finds and runs the official Kling CLI (<c>@klingai/cli-global</c>), installed by the owner with npm.</summary>
public sealed class KlingCli
{
    public const string GlobalPackage = "@klingai/cli-global";
    public const string CnPackage = "@klingai/cli-cn";
    public const string NpmRegistry = "https://registry.npmjs.org";

    public static string PackageFor(string region) => region == "cn" ? CnPackage : GlobalPackage;
    public static string InstallCommand(string region) => $"npm i -g {PackageFor(region)} --registry={NpmRegistry}";
    public const string LoginCommand = "kling login";

    public static KlingLocation Locate(KlingSettings settings)
    {
        string? cli = null, node = null, source = "";

        if (!string.IsNullOrWhiteSpace(settings.CliPath) && File.Exists(settings.CliPath))
        {
            cli = settings.CliPath; source = "settings";
        }
        if (cli is null && OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            foreach (var candidate in new[]
                     {
                         Path.Combine(appData, "npm", "kling.cmd"),
                         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "pnpm", "kling.cmd"),
                         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "kling.cmd"),
                     })
                if (File.Exists(candidate)) { cli = candidate; source = "npm-global"; break; }
        }
        cli ??= FindOnPath(OperatingSystem.IsWindows() ? new[] { "kling.cmd", "kling.exe", "kling" } : new[] { "kling" }, ref source);

        if (!string.IsNullOrWhiteSpace(settings.NodePath) && File.Exists(settings.NodePath)) node = settings.NodePath;
        var nodeSource = "";
        node ??= FindOnPath(OperatingSystem.IsWindows() ? new[] { "node.exe" } : new[] { "node" }, ref nodeSource);
        if (node is null && OperatingSystem.IsWindows())
        {
            foreach (var candidate in new[]
                     {
                         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"),
                         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "nodejs", "node.exe"),
                     })
                if (File.Exists(candidate)) { node = candidate; break; }
        }
        return new KlingLocation(cli, node, source);
    }

    private static string? FindOnPath(string[] names, ref string source)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                try
                {
                    var full = Path.Combine(dir.Trim('"'), name);
                    if (File.Exists(full)) { source = "PATH"; return full; }
                }
                catch (ArgumentException) { }
            }
        }
        return null;
    }

    /// <summary>Runs the CLI hidden (no console window) and captures its JSON output. Never blocks on stdin.</summary>
    public static async Task<CliResult> RunAsync(string cliPath, IEnumerable<string> args, TimeSpan timeout, ProxySettings? proxy = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetTempPath()
        };
        var argList = args.ToList();
        if (OperatingSystem.IsWindows() && cliPath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            psi.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            psi.Arguments = "/d /s /c \"\"" + cliPath + "\" " + string.Join(" ", argList.Select(QuoteArg)) + "\"";
        }
        else
        {
            psi.FileName = cliPath;
            foreach (var a in argList) psi.ArgumentList.Add(a);
        }
        psi.Environment["NO_COLOR"] = "1";
        psi.Environment["CI"] = "1";
        ApplyProxyEnv(psi, proxy);

        var sw = Stopwatch.StartNew();
        using var process = new Process { StartInfo = psi };
        process.Start();
        process.StandardInput.Close();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = !ct.IsCancellationRequested;
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            if (!timedOut) throw;
        }
        string stdout = "", stderr = "";
        try { stdout = await stdoutTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        try { stderr = await stderrTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        var exit = timedOut ? -1 : process.ExitCode;
        return new CliResult(exit, stdout, stderr, timedOut, sw.ElapsedMilliseconds);
    }

    private static void ApplyProxyEnv(ProcessStartInfo psi, ProxySettings? proxy)
    {
        foreach (var (key, value) in ChildEnvironment(proxy))
        {
            if (value is null) psi.Environment.Remove(key);
            else psi.Environment[key] = value;
        }
    }

    public static readonly Uri KlingEndpoint = new("https://kling.ai/");
    private static readonly string[] ProxyVariables = { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy" };

    /// <summary>
    /// Environment for every CLI child process (hidden runs and the visible login console). The CLI itself has no proxy
    /// support and Node's fetch ignores the Windows system proxy, so the proxy the card resolved to is handed over via
    /// <c>HTTPS_PROXY</c> plus <c>NODE_USE_ENV_PROXY=1</c> (honoured by Node ≥ 22.21 / ≥ 24). A null value means "remove".
    /// </summary>
    public static IReadOnlyDictionary<string, string?> ChildEnvironment(ProxySettings? proxy)
    {
        var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["NO_COLOR"] = "1" };
        var mode = (proxy?.Mode ?? "system").Trim().ToLowerInvariant();
        var url = ChildProcessProxy.Resolve(proxy, KlingEndpoint, out _);
        if (mode == "none" || url is null)
        {
            if (mode == "none") foreach (var k in ProxyVariables) env[k] = null;
            return env;
        }
        foreach (var k in ProxyVariables) env[k] = null;
        env["HTTP_PROXY"] = url;
        env["HTTPS_PROXY"] = url;
        env["NO_PROXY"] = "127.0.0.1,localhost";
        env["NODE_USE_ENV_PROXY"] = "1";
        return env;
    }

    /// <summary>Node ≥ 22.21 or ≥ 24 route fetch() through HTTPS_PROXY when NODE_USE_ENV_PROXY=1 is set.</summary>
    public static bool NodeSupportsEnvProxy(Version? node) =>
        node is not null && (node.Major >= 24 || (node.Major == 22 && node.Minor >= 21));

    /// <summary>Runs <c>node --version</c> hidden; null when Node is missing or does not answer quickly.</summary>
    public static async Task<Version?> NodeVersionAsync(string? nodePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nodePath) || !File.Exists(nodePath)) return null;
        try
        {
            var r = await RunAsync(nodePath, new[] { "--version" }, TimeSpan.FromSeconds(10), new ProxySettings { Mode = "none" }, ct).ConfigureAwait(false);
            return KlingOutput.ParseVersion(r.StdOut) ?? KlingOutput.ParseVersion(r.StdErr);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <summary>Everything the settings card shows about the local CLI environment (no Kling network calls).</summary>
    public static async Task<KlingEnvironment> InspectAsync(KlingSettings settings, CancellationToken ct = default)
    {
        var loc = Locate(settings);
        var node = await NodeVersionAsync(loc.NodePath, ct).ConfigureAwait(false);
        var proxyUrl = ChildProcessProxy.Resolve(settings.Proxy, KlingEndpoint, out var proxySource);
        return new KlingEnvironment(loc, node, proxyUrl, proxySource, NodeSupportsEnvProxy(node));
    }

    private static string QuoteArg(string a) => a.Any(char.IsWhiteSpace) || a.Contains('"') ? "\"" + a.Replace("\"", "\\\"") + "\"" : a;

    /// <summary>Version → identity → account (all read-only, no credits consumed).</summary>
    public static Task<ToolCheckResult> CheckAsync(KlingSettings settings, CancellationToken ct = default) =>
        ToolCheckResult.Timed(async () =>
        {
            var loc = Locate(settings);
            if (!loc.CliFound)
            {
                var node = loc.NodeFound ? "Node پیدا شد" : "Node هم در PATH پیدا نشد";
                return new ToolCheckResult(ToolState.NotConfigured, "Kling CLI نصب نیست — از دکمهٔ «نصب CLI» استفاده کنید",
                    $"cli=missing node={(loc.NodePath ?? "missing")} ({node})", "cli_missing");
            }

            var ver = await RunAsync(loc.CliPath!, new[] { "--version" }, TimeSpan.FromSeconds(40), settings.Proxy, ct).ConfigureAwait(false);
            if (ver.TimedOut)
                return new ToolCheckResult(ToolState.Error, "Kling CLI پاسخ نداد (زمان تمام شد)", $"cli={loc.CliPath} --version timeout", "cli_timeout");
            var version = KlingOutput.ParseVersion(ver.StdOut) ?? KlingOutput.ParseVersion(ver.StdErr);
            if (ver.ExitCode != 0 && version is null)
                return new ToolCheckResult(ToolState.Error, "اجرای Kling CLI ناموفق بود", $"cli={loc.CliPath} exit={ver.ExitCode} err={ToolCheckResult.Trim(ver.StdErr.Trim(), 300)}", "cli_exec");
            if (version is not null && version < KlingOutput.MinimumVersion)
                return new ToolCheckResult(ToolState.Error, $"نسخهٔ Kling CLI قدیمی است ({version}) — دوباره نصب کنید", $"version={version} min={KlingOutput.MinimumVersion}", "cli_old");

            var nodeVersion = await NodeVersionAsync(loc.NodePath, ct).ConfigureAwait(false);
            var proxyUrl = ChildProcessProxy.Resolve(settings.Proxy, KlingEndpoint, out var proxySource);
            var proxyNote = proxyUrl is null ? "proxy=direct" : $"proxy={proxyUrl} ({proxySource}) node={nodeVersion?.ToString() ?? "?"} env-proxy={(NodeSupportsEnvProxy(nodeVersion) ? "supported" : "unsupported")}";

            var who = await RunAsync(loc.CliPath!, new[] { "who_am_i" }, TimeSpan.FromSeconds(60), settings.Proxy, ct).ConfigureAwait(false);
            if (who.TimedOut)
                return new ToolCheckResult(ToolState.NetworkError, "Kling پاسخ نداد (زمان تمام شد)" + ProxyHint(proxyUrl, nodeVersion), $"who_am_i timeout {proxyNote}", "timeout");
            var reply = KlingOutput.ParseReply(who.StdOut);
            var combined = who.StdOut + "\n" + who.StdErr;
            if (reply is null || !reply.Ok)
            {
                var status = reply?.Status ?? 0;
                if (status is 401 or 403 || KlingOutput.LooksLikeNotLoggedIn(combined))
                    return new ToolCheckResult(ToolState.NeedsLogin, "وارد حساب Kling نشده‌اید — دکمهٔ «ورود» را بزنید",
                        $"version={version} who_am_i status={status} exit={who.ExitCode} {proxyNote}", "needs_login");
                if (status >= 500 || status == 0 && who.ExitCode != 0 || KlingOutput.LooksLikeNetworkFailure(combined))
                    return new ToolCheckResult(ToolState.NetworkError, "ارتباط CLI با سرویس Kling برقرار نشد" + ProxyHint(proxyUrl, nodeVersion),
                        $"version={version} who_am_i status={status} exit={who.ExitCode} {proxyNote} out={ToolCheckResult.Trim(combined.Trim(), 300)}", "kling_unreachable");
                return new ToolCheckResult(ToolState.Error, "پاسخ Kling قابل تفسیر نبود",
                    $"version={version} exit={who.ExitCode} out={ToolCheckResult.Trim(combined.Trim(), 300)}", "kling_parse");
            }

            var user = reply.FindString("username", "userName", "nickname", "name", "email", "userId", "user_id") ?? "";
            var models = reply.FindStringArray("models");

            string credits = "";
            var acc = await RunAsync(loc.CliPath!, new[] { "account" }, TimeSpan.FromSeconds(60), settings.Proxy, ct).ConfigureAwait(false);
            var accReply = KlingOutput.ParseReply(acc.StdOut);
            if (accReply is { Ok: true })
                credits = accReply.FindString("availableRemainCredits", "available_remain_credits", "remainCredits", "credits") ?? "";

            var summary = "متصل" + (string.IsNullOrEmpty(user) ? "" : $" ({user})") +
                          (string.IsNullOrEmpty(credits) ? "" : $" — اعتبار باقی‌مانده: {credits}");
            var detail = $"version={version} cli={loc.CliPath} node={loc.NodePath ?? "PATH"} models={models.Count} credits={credits} {proxyNote}";
            return new ToolCheckResult(ToolState.Connected, summary, detail);
        }, "kling");

    /// <summary>Actionable Persian hint when the CLI cannot reach Kling although a proxy exists on this machine.</summary>
    public static string ProxyHint(string? proxyUrl, Version? node)
    {
        if (proxyUrl is null) return " — اگر برای اینترنت از پراکسی/فیلترشکن استفاده می‌کنید، حالت پراکسی این کارت را «سیستم» یا «سفارشی» بگذارید یا حالت TUN را روشن کنید";
        if (!NodeSupportsEnvProxy(node))
            return $" — پراکسی {proxyUrl} پیدا شد ولی Node {(node is null ? "" : "نسخهٔ " + node)} نمی‌تواند از آن استفاده کند؛ Node را به نسخهٔ ۲۴ به‌روز کنید (nodejs.org) یا حالت TUN فیلترشکن را روشن کنید";
        return $" — از پراکسی {proxyUrl} استفاده شد؛ اگر باز هم وصل نشد، پراکسی را بررسی کنید";
    }
}

/// <summary>Result of <see cref="KlingCli.InspectAsync"/>: local facts for the settings card, never secrets.</summary>
public sealed record KlingEnvironment(KlingLocation Location, Version? NodeVersion, string? ProxyUrl, string ProxySource, bool NodeSupportsEnvProxy)
{
    /// <summary>One-line, LTR, monospace-friendly summary.</summary>
    public string Describe()
    {
        var cli = Location.CliFound ? "CLI: " + Location.CliPath : "CLI: not found";
        var node = Location.NodeFound ? "Node: " + Location.NodePath + (NodeVersion is null ? "" : $" ({NodeVersion})") : "Node: not found";
        var proxy = ProxyUrl is null ? "proxy: direct" : $"proxy: {ProxyUrl} ({ProxySource})";
        return cli + "   |   " + node + "   |   " + proxy;
    }

    /// <summary>Persian warning shown under the summary when the proxy cannot be used by this Node; empty otherwise.</summary>
    public string Warning()
    {
        if (ProxyUrl is null || !Location.NodeFound) return "";
        if (!NodeSupportsEnvProxy)
            return $"روی این رایانه پراکسی {ProxyUrl} فعال است، اما Node {(NodeVersion is null ? "" : "نسخهٔ " + NodeVersion)} نمی‌تواند آن را به کار ببرد؛ در نتیجه ورود و فرمان‌های Kling مستقیم وصل می‌شوند و ممکن است شکست بخورند. راه‌حل: Node را به نسخهٔ ۲۴ به‌روز کنید (nodejs.org) یا حالت TUN فیلترشکن را روشن کنید.";
        return $"فرمان‌های Kling از پراکسی {ProxyUrl} عبور می‌کنند.";
    }
}

/// <summary>Resolves the proxy URL a child process should use for a target, from the card's proxy setting.</summary>
public static class ChildProcessProxy
{
    public static string? Resolve(ProxySettings? proxy, Uri target, out string source)
    {
        var mode = (proxy?.Mode ?? "system").Trim().ToLowerInvariant();
        source = mode;
        switch (mode)
        {
            case "none":
                return null;
            case "custom":
                return Uri.TryCreate(proxy?.Url?.Trim(), UriKind.Absolute, out var custom) ? custom.ToString().TrimEnd('/') : null;
            default:
                source = "system";
                try
                {
                    var sys = HttpClient.DefaultProxy;
                    if (sys.IsBypassed(target)) return null;
                    var uri = sys.GetProxy(target);
                    if (uri is null || uri == target) return null;
                    return uri.ToString().TrimEnd('/');
                }
                catch (Exception ex) when (ex is PlatformNotSupportedException or InvalidOperationException or System.Net.WebException)
                {
                    return null;
                }
        }
    }
}
