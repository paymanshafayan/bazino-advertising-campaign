using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BazinoMarketing.Core.Logging;

public enum LogLevel { Debug, Info, Success, Warning, Error }

public sealed class LogEvent
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LogLevel Level { get; set; } = LogLevel.Info;
    /// <summary>app | github | kling | zernio | flux | custom:&lt;id&gt; | mailbox | import | diag</summary>
    public string Tool { get; set; } = "app";
    /// <summary>Short machine-readable category such as connect, settings.save, tool.test, command.</summary>
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
    public string Detail { get; set; } = "";
    public string? RequestId { get; set; }
    public string? SessionId { get; set; }
    public string? Risk { get; set; }
    public string? Decision { get; set; }
    public string? Outcome { get; set; }
    public long? DurationMs { get; set; }
    public string? ErrorCode { get; set; }
    public Dictionary<string, string>? Data { get; set; }

    public bool IsFailure => Level == LogLevel.Error || string.Equals(Outcome, "failed", StringComparison.OrdinalIgnoreCase);
}

public sealed class LogQuery
{
    public string? Tool { get; set; }
    public LogLevel? MinLevel { get; set; }
    public bool OnlyFailures { get; set; }
    public string? Text { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public int Limit { get; set; } = 2000;
}

/// <summary>
/// Append-only structured log (one JSON object per line). Every event is redacted before it is written,
/// so copying the log can never leak a secret. Files rotate per day and at 20 MB; the newest 30 files are kept.
/// </summary>
public sealed class JsonlLogStore
{
    private const long MaxFileBytes = 20L * 1024 * 1024;
    private const int KeepFiles = 30;
    private readonly object _gate = new();
    private readonly Redactor _redactor;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string Directory { get; }

    /// <summary>Raised after an event was written (already redacted). May fire on any thread.</summary>
    public event Action<LogEvent>? Appended;

    public JsonlLogStore(string directory, Redactor redactor)
    {
        Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
    }

    public string CurrentFile => Path.Combine(Directory, $"app-{DateTime.Now:yyyyMMdd}.jsonl");

    public LogEvent Append(LogEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        e.Message = _redactor.Redact(e.Message);
        e.Detail = _redactor.Redact(e.Detail);
        if (e.Data is { Count: > 0 })
            e.Data = e.Data.ToDictionary(kv => kv.Key, kv => _redactor.Redact(kv.Value), StringComparer.Ordinal);

        var line = JsonSerializer.Serialize(e, Json);
        lock (_gate)
        {
            System.IO.Directory.CreateDirectory(Directory);
            var file = CurrentFile;
            if (File.Exists(file) && new FileInfo(file).Length > MaxFileBytes)
            {
                var rotated = Path.Combine(Directory, $"app-{DateTime.Now:yyyyMMdd}-{DateTime.Now:HHmmss}.jsonl");
                File.Move(file, rotated, overwrite: true);
                Prune();
            }
            File.AppendAllText(file, line + Environment.NewLine, new UTF8Encoding(false));
        }
        Appended?.Invoke(e);
        return e;
    }

    public LogEvent Append(LogLevel level, string tool, string category, string message, string detail = "",
        long? durationMs = null, string? errorCode = null, string? outcome = null, Dictionary<string, string>? data = null) =>
        Append(new LogEvent
        {
            Level = level, Tool = tool, Category = category, Message = message, Detail = detail,
            DurationMs = durationMs, ErrorCode = errorCode, Outcome = outcome, Data = data
        });

    public IReadOnlyList<LogEvent> Read(LogQuery? query = null)
    {
        query ??= new LogQuery();
        var results = new List<LogEvent>();
        if (!System.IO.Directory.Exists(Directory)) return results;

        var files = System.IO.Directory.GetFiles(Directory, "app-*.jsonl").OrderByDescending(f => f, StringComparer.Ordinal);
        foreach (var file in files)
        {
            string[] lines;
            lock (_gate) { lines = File.ReadAllLines(file); }
            for (var i = lines.Length - 1; i >= 0; i--)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                LogEvent? e;
                try { e = JsonSerializer.Deserialize<LogEvent>(lines[i], Json); }
                catch (JsonException) { continue; }
                if (e is null || !Matches(e, query)) continue;
                results.Add(e);
                if (results.Count >= query.Limit) return results;
            }
        }
        return results;
    }

    public static bool Matches(LogEvent e, LogQuery q)
    {
        if (!string.IsNullOrWhiteSpace(q.Tool) && !e.Tool.StartsWith(q.Tool, StringComparison.OrdinalIgnoreCase)) return false;
        if (q.MinLevel is { } min && e.Level < min) return false;
        if (q.OnlyFailures && !e.IsFailure) return false;
        if (q.From is { } from && e.Timestamp < from) return false;
        if (q.To is { } to && e.Timestamp > to) return false;
        if (!string.IsNullOrWhiteSpace(q.Text))
        {
            var t = q.Text.Trim();
            if (!(e.Message.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                  e.Detail.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                  e.Category.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                  (e.RequestId?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false)))
                return false;
        }
        return true;
    }

    public static string Format(LogEvent e)
    {
        var sb = new StringBuilder();
        sb.Append(e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")).Append(" [").Append(e.Level).Append("] ")
          .Append(e.Tool);
        if (!string.IsNullOrEmpty(e.Category)) sb.Append('/').Append(e.Category);
        sb.Append(" — ").Append(e.Message);
        if (!string.IsNullOrEmpty(e.Detail)) sb.Append(" | ").Append(e.Detail);
        if (e.DurationMs is { } ms) sb.Append(" | ").Append(ms).Append(" ms");
        if (!string.IsNullOrEmpty(e.ErrorCode)) sb.Append(" | code=").Append(e.ErrorCode);
        if (!string.IsNullOrEmpty(e.RequestId)) sb.Append(" | req=").Append(e.RequestId);
        return sb.ToString();
    }

    private void Prune()
    {
        var files = System.IO.Directory.GetFiles(Directory, "app-*.jsonl").OrderByDescending(f => f, StringComparer.Ordinal).ToList();
        foreach (var old in files.Skip(KeepFiles))
        {
            try { File.Delete(old); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
