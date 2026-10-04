using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media;
using BazinoMarketing.App.Infrastructure;
using BazinoMarketing.App.Services;
using BazinoMarketing.Core.Secrets;
using BazinoMarketing.Core.Tools;

namespace BazinoMarketing.App.ViewModels;

/// <summary>One headline number on the daily-report page.</summary>
public sealed class ReportKpi
{
    public string Icon { get; init; } = "";
    public string Label { get; init; } = "";
    public double Value { get; init; }
    public string Note { get; init; } = "";
    public Brush Accent { get; init; } = Brushes.Gray;
}

/// <summary>One slice of the engagement split.</summary>
public sealed class BreakdownItem
{
    public string Icon { get; init; } = "";
    public string Label { get; init; } = "";
    public double Value { get; init; }
    public string ValueText { get; init; } = "";
    public double SharePercent { get; init; }
    public Brush Accent { get; init; } = Brushes.Gray;
}

/// <summary>One row of the "best posts" table.</summary>
public sealed class ReportPostRow
{
    public string Title { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Reach { get; init; } = "";
    public string Likes { get; init; } = "";
    public string Comments { get; init; } = "";
    public string Saves { get; init; } = "";
    /// <summary>Ordering aid (reach, falling back to likes) — never displayed.</summary>
    public double SortKey { get; init; }
}

/// <summary>
/// The «گزارش روزانه» page: reads the newest Instagram insights report that the app wrote to
/// <c>marketing-app-mailbox/insights/daily/</c> and turns it into tiles, diagrams and a post table.
/// Read-only — it never writes anything back to the repository.
/// </summary>
public sealed class DailyReportViewModel : ObservableObject
{
    private static readonly Brush Gold = new SolidColorBrush(Color.FromRgb(0xD9, 0x9A, 0x00));
    private static readonly Brush Blue = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
    private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x12, 0x80, 0x5C));
    private static readonly Brush Violet = new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED));
    private static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(0xEA, 0x6A, 0x0C));
    private static readonly Brush Grey = new SolidColorBrush(Color.FromRgb(0x66, 0x70, 0x85));

    private readonly AppServices _services;
    private bool _isBusy;
    private bool _hasReport;
    private string _message = "";
    private string _statusText = "";
    private string _reportDateText = "—";
    private string _periodText = "";
    private string _handleText = "";
    private string _generatedText = "";
    private int _windowDays = 14;
    private string _centerValue = "";
    private string _centerLabel = "تعامل";

    public DailyReportViewModel(AppServices services)
    {
        _services = services;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        WindowCommand = new RelayCommand(parameter =>
        {
            if (int.TryParse(parameter?.ToString(), out var days) && days is 7 or 14 or 30) SelectedWindow = days;
        });
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand WindowCommand { get; }

    public ObservableCollection<ReportKpi> Kpis { get; } = new();
    public ObservableCollection<double> ReachValues { get; } = new();
    public ObservableCollection<string> ReachLabels { get; } = new();
    public ObservableCollection<double> FollowerValues { get; } = new();
    public ObservableCollection<string> FollowerLabels { get; } = new();
    public ObservableCollection<BreakdownItem> Breakdown { get; } = new();
    public ObservableCollection<double> BreakdownValues { get; } = new();
    public ObservableCollection<Brush> BreakdownBrushes { get; } = new();
    public ObservableCollection<ReportPostRow> Posts { get; } = new();

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(BusyText));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string BusyText => IsBusy ? "در حال خواندن گزارش…" : "";

    public bool HasReport
    {
        get => _hasReport;
        private set
        {
            if (SetProperty(ref _hasReport, value))
            {
                OnPropertyChanged(nameof(ShowEmpty));
                OnPropertyChanged(nameof(ShowContent));
            }
        }
    }

    public bool ShowEmpty => !_hasReport;
    public bool ShowContent => _hasReport;

    public string Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage => !string.IsNullOrWhiteSpace(_message);

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string ReportDateText { get => _reportDateText; private set => SetProperty(ref _reportDateText, value); }
    public string PeriodText { get => _periodText; private set => SetProperty(ref _periodText, value); }
    public string HandleText { get => _handleText; private set => SetProperty(ref _handleText, value); }
    public string GeneratedText { get => _generatedText; private set => SetProperty(ref _generatedText, value); }
    public string CenterValue { get => _centerValue; private set => SetProperty(ref _centerValue, value); }
    public string CenterLabel { get => _centerLabel; private set => SetProperty(ref _centerLabel, value); }

    public bool HasPosts => Posts.Count > 0;

    public int SelectedWindow
    {
        get => _windowDays;
        set
        {
            if (!SetProperty(ref _windowDays, value)) return;
            OnPropertyChanged(nameof(IsWindow7));
            OnPropertyChanged(nameof(IsWindow14));
            OnPropertyChanged(nameof(IsWindow30));
            OnPropertyChanged(nameof(WindowText));
            SliceReach();
        }
    }

    public bool IsWindow7 => _windowDays == 7;
    public bool IsWindow14 => _windowDays == 14;
    public bool IsWindow30 => _windowDays == 30;
    public string WindowText => _windowDays.ToString(CultureInfo.InvariantCulture) + " روز";

    private List<(string Label, double Value)> _reachSeries = new();
    private List<(string Label, double Value)> _followerSeries = new();

    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        // Sample / screenshot mode must never touch the network; SetSample() provides the deterministic content.
        if (_services.IsSampleMode) return;
        IsBusy = true;
        Message = "";
        try
        {
            var settings = _services.Settings.GitHub;
            var token = _services.Secrets.GetOrEmpty(SecretKeys.GitHubToken).Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                SetEmpty("کلید گیت‌هاب در برنامه ثبت نشده است، پس گزارش روزانه خوانده نمی‌شود.");
                return;
            }

            var directory = settings.MailboxPath.TrimEnd('/') + "/insights/daily";
            IReadOnlyList<GitHubContentEntry> entries;
            try
            {
                entries = await GitHubClient.ListDirectoryAsync(settings, token, directory).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                SetEmpty("فهرست گزارش‌ها خوانده نشد: " + ex.Message);
                return;
            }

            var files = entries
                .Where(e => e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0)
            {
                SetEmpty("هنوز هیچ گزارش روزانه‌ای ساخته نشده است. گزارش‌ها پس از نخستین اجرای موفق «گزارش روزانه» روی شاخهٔ صندوق فرمان ظاهر می‌شوند.");
                return;
            }

            var newest = files[0];
            var bytes = await GitHubClient.DownloadRawAsync(settings, token, newest.Path).ConfigureAwait(true);
            JsonNode? root;
            try
            {
                root = JsonNode.Parse(bytes);
            }
            catch (JsonException ex)
            {
                SetEmpty("فایل گزارش خوانده نشد: " + ex.Message);
                return;
            }

            Apply(root, newest.Name, files.Count);
        }
        catch (Exception ex)
        {
            SetEmpty("خواندن گزارش ممکن نشد: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ------------------------------------------------------------------ parsing

    private void Apply(JsonNode? root, string fileName, int availableReports)
    {
        if (root is not JsonObject report)
        {
            SetEmpty("ساختار فایل گزارش شناخته‌شده نبود.");
            return;
        }

        Kpis.Clear();
        ReachValues.Clear();
        ReachLabels.Clear();
        FollowerValues.Clear();
        FollowerLabels.Clear();
        Breakdown.Clear();
        BreakdownValues.Clear();
        BreakdownBrushes.Clear();
        Posts.Clear();
        _reachSeries = new List<(string, double)>();
        _followerSeries = new List<(string, double)>();

        var status = (report["status"] as JsonValue)?.GetValue<string>() ?? "unavailable";
        var reportDate = (report["reportDate"] as JsonValue)?.GetValue<string>() ?? fileName.Replace(".json", "");
        StatusText = status switch
        {
            "complete" => "گزارش کامل",
            "partial" => "گزارش ناقص (بخشی از داده‌ها)",
            _ => "گزارش بدون داده"
        };
        ReportDateText = reportDate;
        HandleText = ((report["account"] as JsonObject)?["handle"] as JsonValue)?.GetValue<string>() ?? "";
        if (report["period"] is JsonObject period)
        {
            var from = (period["fromDate"] as JsonValue)?.GetValue<string>() ?? "";
            var to = (period["toDate"] as JsonValue)?.GetValue<string>() ?? "";
            var days = 0;
            if (period["days"] is JsonValue daysValue) daysValue.TryGetValue<int>(out days);
            PeriodText = days > 0 ? $"{from} تا {to} ({days} روز)" : $"{from} تا {to}";
        }
        var generatedText = (report["generatedAtUtc"] as JsonValue)?.ToString();
        if (!string.IsNullOrWhiteSpace(generatedText) &&
            DateTimeOffset.TryParse(generatedText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var generatedAt))
            GeneratedText = "ساخته شده: " + generatedAt.ToLocalTime().ToString("yyyy/MM/dd — HH:mm", CultureInfo.InvariantCulture);
        Message = $"گزارش {reportDate} از مخزن خوانده شد" + (availableReports > 1 ? $" ({availableReports} گزارش موجود)" : "");

        var sections = report["sections"] as JsonObject;
        var totals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (sections is not null)
        {
            CollectTotals(sections["accountTotals"]?["data"], totals);
            CollectTotals(sections["stories"]?["data"], totals);
        }

        var reach = Series(sections?["dailyReach"]?["data"], "reach");
        if (reach.Count == 0) reach = Series(sections?["accountTotals"]?["data"], "reach");
        _reachSeries = reach;
        SliceReach();

        _followerSeries = Series(sections?["followers"]?["data"], null);
        SliceFollowers();

        BuildKpis(totals);
        BuildBreakdown(totals);
        BuildPosts(sections?["postPerformance"]?["data"]);

        HasReport = Kpis.Count > 0 || ReachValues.Count > 0 || Posts.Count > 0;
        OnPropertyChanged(nameof(HasPosts));
        if (!HasReport) SetEmpty("گزارش روی مخزن هست، اما هیچ داد ه‌ای برای نمایش در آن نبود.");
    }

    private void SliceReach()
    {
        ReachValues.Clear();
        ReachLabels.Clear();
        if (_reachSeries.Count == 0) return;
        foreach (var (label, value) in _reachSeries.TakeLast(Math.Min(_windowDays, _reachSeries.Count)))
        {
            ReachValues.Add(value);
            ReachLabels.Add(label);
        }
    }

    private void SliceFollowers()
    {
        FollowerValues.Clear();
        FollowerLabels.Clear();
        if (_followerSeries.Count == 0) return;
        foreach (var (label, value) in _followerSeries.TakeLast(Math.Min(30, _followerSeries.Count)))
        {
            FollowerValues.Add(value);
            FollowerLabels.Add(label);
        }
    }

    private void BuildKpis(Dictionary<string, double> totals)
    {
        var definitions = new (string Icon, string Label, string Key, Brush Accent, string Note)[]
        {
            ("👁", "بازدید", "views", Blue, "بازدید کل بازه"),
            ("📣", "دسترسی", "reach", Gold, "حساب‌های یکتای رسیده"),
            ("💬", "تعامل", "total_interactions", Green, "لایک، کامنت، ذخیره و اشتراک"),
            ("❤️", "لایک", "likes", Orange, ""),
            ("🔖", "ذخیره", "saves", Violet, ""),
            ("🔗", "کلیک روی لینک", "profile_links_taps", Grey, "")
        };
        foreach (var (icon, label, key, accent, note) in definitions)
        {
            if (!totals.TryGetValue(key, out var value) || value <= 0) continue;
            Kpis.Add(new ReportKpi
            {
                Icon = icon,
                Label = label,
                Value = value,
                Note = note,
                Accent = accent
            });
        }

        var followerTotal = _followerSeries.Count > 0 ? _followerSeries[^1].Value :
            totals.TryGetValue("followers", out var f) ? f : 0;
        if (followerTotal > 0)
        {
            Kpis.Add(new ReportKpi
            {
                Icon = "👥",
                Label = "دنبال‌کننده",
                Value = followerTotal,
                Note = _followerSeries.Count > 0 && _followerSeries.Count > 1
                    ? ChangeText(_followerSeries[^1].Value - _followerSeries[0].Value)
                    : "",
                Accent = Violet
            });
        }
    }

    private static string ChangeText(double delta)
    {
        var rounded = Math.Round(delta, 0);
        if (Math.Abs(rounded) < 1) return "بدون تغییر محسوس در بازه";
        var sign = rounded > 0 ? "+" : "−";
        return sign + Math.Abs(rounded).ToString("N0", CultureInfo.InvariantCulture) + " نفر در بازه";
    }

    private void BuildBreakdown(Dictionary<string, double> totals)
    {
        var definitions = new (string Icon, string Label, string Key, Brush Accent)[]
        {
            ("❤️", "لایک", "likes", Gold),
            ("💬", "کامنت", "comments", Blue),
            ("🔖", "ذخیره", "saves", Green),
            ("📤", "اشتراک‌گذاری", "shares", Violet),
            ("↩️", "پاسخ", "replies", Orange),
            ("🔁", "بازنشر", "reposts", Grey)
        };
        var parts = definitions
            .Where(d => totals.ContainsKey(d.Key) && totals[d.Key] > 0)
            .Select(d => (d.Icon, d.Label, Value: totals[d.Key], d.Accent))
            .ToList();
        var total = parts.Sum(p => p.Value);
        CenterValue = total > 0 ? ChartFormat(total) : "";
        CenterLabel = "تعامل";
        foreach (var part in parts)
        {
            var share = total > 0 ? part.Value / total : 0;
            Breakdown.Add(new BreakdownItem
            {
                Icon = part.Icon,
                Label = part.Label,
                Value = part.Value,
                ValueText = ChartFormat(part.Value),
                SharePercent = Math.Round(share * 100, 1),
                Accent = part.Accent
            });
            BreakdownValues.Add(part.Value);
            BreakdownBrushes.Add(part.Accent);
        }
    }

    private void BuildPosts(JsonNode? data)
    {
        if (data is null) return;
        var candidates = new List<JsonObject>();
        WalkObjects(data, candidates);
        var rows = new List<ReportPostRow>();
        foreach (var node in candidates)
        {
            var title = Text(node, "caption", "title", "message", "text", "mediaCaption");
            var reach = Number(node, "reach", "impressions", "plays", "views");
            var likes = Number(node, "likes", "like_count");
            var comments = Number(node, "comments", "comments_count");
            var saves = Number(node, "saves", "saved");
            if (string.IsNullOrWhiteSpace(title) && reach is null && likes is null) continue;
            rows.Add(new ReportPostRow
            {
                Title = string.IsNullOrWhiteSpace(title) ? "(بدون کپشن)" : Flatten(title, 90),
                Kind = Text(node, "mediaType", "media_type", "type", "kind"),
                Reach = reach is null ? "—" : ChartFormat(reach.Value),
                Likes = likes is null ? "—" : ChartFormat(likes.Value),
                Comments = comments is null ? "—" : ChartFormat(comments.Value),
                Saves = saves is null ? "—" : ChartFormat(saves.Value),
                SortKey = reach ?? likes ?? 0
            });
        }

        foreach (var row in rows.OrderByDescending(r => r.SortKey).Take(6))
            Posts.Add(row);
    }

    // ------------------------------------------------------------------ tolerant JSON helpers

    private static void WalkObjects(JsonNode? node, List<JsonObject> found, int depth = 0)
    {
        if (node is null || depth > 8) return;
        if (node is JsonArray array)
        {
            foreach (var child in array) WalkObjects(child, found, depth + 1);
            return;
        }
        if (node is not JsonObject obj) return;
        found.Add(obj);
        foreach (var child in obj.Select(kv => kv.Value)) WalkObjects(child, found, depth + 1);
    }

    private static void CollectTotals(JsonNode? node, Dictionary<string, double> totals, int depth = 0)
    {
        if (node is null || depth > 6) return;
        if (node is JsonArray array)
        {
            foreach (var child in array) CollectTotals(child, totals, depth + 1);
            return;
        }
        if (node is not JsonObject obj) return;

        var name = Text(obj, "name", "metric", "key", "title");
        if (!string.IsNullOrWhiteSpace(name))
        {
            var value = Number(obj, "value", "total_value", "total", "count", "amount");
            if (value is not null && !totals.ContainsKey(name)) totals[name] = value.Value;
            if (value is null && obj["total_value"] is JsonObject totalObject)
            {
                var nested = Number(totalObject, "value", "total", "count");
                if (nested is not null && !totals.ContainsKey(name)) totals[name] = nested.Value;
            }
        }

        foreach (var child in obj.Select(kv => kv.Value)) CollectTotals(child, totals, depth + 1);
    }

    private static List<(string Label, double Value)> Series(JsonNode? node, string? metricFilter)
    {
        var best = new List<(string, double)>();
        SearchSeries(node, metricFilter, best, 0);
        return best;
    }

    private static void SearchSeries(JsonNode? node, string? metricFilter, List<(string, double)> best, int depth)
    {
        if (node is null || depth > 7) return;
        if (node is JsonArray array)
        {
            var candidate = new List<(string, double)>();
            foreach (var item in array)
            {
                if (item is not JsonObject point) continue;
                var label = Text(point, "date", "day", "endTime", "end_time", "timestamp", "startTime", "start_time", "period");
                var value = Number(point, "value", "reach", "count", "followers", "followerCount", "total");
                if (value is null && metricFilter is not null) value = Number(point, metricFilter);
                if (label.Length == 0 || value is null) continue;
                candidate.Add((ShortDate(label), value.Value));
            }
            if (candidate.Count > best.Count)
            {
                // Mutate the shared list (not the parameter): the caller owns the collection that ends up on screen.
                best.Clear();
                best.AddRange(candidate);
            }
            foreach (var child in array) SearchSeries(child, metricFilter, best, depth + 1);
            return;
        }
        if (node is JsonObject obj)
            foreach (var child in obj.Select(kv => kv.Value)) SearchSeries(child, metricFilter, best, depth + 1);
    }

    private static string ShortDate(string raw)
    {
        var take = raw.Length >= 10 ? raw[..10] : raw;
        return DateTime.TryParse(take, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToString("MM/dd", CultureInfo.InvariantCulture)
            : take;
    }

    private static string Text(JsonObject obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj.TryGetPropertyValue(name, out var node) && node is JsonValue value &&
                value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
                return text;
        }
        return "";
    }

    private static double? Number(JsonObject obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (!obj.TryGetPropertyValue(name, out var node) || node is null) continue;
            if (node is JsonValue value)
            {
                if (value.TryGetValue<double>(out var number)) return number;
                if (value.TryGetValue<string>(out var text) && double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                    return parsed;
            }
            if (node is JsonObject nested)
            {
                var inner = Number(nested, "value", "total", "count");
                if (inner is not null) return inner;
            }
        }
        return null;
    }

    private static string Flatten(string text, int max)
    {
        var clean = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return clean.Length > max ? clean[..max] + "…" : clean;
    }

    private static string ChartFormat(double value) => value >= 1000
        ? value.ToString("N0", CultureInfo.InvariantCulture)
        : value.ToString("0.#", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ states

    private void SetEmpty(string message)
    {
        Message = message;
        HasReport = false;
        StatusText = "گزارشی برای نمایش نیست";
        ReportDateText = "—";
        PeriodText = "";
        GeneratedText = "";
        Kpis.Clear();
        ReachValues.Clear();
        ReachLabels.Clear();
        FollowerValues.Clear();
        FollowerLabels.Clear();
        Breakdown.Clear();
        BreakdownValues.Clear();
        BreakdownBrushes.Clear();
        Posts.Clear();
        OnPropertyChanged(nameof(HasPosts));
    }

    /// <summary>Layout preview with clearly-marked sample numbers (used by the CI screenshot renderer).</summary>
    public void SetSample()
    {
        SetEmpty("");
        Message = "نمونهٔ نمایشی برای پیش‌نمایش چیدمان — هیچ دادهٔ واقعی در این تصویر نیست.";
        StatusText = "گزارش کامل";
        ReportDateText = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        PeriodText = DateTime.Now.AddDays(-30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " تا " + DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " (30 روز)";
        HandleText = "@bazinopro";
        GeneratedText = "ساخته شده: " + DateTime.Now.ToString("yyyy/MM/dd — HH:mm", CultureInfo.InvariantCulture);

        Kpis.Add(new ReportKpi { Icon = "👁", Label = "بازدید", Value = 18450, Note = "بازدید کل بازه", Accent = Blue });
        Kpis.Add(new ReportKpi { Icon = "📣", Label = "دسترسی", Value = 12380, Note = "حساب‌های یکتای رسیده", Accent = Gold });
        Kpis.Add(new ReportKpi { Icon = "💬", Label = "تعامل", Value = 2210, Note = "لایک، کامنت، ذخیره و اشتراک", Accent = Green });
        Kpis.Add(new ReportKpi { Icon = "👥", Label = "دنبال‌کننده", Value = 192, Note = "+14 نفر در بازه", Accent = Violet });
        Kpis.Add(new ReportKpi { Icon = "🔗", Label = "کلیک روی لینک", Value = 96, Note = "", Accent = Grey });

        var labels = new[] { "09/20", "09/21", "09/22", "09/23", "09/24", "09/25", "09/26", "09/27", "09/28", "09/29", "09/30", "10/01", "10/02", "10/03" };
        var reach = new[] { 210d, 340, 280, 460, 520, 390, 610, 730, 540, 680, 810, 760, 920, 880 };
        for (var i = 0; i < labels.Length; i++)
        {
            ReachLabels.Add(labels[i]);
            ReachValues.Add(reach[i]);
        }

        var followerLabels = new[] { "09/04", "09/11", "09/18", "09/25", "10/02" };
        var followers = new[] { 168d, 172, 179, 185, 192 };
        for (var i = 0; i < followerLabels.Length; i++)
        {
            FollowerLabels.Add(followerLabels[i]);
            FollowerValues.Add(followers[i]);
        }

        var breakdown = new (string Icon, string Label, double Value, Brush Accent)[]
        {
            ("❤️", "لایک", 1420, Gold),
            ("💬", "کامنت", 210, Blue),
            ("🔖", "ذخیره", 260, Green),
            ("📤", "اشتراک‌گذاری", 180, Violet),
            ("↩️", "پاسخ", 90, Orange),
            ("🔁", "بازنشر", 50, Grey)
        };
        var total = breakdown.Sum(b => b.Value);
        CenterValue = ChartFormat(total);
        CenterLabel = "تعامل";
        foreach (var item in breakdown)
        {
            Breakdown.Add(new BreakdownItem
            {
                Icon = item.Icon,
                Label = item.Label,
                Value = item.Value,
                ValueText = ChartFormat(item.Value),
                SharePercent = Math.Round(item.Value / total * 100, 1),
                Accent = item.Accent
            });
            BreakdownValues.Add(item.Value);
            BreakdownBrushes.Add(item.Accent);
        }

        Posts.Add(new ReportPostRow { Title = "نمونهٔ ریل آموزش: تنظیم دستهٔ FC 26", Kind = "reel", Reach = "4,120", Likes = "318", Comments = "24", Saves = "41" });
        Posts.Add(new ReportPostRow { Title = "نمونهٔ کاروسل خبری هفتهٔ گیم", Kind = "carousel", Reach = "2,860", Likes = "204", Comments = "12", Saves = "63" });
        Posts.Add(new ReportPostRow { Title = "نمونهٔ پست خانواده و امنیت", Kind = "post", Reach = "1,940", Likes = "151", Comments = "18", Saves = "22" });

        HasReport = true;
        OnPropertyChanged(nameof(HasPosts));
    }
}
