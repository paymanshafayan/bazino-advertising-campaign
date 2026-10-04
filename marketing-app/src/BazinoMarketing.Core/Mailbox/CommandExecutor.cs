using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using BazinoMarketing.Core.Browser;
using BazinoMarketing.Core.Diagnostics;
using BazinoMarketing.Core.Logging;
using BazinoMarketing.Core.Media;
using BazinoMarketing.Core.Secrets;
using BazinoMarketing.Core.Settings;
using BazinoMarketing.Core.Tools;
using BazinoMarketing.Core.Update;

namespace BazinoMarketing.Core.Mailbox;

/// <summary>A verified command about to be executed. <c>Args</c> is always an object (empty when the agent sent none).</summary>
public sealed record CommandContext(string Cmd, JsonElement Args, string Risk, string Note, string RequestId, string Session, string AgentFingerprint)
{
    public string? Str(string name) =>
        Args.ValueKind == JsonValueKind.Object && Args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public bool Bool(string name, bool fallback) =>
        Args.ValueKind == JsonValueKind.Object && Args.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;

    public int Int(string name, int fallback, int min, int max)
    {
        if (Args.ValueKind == JsonValueKind.Object && Args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n))
            return Math.Clamp(n, min, max);
        return fallback;
    }

    public double Number(string name, double fallback, double min, double max)
    {
        if (Args.ValueKind == JsonValueKind.Object && Args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) && double.IsFinite(n))
            return Math.Clamp(n, min, max);
        return fallback;
    }

    public List<string> StrList(string name)
    {
        var list = new List<string>();
        if (Args.ValueKind == JsonValueKind.Object && Args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array)
            foreach (var e in v.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String) list.Add(e.GetString() ?? "");
        return list;
    }

    public JsonObject? Obj(string name) =>
        Args.ValueKind == JsonValueKind.Object && Args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object
            ? JsonNode.Parse(v.GetRawText()) as JsonObject : null;
}

/// <summary>Hooks the desktop app plugs in; everything has a Core default so the executor is testable without WPF.</summary>
public sealed class CommandExecutorOptions
{
    public required Func<AppSettings> GetSettings { get; init; }
    public required ISecretStore Secrets { get; init; }
    public required JsonlLogStore Log { get; init; }
    public required Redactor Redactor { get; init; }
    public required string AppVersion { get; init; }
    public string DataFolder { get; init; } = "";
    /// <summary>Replaces + saves the settings and refreshes the UI. Default: no-op that reports failure.</summary>
    public Func<AppSettings, Task<string?>> ApplySettings { get; init; } = _ => Task.FromResult<string?>("apply_settings_unavailable");
    /// <summary>Human-readable state of every tool card ("title" → "state — summary").</summary>
    public Func<IReadOnlyDictionary<string, string>> ToolStates { get; init; } = () => new Dictionary<string, string>();
    /// <summary>Runs the checks of the given tools (ids: github, kling, zernio, flux, custom:&lt;id&gt; or all) — the app routes this through the cards so the UI updates.</summary>
    public Func<string, CancellationToken, Task<IReadOnlyDictionary<string, ToolCheckResult>>>? CheckTools { get; init; }
    /// <summary>Runs the legacy import and returns its summary. Default: unavailable.</summary>
    public Func<bool, Task<string>> RunLegacyImport { get; init; } = _ => Task.FromResult("import_unavailable");
    /// <summary>Swaps the running EXE for the verified download and restarts (after the reply has been written). Returns an error text or null.</summary>
    public Func<string, TimeSpan, Task<string?>> RestartWith { get; init; } = (_, _) => Task.FromResult<string?>("restart_unavailable");
    /// <summary>Restarts the current EXE as-is (after the reply has been written).</summary>
    public Func<TimeSpan, Task<string?>> Restart { get; init; } = _ => Task.FromResult<string?>("restart_unavailable");
    /// <summary>Where verified downloads are kept.</summary>
    public string DownloadFolder { get; init; } = Path.Combine(Path.GetTempPath(), "BazinoMarketing-downloads");
    /// <summary>The in-app browser bridge (the agent's connection to the owner's Chrome). Default: browser commands report as unavailable.</summary>
    public Func<BrowserBridge>? Browser { get; init; }
    /// <summary>Local media folder, official Instaloader, FFmpeg and the existing GitHub connection.</summary>
    public MediaService? Media { get; init; }
}

/// <summary>
/// Executes verified mailbox commands immediately. By the owner's explicit decision (2026-09-27) nothing asks for his
/// confirmation: while the app runs, the agent may do anything the app can do; the owner's control is closing the app.
/// </summary>
public sealed class CommandExecutor
{
    private readonly CommandExecutorOptions _o;

    public CommandExecutor(CommandExecutorOptions options) => _o = options;

