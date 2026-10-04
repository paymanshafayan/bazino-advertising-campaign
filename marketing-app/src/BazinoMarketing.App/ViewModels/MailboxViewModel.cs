using System.Collections.ObjectModel;
using System.Windows.Threading;
using BazinoMarketing.App.Infrastructure;
using BazinoMarketing.Core.Mailbox;

namespace BazinoMarketing.App.ViewModels;

/// <summary>
/// کارت «صندوق فرمان» روی صفحهٔ اتصال: وضعیت گوش‌دادن، ایجنت متصل (برچسب + اثر انگشت) و فهرست آخرین فرمان‌های اجراشده.
/// به تصمیم صریح مالک (۲۰۲۶-۰۹-۲۷) هیچ اجازه‌ای پرسیده نمی‌شود؛ کنترل مالک بستن برنامه یا دکمهٔ «توقف گوش‌دادن» است.
/// همهٔ رویدادهای سرویس از رشته‌های پس‌زمینه می‌آیند و اینجا به رشتهٔ UI منتقل می‌شوند.
/// </summary>
public sealed class MailboxViewModel : ObservableObject
{
    private readonly Dispatcher _dispatcher;
    private MailboxService? _service;
    private string _statusText = "صندوق فرمان هنوز راه نیفتاده است.";
    private string _statusKind = "off";
    private string _agentLabel = "", _agentFingerprint = "", _appFingerprint = "", _lastPollText = "";
    private bool _isSample;

    public MailboxViewModel(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        RestartCommand = new RelayCommand(Restart, () => _service is not null && !_isSample);
        PollNowCommand = new RelayCommand(() => _service?.PollNow(), () => _service is { IsRunning: true });
    }

    public ObservableCollection<MailboxActivity> Activity { get; } = new();
    public RelayCommand RestartCommand { get; }
    public RelayCommand PollNowCommand { get; }

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    /// <summary>off | listening | active | error | stopped — drives the colour in XAML.</summary>
    public string StatusKind { get => _statusKind; private set { if (SetProperty(ref _statusKind, value)) { OnPropertyChanged(nameof(IsActive)); RelayCommand.RaiseCanExecuteChanged(); } } }
    public bool IsActive => _statusKind == "active";
    public string AgentLabel { get => _agentLabel; private set => SetProperty(ref _agentLabel, value); }
    public string AgentFingerprint { get => _agentFingerprint; private set => SetProperty(ref _agentFingerprint, value); }
    public string AppFingerprint { get => _appFingerprint; private set => SetProperty(ref _appFingerprint, value); }
    public string LastPollText { get => _lastPollText; private set => SetProperty(ref _lastPollText, value); }
    public bool HasActivity => Activity.Count > 0;
    public string RestartText => _service is { IsRunning: true } ? "توقف گوش‌دادن" : "شروع گوش‌دادن";

    public string Explanation =>
        "ایجنت (دستیار برنامه‌نویس) فرمان‌هایش را رمزشده در همین شاخهٔ گیت‌هاب می‌گذارد و این برنامه هر چند ثانیه آن‌ها را می‌خواند و بی‌درنگ اجرا می‌کند — " +
        "بدون هیچ پرسشی از شما (تصمیم خودتان). اگر نمی‌خواهید ایجنت کاری بکند، برنامه را ببندید یا «توقف گوش‌دادن» را بزنید. " +
        "همهٔ فرمان‌ها و نتیجه‌شان در صفحهٔ «لاگ» ثبت می‌شوند و هیچ کلیدی از این رایانه بیرون نمی‌رود.";

    public void Attach(MailboxService service)
    {
        _service = service;
        service.Changed += () => _dispatcher.InvokeAsync(Refresh);
        service.ActivityAdded += a => _dispatcher.InvokeAsync(() =>
        {
            Activity.Insert(0, a);
            while (Activity.Count > 8) Activity.RemoveAt(Activity.Count - 1);
            OnPropertyChanged(nameof(HasActivity));
        });
        Refresh();
    }

    public void Refresh()
    {
        if (_service is null) return;
        var status = _service.Status;
        StatusKind = status switch
        {
            MailboxStatus.Listening => "listening",
            MailboxStatus.Active => "active",
            MailboxStatus.Error => "error",
            MailboxStatus.Stopped => "stopped",
            _ => "off"
        };
        StatusText = status switch
        {
            MailboxStatus.Listening => "در حال گوش‌دادن — هنوز ایجنتی هویتش را نگذاشته است.",
            MailboxStatus.Active => "ایجنت وصل است؛ فرمان‌هایش بی‌درنگ اجرا می‌شوند.",
            MailboxStatus.Error => string.IsNullOrEmpty(_service.StatusText) ? "دسترسی به صندوق فرمان ممکن نشد؛ دوباره تلاش می‌شود." : _service.StatusText,
            MailboxStatus.Stopped => "گوش‌دادن متوقف است.",
            _ => string.IsNullOrEmpty(_service.StatusText) ? "صندوق فرمان خاموش است." : "خاموش — " + _service.StatusText
        };
        AgentLabel = _service.AgentLabel;
        AgentFingerprint = _service.AgentFingerprint;
        AppFingerprint = _service.AppFingerprint;
        LastPollText = _service.LastPoll is { } p ? "آخرین بررسی صندوق: " + p.ToString("HH:mm:ss") : "";
        OnPropertyChanged(nameof(RestartText));
        RelayCommand.RaiseCanExecuteChanged();
    }

    private void Restart()
    {
        if (_service is null) return;
        if (_service.IsRunning)
        {
            _ = _service.StopAsync(TimeSpan.FromSeconds(5)).ContinueWith(t => _dispatcher.InvokeAsync(Refresh), TaskScheduler.Default);
        }
        else
        {
            _service.Start();
            Refresh();
        }
    }

    public void SetSample()
    {
        _isSample = true;
        StatusKind = "active";
        StatusText = "ایجنت وصل است؛ فرمان‌هایش بی‌درنگ اجرا می‌شوند.";
        AgentLabel = "Arena agent — نشست ۲۷ سپتامبر";
        AgentFingerprint = "7f3a:91c2:0be4:5d18";
        AppFingerprint = "c41d:22a9:8e07:b6f3";
        LastPollText = "آخرین بررسی صندوق: 14:02:11";
        Activity.Add(new MailboxActivity(DateTimeOffset.Now.AddMinutes(-3), "tool.check", true, "انجام شد", 2140, "read"));
        Activity.Add(new MailboxActivity(DateTimeOffset.Now.AddMinutes(-4), "diag", true, "انجام شد", 5320, "read"));
        Activity.Add(new MailboxActivity(DateTimeOffset.Now.AddMinutes(-6), "os.run", true, "انجام شد", 12000, "os"));
        OnPropertyChanged(nameof(HasActivity));
        OnPropertyChanged(nameof(RestartText));
    }

    public void NotifyRunningChanged() => OnPropertyChanged(nameof(RestartText));
}
