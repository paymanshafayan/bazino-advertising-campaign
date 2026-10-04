using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using BazinoMarketing.Core.Settings;

namespace BazinoMarketing.Core.Http;

/// <summary>Creates HttpClients honouring the per-tool proxy setting (system / none / custom).</summary>
public static class HttpFactory
{
    public const string UserAgent = "BazinoMarketingStudio/0.1 (+windows; wpf)";
    private static readonly ConcurrentDictionary<string, SocketsHttpHandler> Handlers = new(StringComparer.Ordinal);

    public static HttpClient Create(ProxySettings? proxy, TimeSpan? timeout = null, bool allowAutoRedirect = true)
    {
        var mode = (proxy?.Mode ?? "system").Trim().ToLowerInvariant();
        var rawUrl = (proxy?.Url ?? "").Trim();
        var key = $"{mode}|{rawUrl}|redirect={allowAutoRedirect}";
        var handler = Handlers.GetOrAdd(key, _ => CreateHandler(mode, rawUrl, allowAutoRedirect));
        var client = new HttpClient(handler, disposeHandler: false) { Timeout = timeout ?? TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static SocketsHttpHandler CreateHandler(string mode, string rawUrl, bool allowAutoRedirect)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = allowAutoRedirect,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(10),
            ConnectTimeout = TimeSpan.FromSeconds(30)
        };
        switch (mode)
        {
            case "none":
                handler.UseProxy = false;
                break;
            case "custom" when Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri):
                handler.UseProxy = true;
                handler.Proxy = new WebProxy(uri) { BypassProxyOnLocal = true };
                break;
            default:
                handler.UseProxy = true; // system default (WinHTTP / env)
                break;
        }
        return handler;
    }

    public static string DescribeProxy(ProxySettings? proxy)
    {
        var mode = (proxy?.Mode ?? "system").Trim().ToLowerInvariant();
        return mode switch
        {
            "none" => "بدون پراکسی",
            "custom" => "پراکسی سفارشی",
            _ => "پراکسی سیستم"
        };
    }
}