    public async Task<object?> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        return ctx.Cmd switch
        {
            Commands.Ping => Ping(),
            Commands.AppUpdate => await AppUpdateAsync(ctx, ct).ConfigureAwait(false),
            Commands.AppRestart => await AppRestartAsync(ctx).ConfigureAwait(false),
            Commands.Diag => await DiagAsync(ctx, ct).ConfigureAwait(false),
            Commands.ToolCheck => await ToolCheckAsync(ctx, ct).ConfigureAwait(false),
            Commands.KlingCli => await KlingCliAsync(ctx, ct).ConfigureAwait(false),
            Commands.LogTail => LogTail(ctx),
            Commands.SettingsGet => SettingsGet(),
            Commands.SettingsSet => await SettingsSetAsync(ctx, ct).ConfigureAwait(false),
            Commands.SecretStatus => SecretStatus(),
            Commands.SecretSet => SecretSet(ctx),
            Commands.ImportLegacy => await ImportLegacyAsync(ctx, ct).ConfigureAwait(false),
            Commands.OsRun => await OsRunAsync(ctx, ct).ConfigureAwait(false),
            Commands.ZernioAccounts => await ZernioAccountsAsync(ctx, ct).ConfigureAwait(false),
            Commands.ZernioPosts => await ZernioPostsAsync(ctx, ct).ConfigureAwait(false),
            Commands.ZernioCommentAutomationCreate => await ZernioCommentAutomationCreateAsync(ctx, ct).ConfigureAwait(false),
            Commands.BrowserStatus or Commands.BrowserLaunch or Commands.BrowserTargets or Commands.BrowserSelect
                or Commands.BrowserOpen or Commands.BrowserNavigate or Commands.BrowserRead or Commands.BrowserEval
                or Commands.BrowserClose => await BrowserAsync(ctx, ct).ConfigureAwait(false),
            Commands.InstagramDownload => await InstagramDownloadAsync(ctx, ct).ConfigureAwait(false),
            Commands.MediaList => MediaList(ctx),
            Commands.MediaUpload => await MediaUploadAsync(ctx, ct).ConfigureAwait(false),
            Commands.MediaFfmpeg => await MediaFfmpegAsync(ctx, ct).ConfigureAwait(false),
            Commands.MediaTranscribe => await MediaTranscribeAsync(ctx, ct).ConfigureAwait(false),
            _ => throw new CommandException("unknown_command", $"'{ctx.Cmd}' is not a known command")
        };
    }

    private object Ping() => new
    {
        pong = true,
        version = _o.AppVersion,
        time = DateTimeOffset.UtcNow,
        os = Environment.OSVersion.ToString(),
        runtime = Environment.Version.ToString(),
        dataFolder = _o.DataFolder
    };

    private async Task<object> DiagAsync(CommandContext ctx, CancellationToken ct)
    {
        var settings = _o.GetSettings();
        string? probeText = null;
        if (ctx.Bool("probe", true))
        {
            try
            {
                var probes = await ConnectivityProbe.RunAsync(settings.GitHub.Proxy, ct).ConfigureAwait(false);
                probeText = ConnectivityProbe.Format(probes);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                probeText = "probe failed: " + ex.Message;
            }
        }
        var recent = _o.Log.Read(new LogQuery { Limit = ctx.Int("logLimit", 40, 0, 300) });
        var text = DiagnosticsBundle.Build(_o.AppVersion, settings, _o.ToolStates(), recent, _o.Redactor, probeText, _o.DataFolder);

        object? kling = null;
        try
        {
            var env = await KlingCli.InspectAsync(settings.Kling, ct).ConfigureAwait(false);
            kling = new
            {
                cliFound = env.Location.CliFound,
                cliPath = env.Location.CliPath,
                nodeFound = env.Location.NodeFound,
                nodePath = env.Location.NodePath,
                nodeVersion = env.NodeVersion?.ToString(),
                proxyUrl = env.ProxyUrl,
                proxySource = env.ProxySource,
                nodeSupportsEnvProxy = env.NodeSupportsEnvProxy,
                describe = env.Describe(),
                warning = env.Warning()
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            kling = new { error = ex.Message };
        }
        return new { text = _o.Redactor.Redact(text), kling, secrets = SecretStatus() };
    }

    private async Task<object> ToolCheckAsync(CommandContext ctx, CancellationToken ct)
    {
        var selector = (ctx.Str("tool") ?? "all").Trim().ToLowerInvariant();
        IReadOnlyDictionary<string, ToolCheckResult> results;
        if (_o.CheckTools is not null)
            results = await _o.CheckTools(selector, ct).ConfigureAwait(false);
        else
            results = await CheckWithCoreClientsAsync(selector, ct).ConfigureAwait(false);
        if (results.Count == 0) throw new CommandException("unknown_tool", $"no tool matches '{selector}'");
        return results.ToDictionary(kv => kv.Key, kv => (object)new
        {
            state = kv.Value.State.ToString(),
            ok = kv.Value.IsOk,
            summary = kv.Value.Summary,
            detail = _o.Redactor.Redact(kv.Value.Detail),
            errorCode = kv.Value.ErrorCode,
            durationMs = kv.Value.DurationMs
        });
    }

    /// <summary>Fallback used by tests/headless hosts: the same checks the cards run, without the UI.</summary>
    private async Task<IReadOnlyDictionary<string, ToolCheckResult>> CheckWithCoreClientsAsync(string selector, CancellationToken ct)
    {
        var s = _o.GetSettings();
        var all = selector == "all";
        var results = new Dictionary<string, ToolCheckResult>(StringComparer.Ordinal);
        if (all || selector == "github")
            results["github"] = await GitHubClient.CheckAsync(s.GitHub, _o.Secrets.GetOrEmpty(SecretKeys.GitHubToken), ct).ConfigureAwait(false);
        if (all || selector == "zernio")
            results["zernio"] = await ZernioClient.CheckAsync(s.Zernio, _o.Secrets.GetOrEmpty(SecretKeys.ZernioApiKey), ct).ConfigureAwait(false);
        if (all || selector == "flux")
            results["flux"] = await FluxClient.CheckAsync(s.Flux, _o.Secrets.GetOrEmpty(SecretKeys.FluxApiKey), ct).ConfigureAwait(false);
        if (all || selector == "groq")
            results["groq"] = await GroqClient.CheckAsync(s.Groq, _o.Secrets.GetOrEmpty(SecretKeys.GroqApiKey), ct).ConfigureAwait(false);
        if (all || selector == "kling")
            results["kling"] = await KlingCli.CheckAsync(s.Kling, ct).ConfigureAwait(false);
        foreach (var card in s.CustomCards)
            if (all || selector == "custom:" + card.Id.ToLowerInvariant())
                results["custom:" + card.Id] = await CustomCardClient.CheckAsync(card, _o.Secrets.GetOrEmpty(SecretKeys.ForCustomCredential(card.Id)), ct).ConfigureAwait(false);
        return results;
    }

    private async Task<object> KlingCliAsync(CommandContext ctx, CancellationToken ct)
    {
        var args = ctx.StrList("args");
        if (args.Count == 0) throw new CommandException("bad_args", "args[] is required");
        if (args.Count > 64) throw new CommandException("bad_args", "too many arguments");
        foreach (var a in args)
            if (a.Contains('\n') || a.Contains('\r')) throw new CommandException("bad_args", "arguments must be single-line");

        var settings = _o.GetSettings();
        var location = KlingCli.Locate(settings.Kling);
        if (!location.CliFound) throw new CommandException("cli_missing", "kling CLI not found (install it from the Kling card)");
        var timeout = TimeSpan.FromSeconds(ctx.Int("timeoutSec", 60, 5, 900));
        var result = await KlingCli.RunAsync(location.CliPath!, args, timeout, settings.Kling.Proxy, ct).ConfigureAwait(false);
        var reply = KlingOutput.ParseReply(result.StdOut);
        return new
        {
            cli = location.CliPath,
            exitCode = result.ExitCode,
            timedOut = result.TimedOut,
            durationMs = result.DurationMs,
            stdout = Tail(_o.Redactor.Redact(result.StdOut), 12_000),
            stderr = Tail(_o.Redactor.Redact(result.StdErr), 4_000),
            parsed = reply is null ? null : new { ok = reply.Ok, status = reply.Status }
        };
    }

    /// <summary>Read-only: every social account the owner's Zernio key can see, with platform, username and status.</summary>
    private async Task<object> ZernioAccountsAsync(CommandContext ctx, CancellationToken ct)
    {
        var settings = _o.GetSettings();
        var connection = ResolveZernioConnection(settings, ctx.Str("card"));
        var reply = await ZernioClient.ListAccountsAsync(connection.Settings, connection.ApiKey, ct).ConfigureAwait(false);
        if (!reply.Ok) throw new CommandException("zernio_failed", reply.Error ?? "خواندن فهرست حساب‌های Zernio ممکن نشد");
        var node = reply.Body ?? new JsonObject();
        node["connection"] = DescribeConnection(connection);
        _o.Log.Append(LogLevel.Info, "zernio", ctx.Cmd, "ایجنت فهرست حساب‌های Zernio را خواند",
            connection.Label + " — " + _o.Redactor.Redact(node.ToJsonString()));
        return node;
    }

    /// <summary>
    /// Read-only: the posts the owner's Zernio key can see. <c>source=external</c> reads the account's own history
    /// (the posts already published on Instagram); <c>source=zernio</c> reads what was authored inside Zernio,
    /// drafts included. Nothing is created, changed or deleted.
    /// </summary>
    private async Task<object> ZernioPostsAsync(CommandContext ctx, CancellationToken ct)
    {
        var settings = _o.GetSettings();
        var connection = ResolveZernioConnection(settings, ctx.Str("card"));
        var query = new ZernioPostQuery(
            ctx.Str("source"),
            ctx.Str("status"),
            ctx.Int("limit", 25, 1, 100),
            ctx.Int("page", 1, 1, 1000),
            ctx.Str("search"),
            ctx.Str("accountId"));
        var reply = await ZernioClient.ListPostsAsync(connection.Settings, connection.ApiKey, query, ct).ConfigureAwait(false);
        if (!reply.Ok) throw new CommandException("zernio_failed", reply.Error ?? "خواندن فهرست پست‌های Zernio ممکن نشد");
        var node = reply.Body ?? new JsonObject();
        node["connection"] = DescribeConnection(connection);
        _o.Log.Append(LogLevel.Info, "zernio", ctx.Cmd, "ایجنت فهرست پست‌های Zernio را خواند",
            connection.Label + " — " + _o.Redactor.Redact(node.ToJsonString()));
        return node;
    }

    private Task<object> ZernioCommentAutomationCreateAsync(CommandContext ctx, CancellationToken ct) =>
        throw new CommandException("approval_required",
            "اتوماسیون comment-to-DM فقط در مسیر صف انتشارِ کارت DM تأییدشده و پس از انتشار موفق ساخته می‌شود؛ فرمان مستقیم برای دورزدن تأیید صف غیرفعال است.");

    /// <summary>
    /// Which Zernio connection a read command should talk to. The owner may register the same service more than once
    /// (the built-in Zernio card, plus any number of custom cards), each with its own key. <c>card</c> picks one:
    /// empty or "zernio" is the built-in card; anything else is matched against the custom cards by id, slug or name.
    /// </summary>
    private ZernioConnection ResolveZernioConnection(AppSettings settings, string? card)
    {
        var wanted = (card ?? "").Trim();
        if (wanted.Length == 0 ||
            string.Equals(wanted, "zernio", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(wanted, "main", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(wanted, "builtin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(wanted, "default", StringComparison.OrdinalIgnoreCase))
        {
            return new ZernioConnection(settings.Zernio, _o.Secrets.GetOrEmpty(SecretKeys.ZernioApiKey),
                "zernio", "Zernio (کارت اصلی)", settings.Zernio.BaseUrl);
        }

        var id = wanted.StartsWith("custom:", StringComparison.OrdinalIgnoreCase) ? wanted["custom:".Length..] : wanted;
        var match = settings.CustomCards.FirstOrDefault(c =>
            string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals((c.Name ?? "").Trim(), id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(CustomCardIds.FromName(c.Name), id, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            throw new CommandException("unknown_card",
                $"اتصال «{card}» پیدا نشد. اتصال‌های موجود با فرمان settings.get دیده می‌شوند.");

        var key = _o.Secrets.GetOrEmpty(SecretKeys.ForCustomCredential(match.Id));
        if (string.IsNullOrWhiteSpace(key))
            throw new CommandException("no_credential", $"برای اتصال «{match.Name}» کلیدی ذخیره نشده است.");

        var zernio = new ZernioSettings { BaseUrl = match.BaseUrl, Proxy = match.Proxy };
        return new ZernioConnection(zernio, key, "custom:" + match.Id, match.Name ?? match.Id, match.BaseUrl);
    }

    private static JsonObject DescribeConnection(ZernioConnection c) => new()
    {
        ["id"] = c.Id,
        ["name"] = c.Name,
        ["baseUrl"] = c.BaseUrl,
    };

    /// <summary>One resolved Zernio endpoint: settings, key and how to name it back to the owner.</summary>
    private sealed record ZernioConnection(ZernioSettings Settings, string ApiKey, string Id, string Name, string BaseUrl)
    {
        public string Label => $"اتصال {Name} ({Id})";
    }

    private MediaService RequireMedia() => _o.Media ?? throw new CommandException("media_unavailable", "this host has no media studio");

    private async Task<object> InstagramDownloadAsync(CommandContext ctx, CancellationToken ct)
    {
        var media = RequireMedia();
        var url = ctx.Str("url");
        if (string.IsNullOrWhiteSpace(url))
        {
            var browser = _o.Browser?.Invoke();
            if (browser is null) throw new CommandException("browser_unavailable", "no browser bridge is connected and no post URL was supplied");
            var page = await browser.ReadAsync(_o.GetSettings().Browser.MaxReadChars, ct).ConfigureAwait(false);
            if (!page.Ok || page.Value is null)
                throw new CommandException("instagram_url_unavailable", page.Error ?? "select the Instagram post tab or supply its URL");
            url = page.Value["url"]?.GetValue<string>();
        }

        var result = await media.DownloadInstagramAsync(url, ct).ConfigureAwait(false);
        if (result.Ok && result.Files is { Count: > 0 } && ctx.Bool("transcribe", true))
        {
            var videoOrAudio = result.Files.FirstOrDefault(f => f.IsVideo || f.IsAudio);
            if (videoOrAudio is not null)
            {
                try
                {
                    if (ctx.Bool("upload", false))
                    {
                        var up = await media.UploadToGitHubAsync(videoOrAudio.RelativePath, ct).ConfigureAwait(false);
                        if (up.Ok)
                        {
                            result = result with
                            {
                                Url = up.Url,
                                Transcript = up.Transcript,
                                TranscriptLanguage = up.TranscriptLanguage,
                                TranscriptProvider = up.TranscriptProvider,
                                TranscriptUrl = up.TranscriptUrl
                            };
                        }
                    }
                    else
                    {
                        var tr = await media.TranscribeAsync(
                            videoOrAudio.RelativePath,
                            ctx.Str("language"),
                            ctx.Str("prompt"),
                            ctx.Bool("translate", false),
                            uploadToGitHub: ctx.Bool("uploadTranscript", true),
                            ct: ct).ConfigureAwait(false);
                        if (tr.Ok)
                        {
                            result = result with
                            {
                                Transcript = tr.Text,
                                TranscriptLanguage = tr.Language,
                                TranscriptProvider = tr.Provider,
                                TranscriptUrl = tr.UploadedUrl
                            };
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Best-effort auto-transcribe on download
                }
            }
        }
        _o.Log.Append(result.Ok ? LogLevel.Success : LogLevel.Error, "media", ctx.Cmd,
            result.Message, result.Code, result.DurationMs, result.Ok ? null : result.Code, result.Ok ? "ok" : "failed");
        return result;
    }

    private object MediaList(CommandContext ctx)
    {
        var media = RequireMedia();
        var files = media.ListFiles(ctx.Int("limit", 100, 1, 250));
        return new
        {
            folder = media.CurrentFolder,
            count = files.Count,
            files = files.Select(f => new
            {
                path = f.RelativePath,
                name = f.Name,
                extension = f.Extension,
                sizeBytes = f.SizeBytes,
                modifiedUtc = f.ModifiedUtc,
                isMedia = f.IsMedia,
                isVideo = f.IsVideo,
                isAudio = f.IsAudio
            }).ToArray()
        };
    }

    private async Task<object> MediaUploadAsync(CommandContext ctx, CancellationToken ct)
    {
        var path = ctx.Str("file") ?? ctx.Str("relativePath") ?? "";
        if (string.IsNullOrWhiteSpace(path)) throw new CommandException("media_file_required", "supply a relative file path from media.list");
        var result = await RequireMedia().UploadToGitHubAsync(path, ct).ConfigureAwait(false);
        _o.Log.Append(result.Ok ? LogLevel.Success : LogLevel.Error, "media", ctx.Cmd,
            result.Message, result.RelativePath ?? result.Code, result.DurationMs, result.Ok ? null : result.Code, result.Ok ? "ok" : "failed");
        return result;
    }

    private async Task<object> MediaTranscribeAsync(CommandContext ctx, CancellationToken ct)
    {
        var media = RequireMedia();
        var path = ctx.Str("file") ?? ctx.Str("relativePath") ?? "latest";
        var language = ctx.Str("language");
        var prompt = ctx.Str("prompt");
        var translate = ctx.Bool("translate", false);
        var upload = ctx.Bool("upload", true);

        var result = await media.TranscribeAsync(path, language, prompt, translate, upload, ct: ct).ConfigureAwait(false);
        _o.Log.Append(result.Ok ? LogLevel.Success : LogLevel.Error, "media", ctx.Cmd,
            result.Message, result.RelativePath ?? result.Code, result.DurationMs, result.Ok ? null : result.Code, result.Ok ? "ok" : "failed");
        return result;
    }

    private async Task<object> MediaFfmpegAsync(CommandContext ctx, CancellationToken ct)
    {
        var media = RequireMedia();
        var operation = (ctx.Str("operation") ?? "").Trim().ToLowerInvariant();
        var file = ctx.Str("file") ?? ctx.Str("relativePath") ?? "";
        if (operation == "transcribe")
            return await MediaTranscribeAsync(ctx, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(file)) throw new CommandException("media_file_required", "supply a relative media file path from media.list");

        if (operation == "probe")
        {
            var info = await media.ProbeAsync(file, ct).ConfigureAwait(false);
            if (info is null) throw new CommandException("ffprobe_unavailable", "ffprobe.exe was not found or could not read the selected file");
            return new { ok = true, file, info };
        }

        MediaActionResult result = operation switch
        {
            "preview" => await media.CreatePreviewAsync(file, ctx.Number("seconds", 1, 0, 86400), ct).ConfigureAwait(false),
            "trim" => await media.TrimAsync(file, ctx.Number("start", 0, 0, 86400), ctx.Number("end", 15, 0, 86400), ct).ConfigureAwait(false),
            "convert" => await media.ConvertAsync(file, ctx.Str("format") ?? "mp4", ct).ConfigureAwait(false),
            _ => throw new CommandException("media_operation_invalid", "operation must be preview, probe, trim or convert")
        };
        _o.Log.Append(result.Ok ? LogLevel.Success : LogLevel.Error, "media", "ffmpeg." + operation,
            result.Message, result.RelativePath ?? result.Code, result.DurationMs, result.Ok ? null : result.Code, result.Ok ? "ok" : "failed");
        return result;
    }

    /// <summary>
    /// One entry point for every <c>browser.*</c> command. The bridge is the owner's Chrome on this PC, driven through
    /// the DevTools port. Any HTTP(S) host is permitted; attempts to read stored browser credentials remain refused.
    /// </summary>
    private async Task<object> BrowserAsync(CommandContext ctx, CancellationToken ct)
    {
        var bridge = _o.Browser?.Invoke();
        if (bridge is null) throw new CommandException("browser_unavailable", "this host has no browser bridge");

        var maxChars = ctx.Int("maxChars", 20000, 200, 200000);
        BrowserResult result = ctx.Cmd switch
        {
            Commands.BrowserStatus => await bridge.StatusAsync(ct).ConfigureAwait(false),
            Commands.BrowserLaunch => LaunchBrowser(bridge),
            Commands.BrowserTargets => await TargetsAsync(bridge, ct).ConfigureAwait(false),
            Commands.BrowserSelect => await bridge.SelectAsync(ctx.Str("targetId") ?? "", ct).ConfigureAwait(false),
            Commands.BrowserOpen => await bridge.OpenAsync(ctx.Str("url") ?? "", ct).ConfigureAwait(false),
            Commands.BrowserNavigate => await bridge.NavigateAsync(ctx.Str("url") ?? "", ct).ConfigureAwait(false),
            Commands.BrowserRead => await bridge.ReadAsync(maxChars, ct).ConfigureAwait(false),
            Commands.BrowserEval => await bridge.EvaluateAsync(ctx.Str("expression") ?? "", ct).ConfigureAwait(false),
            Commands.BrowserClose => await CloseBrowserAsync(bridge).ConfigureAwait(false),
            _ => BrowserResult.Fail("فرمان مرورگر ناشناخته است.")
        };

        if (!result.Ok) throw new CommandException("browser_refused", result.Error ?? "browser command failed");
        var node = result.Value ?? new JsonObject();
        // Page text and query-string URLs can contain private content or temporary credentials; never copy them to the log.
        _o.Log.Append(LogLevel.Info, "browser", ctx.Cmd, "فرمان مرورگر اجرا شد");
        return node;
    }

    private BrowserResult LaunchBrowser(BrowserBridge bridge)
    {
        if (!OperatingSystem.IsWindows()) return BrowserResult.Fail("باز کردن خودکار مرورگر فقط روی ویندوز ممکن است.");
        try
        {
            var process = bridge.TryLaunchChrome();
            return process is null
                ? BrowserResult.Fail("کروم روی این رایانه پیدا نشد. مسیر آن را در صفحهٔ مرورگر وارد کنید.")
                : BrowserResult.Good(new JsonObject { ["started"] = true, ["pid"] = process.Id });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return BrowserResult.Fail(ex.Message);
        }
    }

    private async Task<BrowserResult> TargetsAsync(BrowserBridge bridge, CancellationToken ct)
    {
        if (!await bridge.IsChromeRunningAsync(ct).ConfigureAwait(false))
            return BrowserResult.Fail("کروم با پورت اشکال‌یابی باز نیست. اول browser.launch را بفرستید.");
        var tabs = await bridge.ListTargetsAsync(ct).ConfigureAwait(false);
        return BrowserResult.Good(new JsonObject
        {
            ["count"] = tabs.Count,
            ["tabs"] = new JsonArray(tabs.Select(t => (JsonNode)new JsonObject
            {
                ["targetId"] = t.Id,
                ["title"] = t.Title,
                ["url"] = t.Url
            }).ToArray())
        });
    }

    private async Task<BrowserResult> CloseBrowserAsync(BrowserBridge bridge)
    {
        var id = bridge.SelectedTargetId;
        await bridge.DetachAsync().ConfigureAwait(false);
        return BrowserResult.Good(new JsonObject { ["detached"] = id is not null, ["targetId"] = id });
    }

    private object LogTail(CommandContext ctx)
    {
        var minutes = ctx.Int("minutes", 0, 0, 7 * 24 * 60);
        var q = new LogQuery
        {
            Limit = ctx.Int("limit", 80, 1, 400),
            Tool = string.IsNullOrWhiteSpace(ctx.Str("tool")) ? null : ctx.Str("tool"),
            OnlyFailures = ctx.Bool("failuresOnly", false),
            Text = string.IsNullOrWhiteSpace(ctx.Str("text")) ? null : ctx.Str("text"),
            From = minutes > 0 ? DateTimeOffset.Now.AddMinutes(-minutes) : null
        };
        var events = _o.Log.Read(q);
        return new
        {
            count = events.Count,
            lines = events.Select(e => _o.Redactor.Redact(JsonlLogStore.Format(e))).ToList(),
            file = _o.Log.CurrentFile
        };
    }

    private object SettingsGet()
    {
        var node = JsonSerializer.SerializeToNode(_o.GetSettings(), JsonUtil.Options);
        return new { settings = node, dataFolder = _o.DataFolder, version = _o.AppVersion };
    }

    private async Task<object> SettingsSetAsync(CommandContext ctx, CancellationToken ct)
    {
        var changes = ctx.Obj("changes") ?? throw new CommandException("bad_args", "args.changes must be an object");
        var current = _o.GetSettings();
        var target = JsonSerializer.SerializeToNode(current, JsonUtil.Options) as JsonObject
                     ?? throw new CommandException("internal", "settings could not be serialized");
        var changed = JsonMerge.Apply(target, changes);
        if (changed.Count == 0) return new { applied = false, changed, reason = "no_change" };

        AppSettings merged;
        try
        {
            merged = SettingsStore.Import(target.ToJsonString(JsonUtil.Options));
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            throw new CommandException("bad_args", "changes do not fit the settings schema: " + ex.Message);
        }
        if (!GitHubClient.IsValidRepository(merged.GitHub.Repository)) throw new CommandException("bad_args", "GitHub.Repository is not owner/name");
        if (merged.GitHub.PollSeconds < 3) merged.GitHub.PollSeconds = 3;

        _o.Log.Append(LogLevel.Info, "mailbox", "settings", $"ایجنت {changed.Count} مورد از تنظیمات را تغییر داد", string.Join("\n", changed));
        var error = await _o.ApplySettings(merged).ConfigureAwait(false);
        if (error is not null) throw new CommandException("apply_failed", error);
        return new { applied = true, changed };
    }

    private object SecretStatus()
    {
        var list = new List<object>();
        foreach (var key in _o.Secrets.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (key == SecretKeys.MailboxIdentity) continue;
            var value = _o.Secrets.GetOrEmpty(key);
            list.Add(new { key, present = value.Length > 0, length = value.Length, headerSafe = SecretFormat.IsHeaderSafe(value) });
        }
        return new { available = _o.Secrets.IsAvailable, reason = _o.Secrets.UnavailableReason, keys = list };
    }

    private object SecretSet(CommandContext ctx)
    {
        var key = (ctx.Str("key") ?? "").Trim();
        if (key.Length == 0) throw new CommandException("bad_args", "args.key is required");
        if (string.Equals(key, SecretKeys.MailboxIdentity, StringComparison.OrdinalIgnoreCase))
            throw new CommandException("forbidden", "mailbox.identity cannot be modified");

        var remove = ctx.Bool("remove", false);
        var value = (ctx.Str("value") ?? "").Trim();
        if (remove || value.Length == 0)
        {
            _o.Secrets.Remove(key);
            _o.Log.Append(LogLevel.Info, "mailbox", "secret.set", $"ایجنت کلید «{key}» را حذف کرد", key);
            return new { key, saved = false, removed = true };
        }

        if (!SecretFormat.IsHeaderSafe(value))
            throw new CommandException("invalid_secret", "secret value must be ASCII and single-line");

        _o.Secrets.Set(key, value);
        _o.Log.Append(LogLevel.Info, "mailbox", "secret.set", $"ایجنت کلید «{key}» را در مخزن امن ذخیره کرد", $"len={value.Length}");
        return new { key, saved = true, length = value.Length };
    }

    private async Task<object> ImportLegacyAsync(CommandContext ctx, CancellationToken ct)
    {
        var overwrite = ctx.Bool("overwrite", false);
        var summary = await _o.RunLegacyImport(overwrite).ConfigureAwait(false);
        return new { summary = _o.Redactor.Redact(summary) };
    }

    private async Task<object> OsRunAsync(CommandContext ctx, CancellationToken ct)
    {
        var file = (ctx.Str("file") ?? "").Trim();
        if (file.Length == 0) throw new CommandException("bad_args", "args.file is required");
        var args = ctx.StrList("args");
        if (args.Count > 32) throw new CommandException("bad_args", "too many arguments");
        var timeout = TimeSpan.FromSeconds(ctx.Int("timeoutSec", 120, 5, 900));
        var resolved = ResolveExecutable(file) ?? throw new CommandException("not_found", $"'{file}' was not found on this PC (PATH, %APPDATA%\\npm)");

        var commandLine = QuoteForDisplay(resolved) + (args.Count > 0 ? " " + string.Join(" ", args.Select(QuoteForDisplay)) : "");
        _o.Log.Append(LogLevel.Info, "mailbox", "os.run", "ایجنت فرمانی روی این رایانه اجرا کرد", commandLine + (string.IsNullOrWhiteSpace(ctx.Note) ? "" : "\n" + ctx.Note));

        var settings = _o.GetSettings();
        var proxy = settings.Kling.Proxy;
        var result = await KlingCli.RunAsync(resolved, args, timeout, proxy, ct).ConfigureAwait(false);
        return new
        {
            command = commandLine,
            exitCode = result.ExitCode,
            timedOut = result.TimedOut,
            durationMs = result.DurationMs,
            stdout = Tail(_o.Redactor.Redact(result.StdOut), 16_000),
            stderr = Tail(_o.Redactor.Redact(result.StdErr), 6_000)
        };
    }

    /// <summary>
    /// Downloads a build from the repository's Releases (default: newest <c>marketing-app-dev-*</c>), verifies its SHA-256 and — unless
    /// <c>args.apply=false</c> — swaps the running EXE and restarts a few seconds after the reply has been sent.
    /// </summary>
    private async Task<object> AppUpdateAsync(CommandContext ctx, CancellationToken ct)
    {
        var settings = _o.GetSettings();
        var token = _o.Secrets.GetOrEmpty(SecretKeys.GitHubToken);
        if (token.Length == 0) throw new CommandException("no_token", "GitHub token missing");
        var tag = ctx.Str("tag");
        var apply = ctx.Bool("apply", true);
        var delay = TimeSpan.FromSeconds(ctx.Int("delaySec", 10, 5, 120)); // the reply must reach the outbox before the process goes away

        ReleaseInfo release;
        UpdateDownload download;
        try
        {
            release = await AppUpdater.FindReleaseAsync(settings.GitHub, token, tag, ct).ConfigureAwait(false);
            download = await AppUpdater.DownloadAsync(settings.GitHub, token, release, _o.DownloadFolder, ct).ConfigureAwait(false);
        }
        catch (UpdateException ex)
        {
            throw new CommandException("update_" + ex.Code, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            throw new CommandException("update_network", ex.Message);
        }
        _o.Log.Append(LogLevel.Success, "mailbox", "update", $"نسخهٔ {release.Tag} دانلود و تأیید شد", $"{download.Bytes:N0} bytes, sha256 {download.Sha256[..12]}…, {download.DurationMs} ms");

        string? restartError = null;
        if (apply) restartError = await _o.RestartWith(download.ExePath, delay).ConfigureAwait(false);
        return new
        {
            release = release.Tag,
            name = release.Name,
            publishedAt = release.PublishedAt,
            file = download.ExePath,
            bytes = download.Bytes,
            sha256 = download.Sha256,
            downloadMs = download.DurationMs,
            currentVersion = _o.AppVersion,
            applied = apply && restartError is null,
            restartInSec = apply && restartError is null ? (int)delay.TotalSeconds : 0,
            error = restartError
        };
    }

    private async Task<object> AppRestartAsync(CommandContext ctx)
    {
        var delay = TimeSpan.FromSeconds(ctx.Int("delaySec", 6, 4, 120));
        var error = await _o.Restart(delay).ConfigureAwait(false);
        if (error is not null) throw new CommandException("restart_failed", error);
        return new { restarting = true, inSec = (int)delay.TotalSeconds, currentVersion = _o.AppVersion };
    }

    /// <summary>Absolute paths are used as-is; bare names are searched on PATH (with .cmd/.exe/.bat on Windows) and in the npm global folder.</summary>
    public static string? ResolveExecutable(string file)
    {
        if (Path.IsPathRooted(file)) return File.Exists(file) ? file : null;
        if (file.Contains('/') || file.Contains('\\')) return null;
        var names = OperatingSystem.IsWindows() && !Path.HasExtension(file)
            ? new[] { file + ".cmd", file + ".exe", file + ".bat", file }
            : new[] { file };
        var dirs = new List<string>();
        dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        if (OperatingSystem.IsWindows())
        {
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"));
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs"));
            dirs.Add(Environment.SystemDirectory);
        }
        foreach (var dir in dirs)
        {
            foreach (var name in names)
            {
                try
                {
                    var full = Path.Combine(dir.Trim('"'), name);
                    if (File.Exists(full)) return full;
                }
                catch (ArgumentException) { }
            }
        }
        return null;
    }

    public static string Tail(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max) return text ?? "";
        return "…[" + (text.Length - max) + " chars cut]…" + text[^max..];
    }

    private static string QuoteForDisplay(string a) => a.Length > 0 && a.All(c => !char.IsWhiteSpace(c) && c != '"') ? a : "\"" + a.Replace("\"", "\\\"") + "\"";
}
