using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using BazinoMarketing.App.Infrastructure;
using BazinoMarketing.App.Services;
using BazinoMarketing.Core.Logging;

namespace BazinoMarketing.App.ViewModels;

public sealed class LogEntryViewModel
{
    public LogEntryViewModel(LogEvent e)
    {
        Event = e;
        Time = e.Timestamp.ToLocalTime().ToString("HH:mm:ss");
        Date = e.Timestamp.ToLocalTime().ToString("yyyy-MM-dd");
        Tool = e.Tool;
        Level = e.Level;
        LevelText = e.Level switch
        {
            LogLevel.Error => "خطا",
            LogLevel.Warning => "هشدار",
            LogLevel.Success => "موفق",
            LogLevel.Debug => "جزئیات",
            _ => "اطلاع"
        };
        Message = e.Message;
        Detail = e.Detail + (e.DurationMs is { } ms ? $"  ({ms} ms)" : "") + (string.IsNullOrEmpty(e.ErrorCode) ? "" : $"  [{e.ErrorCode}]");
        HasDetail = !string.IsNullOrWhiteSpace(Detail);
    }

    public LogEvent Event { get; }
    public string Time { get; }
    public string Date { get; }
    public string Tool { get; }
    public LogLevel Level { get; }
    public string LevelText { get; }
    public string Message { get; }
    public string Detail { get; }
    public bool HasDetail { get; }
    public string PlainText => JsonlLogStore.Format(Event);
}

public sealed class LogViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Dispatcher _dispatcher;
    private string _toolFilter = "all";
    private string _textFilter = "";
    private bool _onlyFailures;
    private bool _live = true;
    private string _statusText = "";
    private LogEntryViewModel? _selected;

    public LogViewModel(AppServices services)
    {
        _services = services;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        RefreshCommand = new RelayCommand(Refresh);
        CopyAllCommand = new RelayCommand(CopyAll, () => Entries.Count > 0);
        CopySelectedCommand = new RelayCommand(CopySelected, () => Selected is not null);
        OpenFolderCommand = new RelayCommand(() => AppServices.OpenFolder(_services.Paths.Logs));
        ClearFiltersCommand = new RelayCommand(() => { ToolFilter = "all"; TextFilter = ""; OnlyFailures = false; });
        _services.Log.Appended += OnAppended;
    }

    public ObservableCollection<LogEntryViewModel> Entries { get; } = new();
    public IReadOnlyList<string> ToolFilters { get; } = new[] { "all", "app", "github", "kling", "zernio", "flux", "browser", "custom", "mailbox", "import", "diag" };

    public RelayCommand RefreshCommand { get; }
    public RelayCommand CopyAllCommand { get; }
    public RelayCommand CopySelectedCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand ClearFiltersCommand { get; }

    public string LogFolder => _services.Paths.Logs;
    public string ToolFilter { get => _toolFilter; set { if (SetProperty(ref _toolFilter, value)) Refresh(); } }
    public string TextFilter { get => _textFilter; set { if (SetProperty(ref _textFilter, value)) Refresh(); } }
    public bool OnlyFailures { get => _onlyFailures; set { if (SetProperty(ref _onlyFailures, value)) Refresh(); } }
    public bool Live { get => _live; set => SetProperty(ref _live, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public LogEntryViewModel? Selected { get => _selected; set { if (SetProperty(ref _selected, value)) RelayCommand.RaiseCanExecuteChanged(); } }

    private LogQuery Query() => new()
    {
        Tool = string.IsNullOrWhiteSpace(ToolFilter) || ToolFilter == "all" ? null : ToolFilter,
        Text = string.IsNullOrWhiteSpace(TextFilter) ? null : TextFilter,
        OnlyFailures = OnlyFailures,
        Limit = 500
    };

    public void Refresh()
    {
        try
        {
            var events = _services.Log.Read(Query());
            Entries.Clear();
            foreach (var e in events) Entries.Add(new LogEntryViewModel(e));
            StatusText = Entries.Count == 0 ? "رویدادی مطابق فیلتر نیست." : $"{Entries.Count} رویداد (جدیدترین بالا)" + (Entries.Count >= 500 ? " — فقط ۵۰۰ مورد آخر" : "");
        }
        catch (Exception ex)
        {
            StatusText = "خواندن لاگ ناموفق بود: " + ex.Message;
        }
        RelayCommand.RaiseCanExecuteChanged();
    }

    private void OnAppended(LogEvent e)
    {
        if (!Live) return;
        if (!JsonlLogStore.Matches(e, Query())) return;
        void Add()
        {
            Entries.Insert(0, new LogEntryViewModel(e));
            while (Entries.Count > 500) Entries.RemoveAt(Entries.Count - 1);
            StatusText = $"{Entries.Count} رویداد (جدیدترین بالا)";
        }
        if (_dispatcher.CheckAccess()) Add(); else _dispatcher.BeginInvoke(Add);
    }

    private void CopyAll()
    {
        var sb = new StringBuilder();
        foreach (var entry in Entries.Reverse()) sb.AppendLine(entry.PlainText);
        TrySetClipboard(sb.ToString(), $"{Entries.Count} رویداد کپی شد (بدون کلید/توکن).");
    }

    private void CopySelected()
    {
        if (Selected is null) return;
        TrySetClipboard(Selected.PlainText, "رویداد انتخاب‌شده کپی شد.");
    }

    private void TrySetClipboard(string text, string ok)
    {
        try
        {
            Clipboard.SetText(_services.Redactor.Redact(text));
            StatusText = ok;
        }
        catch (Exception ex)
        {
            StatusText = "کپی ناموفق بود: " + ex.Message;
        }
    }

    public void AddSample(LogEvent e) => Entries.Insert(0, new LogEntryViewModel(e));
    public void SetSampleStatus(string text) => StatusText = text;
}
