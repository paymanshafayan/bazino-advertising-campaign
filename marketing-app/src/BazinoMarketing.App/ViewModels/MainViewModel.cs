using System.Windows.Media;
using BazinoMarketing.App.Infrastructure;
using BazinoMarketing.App.Services;
using BazinoMarketing.Core.Tools;

namespace BazinoMarketing.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AppServices _services;
    private string _currentPage = "connection";

    public MainViewModel(AppServices services)
    {
        _services = services;
        GitHub = new GitHubCardViewModel(services);
        Kling = new KlingCardViewModel(services);
        Zernio = new ZernioCardViewModel(services);
        Flux = new FluxCardViewModel(services);
        Groq = new GroqCardViewModel(services);

        Settings = new SettingsViewModel(services, GitHub, Kling, Zernio, Flux, Groq);
        var cards = new List<ToolCardViewModel> { GitHub, Kling, Zernio, Flux, Groq };
        cards.AddRange(Settings.CustomCards);
        Connection = new ConnectionViewModel(services, cards);
        Log = new LogViewModel(services);
        Browser = new BrowserViewModel(services);
        Media = new MediaViewModel(services);
        PublishQueue = new PublishQueueViewModel(services);
        Report = new DailyReportViewModel(services);
        _ = Browser.PrepareAsync();

        Settings.CustomCardAdded += vm => { if (!Connection.Cards.Contains(vm)) Connection.Cards.Add(vm); };
        Settings.CustomCardRemoved += vm => Connection.Cards.Remove(vm);

        Settings.LoadAll();
        // 0.8.1 and older settings files say theme=dark; the single design system is light now, so migrate quietly.
        if (!_services.IsSampleMode && !string.Equals(_services.Settings.Ui.Theme, "light", StringComparison.OrdinalIgnoreCase))
        {
            _services.Settings.Ui.Theme = "light";
            try { _services.SaveSettings(); } catch { }
        }
        Connection.RefreshTexts();
        Connection.Mailbox.PropertyChanged += (_, _) => RaiseHeaderChips();
        var last = services.Settings.Ui.LastPage;
        if (last is "settings" or "log" or "connection" or "browser" or "media" or "publish" or "report") _currentPage = last;
        if (_currentPage == "media") Media.Refresh();
        if (_currentPage == "publish") _ = PublishQueue.RefreshAsync();
        if (_currentPage == "report") _ = Report.RefreshAsync();
    }

    public GitHubCardViewModel GitHub { get; }
    public KlingCardViewModel Kling { get; }
    public ZernioCardViewModel Zernio { get; }
    public FluxCardViewModel Flux { get; }
    public GroqCardViewModel Groq { get; }
    public ConnectionViewModel Connection { get; }
    public SettingsViewModel Settings { get; }
    public LogViewModel Log { get; }
    public BrowserViewModel Browser { get; }
    public MediaViewModel Media { get; }
    public PublishQueueViewModel PublishQueue { get; }
    public DailyReportViewModel Report { get; }

    public string AppVersion => "نسخهٔ " + AppServices.Version;

    /// <summary>Product name shown in the shell. The Windows app is «ژینوس» (owner decision, 2026-10-03).</summary>
    public string AppName => "ژینوس";

    public string MailboxPathText => _services.Settings.GitHub.MailboxPath;
    public string DataFolder => _services.Paths.Root;

    /// <summary>Status-bar texts. Read straight from the mailbox view-model so they never depend on a refresh order.</summary>
    public Brush RelayDotBrush => Connection.Mailbox.IsActive ? Brushes.Green : Brushes.Gray;
    public string RelaySummary => Connection.Mailbox.IsActive ? "صندوق فرمان باز است" : "صندوق فرمان بسته است";
    public Brush MailboxDotBrush => Connection.Mailbox.StatusKind switch
    {
        "active" => Brushes.Green,
        "listening" => Brushes.Orange,
        _ => Brushes.Gray
    };
    public string MailboxSummary => Connection.Mailbox.StatusKind switch
    {
        "active" => "گوش‌دادن فعال",
        "listening" => "در انتظار ایجنت",
        _ => "بدون اتصال"
    };

    public string PageTitle => _currentPage switch
    {
        "settings" => "تنظیمات و ابزارها",
        "log" => "لاگ رویدادها",
        "browser" => "مرورگر",
        "media" => "رسانه و ویدئو",
        "publish" => "محتوای آماده انتشار",
        "report" => "گزارش روزانه",
        _ => "اتصال ابزارها"
    };

    public string PageSubtitle => _currentPage switch
    {
        "settings" => "کلیدها، کارت‌های ابزار و مسیرهای داده — همه‌چیز روی همین رایانه می‌ماند.",
        "log" => "همهٔ رویدادها پیش از ثبت از کلید و توکن پاک‌سازی می‌شوند.",
        "browser" => "اتصال به کروم خودتان برای خواندن صفحه‌ها؛ بدون سرور و بدون ابر.",
        "media" => "برش ویدئو، تبدیل گفتار به متن و ساختن پیش‌نمایش از فایل‌های همین رایانه.",
        "publish" => "پیش‌نمایش همان فید اینستاگرام؛ فقط دکمهٔ ارسال و کامنت عمل می‌کنند.",
        "report" => "بازخورد محتوای منتشرشده به‌صورت نمودار — خوانده‌شده از گزارش‌های خودِ برنامه.",
        _ => "وضعیت شش ابزار، صندوق فرمان و مسیرهای داده."
    };

    public string CurrentPage
    {
        get => _currentPage;
        set
        {
            if (!SetProperty(ref _currentPage, value)) return;
            OnPropertyChanged(nameof(CurrentContent));
            OnPropertyChanged(nameof(IsConnectionPage));
            OnPropertyChanged(nameof(IsSettingsPage));
            OnPropertyChanged(nameof(IsLogPage));
            OnPropertyChanged(nameof(IsBrowserPage));
            OnPropertyChanged(nameof(IsMediaPage));
            OnPropertyChanged(nameof(IsPublishPage));
            OnPropertyChanged(nameof(IsReportPage));
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(PageSubtitle));
            if (value == "log") Log.Refresh();
            if (value == "connection") Connection.RefreshTexts();
            if (value == "browser") _ = Browser.RefreshAsync();
            if (value == "media") Media.Refresh();
            if (value == "publish") _ = PublishQueue.RefreshAsync();
            if (value == "report") _ = Report.RefreshAsync();
            if (!_services.IsSampleMode)
            {
                _services.Settings.Ui.LastPage = value;
                try { _services.SaveSettings(); } catch { }
            }
        }
    }

    public object CurrentContent => _currentPage switch
    {
        "settings" => Settings,
        "log" => Log,
        "browser" => Browser,
        "media" => Media,
        "publish" => PublishQueue,
        "report" => Report,
        _ => Connection
    };

    public bool IsConnectionPage { get => _currentPage == "connection"; set { if (value) CurrentPage = "connection"; } }
    public bool IsSettingsPage { get => _currentPage == "settings"; set { if (value) CurrentPage = "settings"; } }
    public bool IsLogPage { get => _currentPage == "log"; set { if (value) CurrentPage = "log"; } }
    public bool IsBrowserPage { get => _currentPage == "browser"; set { if (value) CurrentPage = "browser"; } }
    public bool IsMediaPage { get => _currentPage == "media"; set { if (value) CurrentPage = "media"; } }
    public bool IsPublishPage { get => _currentPage == "publish"; set { if (value) CurrentPage = "publish"; } }
    public bool IsReportPage { get => _currentPage == "report"; set { if (value) CurrentPage = "report"; } }

    public void RaiseHeaderChips()
    {
        OnPropertyChanged(nameof(RelayDotBrush));
        OnPropertyChanged(nameof(RelaySummary));
        OnPropertyChanged(nameof(MailboxDotBrush));
        OnPropertyChanged(nameof(MailboxSummary));
    }

    /// <summary>Used by --render-screenshots to snapshot every page deterministically.</summary>
    public void SetSample(string page, int connectedCount)
    {
        if (page == "report") Report.SetSample();
        Connection.SetSampleConnectedCount(connectedCount);
        CurrentPage = page;
        RaiseHeaderChips();
    }

    /// <summary>Best-effort tool check used by the CLI <c>--check</c> mode.</summary>
    public async Task<int> CheckAllAsync()
    {
        await Connection.CheckAllAsync();
        return Connection.Cards.Count(c => c.State == ToolState.Connected);
    }
}
