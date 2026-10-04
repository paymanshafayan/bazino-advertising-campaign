using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BazinoMarketing.App.Services;

/// <summary>
/// Small file the app keeps in its own Windows folder (<c>%LOCALAPPDATA%\BazinoMarketing\daily-report.marker.json</c>)
/// that records which local day's daily report was last produced. Owner's rule (2026-10-03): on every launch after
/// 08:00 the app looks only at this file — if today has no entry, it builds the report and commits it to the branch.
/// </summary>
internal sealed class DailyReportMarker
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Hour of the local day after which a missing report must be produced.</summary>
    public const int ProductionHour = 8;

    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;
    /// <summary>Local date (yyyy-MM-dd) of the last report the app knows it produced.</summary>
    [JsonPropertyName("lastReportDate")] public string LastReportDate { get; set; } = "";
    [JsonPropertyName("lastReportAtLocal")] public string LastReportAtLocal { get; set; } = "";
    [JsonPropertyName("lastReportPath")] public string LastReportPath { get; set; } = "";
    [JsonPropertyName("lastReportBranch")] public string LastReportBranch { get; set; } = "";
    [JsonPropertyName("lastReportStatus")] public string LastReportStatus { get; set; } = "";
    [JsonPropertyName("lastAttemptAtLocal")] public string LastAttemptAtLocal { get; set; } = "";
    [JsonPropertyName("lastAttemptResult")] public string LastAttemptResult { get; set; } = "";

    public static string PathFor(string dataFolder) => Path.Combine(dataFolder, "daily-report.marker.json");

    public static DailyReportMarker Read(string dataFolder)
    {
        try
        {
            var path = PathFor(dataFolder);
            if (!File.Exists(path)) return new DailyReportMarker();
            return JsonSerializer.Deserialize<DailyReportMarker>(File.ReadAllText(path), Options) ?? new DailyReportMarker();
        }
        catch
        {
            // A damaged marker must never block the app; treating it as empty means the report is produced again today.
            return new DailyReportMarker();
        }
    }

    public static void Write(string dataFolder, DailyReportMarker marker)
    {
        try
        {
            Directory.CreateDirectory(dataFolder);
            File.WriteAllText(PathFor(dataFolder), JsonSerializer.Serialize(marker, Options));
        }
        catch
        {
            // Best effort: the report itself is already in the repository; the marker only avoids redoing work.
        }
    }

    public bool HasReportFor(DateOnly day) =>
        string.Equals(LastReportDate, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the local clock is past <see cref="ProductionHour"/> and this file has no entry for today.</summary>
    public static bool IsDue(DateTimeOffset now, DailyReportMarker marker) =>
        now.Hour >= ProductionHour && !marker.HasReportFor(DateOnly.FromDateTime(now.DateTime));

    public static string Stamp(DateTimeOffset time) => time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
