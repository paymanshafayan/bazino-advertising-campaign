using System.Diagnostics;
using System.Net;
using System.Text;
using BazinoMarketing.Core.Http;
using BazinoMarketing.Core.Logging;
using BazinoMarketing.Core.Settings;

namespace BazinoMarketing.Core.Diagnostics;

public sealed record ProbeResult(string Host, bool DnsOk, bool HttpsOk, int? StatusCode, long DurationMs, string Note);

/// <summary>Cheap, read-only reachability checks (DNS + HTTPS HEAD/GET) for the services the app talks to.</summary>
public static class ConnectivityProbe
{
    public static readonly (string Host, string Url)[] DefaultTargets =
    {
        ("api.github.com", "https://api.github.com/"),
        ("zernio.com", "https://zernio.com/"),
        ("api.cloudflare.com", "https://api.cloudflare.com/client/v4/"),
        ("api.klingai.com", "https://api.klingai.com/"),
        ("registry.npmjs.org", "https://registry.npmjs.org/"),
    };

    public static async Task<IReadOnlyList<ProbeResult>> RunAsync(ProxySettings? proxy, CancellationToken ct = default, IEnumerable<(string Host, string Url)>? targets = null)
    {
        var list = new List<ProbeResult>();
        using var http = HttpFactory.Create(proxy, TimeSpan.FromSeconds(12));
        foreach (var (host, url) in targets ?? DefaultTargets)
        {
            ct.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            bool dns = false, https = false;
            int? status = null;
            var note = "";
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
                dns = addresses.Length > 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                note = "dns: " + ex.GetType().Name;
            }
            if (dns)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    status = (int)resp.StatusCode;
                    https = true; // any HTTP answer means TLS + routing work
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    note = ex is TaskCanceledException ? "timeout" : "https: " + (ex.InnerException?.GetType().Name ?? ex.GetType().Name);
                }
            }
            list.Add(new ProbeResult(host, dns, https, status, sw.ElapsedMilliseconds, note));
        }
        return list;
    }

    public static string Format(IReadOnlyList<ProbeResult> results)
    {
        var sb = new StringBuilder();
        foreach (var r in results)
        {
            sb.Append(r.HttpsOk ? "✔" : r.DnsOk ? "△" : "✖").Append(' ').Append(r.Host);
            if (r.StatusCode is { } s) sb.Append(" HTTP ").Append(s);
            sb.Append(" (").Append(r.DurationMs).Append(" ms)");
            if (!string.IsNullOrEmpty(r.Note)) sb.Append(" — ").Append(r.Note);
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }
}

/// <summary>Builds the text the owner pastes into chat when something fails. Always passes through the redactor.</summary>
public static class DiagnosticsBundle
{
    public static string Build(string appVersion, AppSettings settings, IReadOnlyDictionary<string, string> toolStates,
        IReadOnlyList<LogEvent> recentEvents, Redactor redactor, string? probes = null, string? dataFolder = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Bazino Marketing Studio — diagnostics ===");
        sb.AppendLine($"time: {DateTimeOffset.Now:O}");
        sb.AppendLine($"app: {appVersion}");
        sb.AppendLine($"os: {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "x64" : "x86")}) runtime: {Environment.Version}");
        sb.AppendLine($"culture: {System.Globalization.CultureInfo.CurrentCulture.Name} ui: {System.Globalization.CultureInfo.CurrentUICulture.Name}");
        if (dataFolder is not null) sb.AppendLine($"data: {dataFolder}");
        sb.AppendLine($"github: repo={settings.GitHub.Repository} branch={settings.GitHub.Branch} mailbox={settings.GitHub.MailboxPath} poll={settings.GitHub.PollSeconds}s proxy={settings.GitHub.Proxy.Mode}");
        sb.AppendLine($"kling: region={settings.Kling.Region} cli={(string.IsNullOrEmpty(settings.Kling.CliPath) ? "auto" : settings.Kling.CliPath)} proxy={settings.Kling.Proxy.Mode}");
        sb.AppendLine($"zernio: base={settings.Zernio.BaseUrl} proxy={settings.Zernio.Proxy.Mode}");
        sb.AppendLine($"flux: provider={settings.Flux.Provider} account={(string.IsNullOrEmpty(settings.Flux.AccountId) ? "-" : "set")} model={settings.Flux.Model} proxy={settings.Flux.Proxy.Mode}");
        sb.AppendLine($"custom cards: {settings.CustomCards.Count}");
        sb.AppendLine($"legacy import: attempted={settings.LegacyImport.Attempted} at={settings.LegacyImport.LastImportedAtUtc?.ToString("O") ?? "-"}");
        sb.AppendLine();
        sb.AppendLine("--- tool states ---");
        foreach (var kv in toolStates) sb.AppendLine($"{kv.Key}: {kv.Value}");
        if (!string.IsNullOrWhiteSpace(probes))
        {
            sb.AppendLine();
            sb.AppendLine("--- connectivity ---");
            sb.AppendLine(probes);
        }
        sb.AppendLine();
        sb.AppendLine($"--- last {recentEvents.Count} log events ---");
        foreach (var e in recentEvents.Reverse()) sb.AppendLine(JsonlLogStore.Format(e));
        return redactor.Redact(sb.ToString());
    }
}
