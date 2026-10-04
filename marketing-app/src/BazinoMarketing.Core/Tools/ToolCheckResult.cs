using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace BazinoMarketing.Core.Tools;

public enum ToolState { Unknown, Checking, Connected, NeedsLogin, NotConfigured, NetworkError, Error }

/// <summary>Outcome of a read-only connectivity/health check. Summary is user-facing Persian; Detail is technical (already secret-free).</summary>
public sealed record ToolCheckResult(ToolState State, string Summary, string Detail = "", string? ErrorCode = null, long DurationMs = 0)
{
    public bool IsOk => State == ToolState.Connected;

    public ToolCheckResult WithDuration(long ms) => this with { DurationMs = ms };

    public static ToolCheckResult NotConfigured(string summary, string detail = "") =>
        new(ToolState.NotConfigured, summary, detail, "not_configured");

    /// <summary>The stored credential can never work (non-ASCII/whitespace) — say so instead of a cryptic header error.</summary>
    public static ToolCheckResult InvalidKey(string what, string? value) =>
        new(ToolState.Error, $"{what} ذخیره‌شده معتبر نیست ({Secrets.SecretFormat.DescribeProblem(value)}) — آن را در تنظیمات دوباره وارد کنید",
            "stored credential is not header-safe", "key_invalid");

    /// <summary>True for transient network outcomes (timeouts, connection resets, 5xx) worth one automatic retry.</summary>
    public bool IsTransient => State == ToolState.NetworkError && ErrorCode is not null &&
                               (ErrorCode == "timeout" || ErrorCode.StartsWith("network:", StringComparison.Ordinal) || ErrorCode.StartsWith("http:5", StringComparison.Ordinal));

    /// <summary>Runs a check and retries once after a short pause when the first attempt failed transiently (slow/proxied networks).</summary>
    public static async Task<ToolCheckResult> WithRetryAsync(Func<CancellationToken, Task<ToolCheckResult>> run, CancellationToken ct, int attempts = 2, TimeSpan? pause = null)
    {
        ToolCheckResult? last = null;
        for (var i = 1; i <= attempts; i++)
        {
            var r = await run(ct).ConfigureAwait(false);
            if (i > 1)
                r = r with { Detail = (r.Detail + $" | attempt {i}/{attempts} (previous: {last?.ErrorCode ?? "?"})").Trim(' ', '|') , DurationMs = r.DurationMs + (last?.DurationMs ?? 0) };
            if (!r.IsTransient || i == attempts || ct.IsCancellationRequested) return r;
            last = r;
            try { await Task.Delay(pause ?? TimeSpan.FromSeconds(1.5), ct).ConfigureAwait(false); } catch (OperationCanceledException) { return r; }
        }
        return last!;
    }

    public static ToolCheckResult FromException(Exception ex, string context)
    {
        switch (ex)
        {
            case OperationCanceledException:
                return new ToolCheckResult(ToolState.NetworkError, "زمان اتصال تمام شد", context + ": timeout", "timeout");
            case HttpRequestException hre when hre.StatusCode is { } code:
                return FromStatus(code, context, hre.Message);
            case HttpRequestException hre:
                var inner = hre.InnerException;
                var reason = inner is SocketException se ? se.SocketErrorCode.ToString() :
                             inner is System.Security.Authentication.AuthenticationException ? "tls" :
                             hre.HttpRequestError.ToString();
                return new ToolCheckResult(ToolState.NetworkError, "دسترسی به شبکه/سرویس ممکن نشد", $"{context}: {reason} — {hre.Message}", "network:" + reason);
            case System.ComponentModel.Win32Exception w32:
                return new ToolCheckResult(ToolState.Error, "اجرای برنامهٔ خارجی ناموفق بود", $"{context}: {w32.Message}", "win32:" + w32.NativeErrorCode);
            default:
                return new ToolCheckResult(ToolState.Error, "خطای غیرمنتظره", $"{context}: {ex.GetType().Name}: {ex.Message}", "exception");
        }
    }

    public static ToolCheckResult FromStatus(HttpStatusCode status, string context, string body = "")
    {
        var code = (int)status;
        var detail = $"{context}: HTTP {code}" + (string.IsNullOrWhiteSpace(body) ? "" : " — " + Trim(body, 300));
        return code switch
        {
            401 => new ToolCheckResult(ToolState.NeedsLogin, "کلید/توکن پذیرفته نشد (401)", detail, "http:401"),
            403 => new ToolCheckResult(ToolState.NeedsLogin, "دسترسی رد شد (403) — مجوزهای کلید را بررسی کنید", detail, "http:403"),
            404 => new ToolCheckResult(ToolState.Error, "آدرس یا منبع یافت نشد (404)", detail, "http:404"),
            429 => new ToolCheckResult(ToolState.Error, "محدودیت نرخ درخواست (429)", detail, "http:429"),
            >= 500 => new ToolCheckResult(ToolState.NetworkError, $"خطای سمت سرویس ({code})", detail, "http:" + code),
            _ => new ToolCheckResult(ToolState.Error, $"پاسخ غیرمنتظره ({code})", detail, "http:" + code)
        };
    }

    public static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    public static async Task<ToolCheckResult> Timed(Func<Task<ToolCheckResult>> action, string context)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var r = await action().ConfigureAwait(false);
            return r.WithDuration(sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            return FromException(ex, context).WithDuration(sw.ElapsedMilliseconds);
        }
    }
}
