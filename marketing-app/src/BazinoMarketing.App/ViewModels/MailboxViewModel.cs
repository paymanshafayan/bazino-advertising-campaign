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
    private string _newSourceUrl = "";
    private string _sourceMessage = "برای افزودن شاخه، آدرس عمومی همان برنچ را اینجا بچسبانید؛ نمونه: https://github.com/<مخزن>/<ریپو>/tree/<برنچ>";

    public MailboxViewModel(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        RestartCommand = new RelayCommand(Restart, () => _service is not null && !_isSample);
        PollNowCommand = new RelayCommand(() => _service?.PollNow(), () => _service is { IsRunning: true });
        AddSourceCommand = new RelayCommand(AddSource, () => _service is not null && !_isSample && !string.IsNullOrWhiteSpace(NewSourceUrl));
        RemoveSourceCommand = new RelayCommand(RemoveSource, _ => _service is not null && !_isSample);
        ApproveAgentCommand = new RelayCommand(p => SetAgentState(p, "approve"), _ => _service is not null && !_isSample);
        DisableAgentCommand = new RelayCommand(p => SetAgentState(p, "disable"), _ => _service is not null && !_isSample);
        ForgetAgentCommand = new RelayCommand(p => SetAgentState(p, "forget"), _ => _service is not null && !_isSample);
    }

    public ObservableCollection<MailboxActivity> Activity { get; } = new();

    /// <summary>Phase 8: every agent fingerprint the app has seen, with the owner's decision about it.</summary>
    public ObservableCollection<MailboxAgentRow> Agents { get; } = new();

    /// <summary>Phase 8 owner addition: every repository + branch the mailbox listens to.</summary>
    public ObservableCollection<MailboxSourceRow> Sources { get; } = new();

    public RelayCommand RestartCommand { get; }
    public RelayCommand PollNowCommand { get; }
    public RelayCommand AddSourceCommand { get; }
    public RelayCommand RemoveSourceCommand { get; }
    public RelayCommand ApproveAgentCommand { get; }
    public RelayCommand DisableAgentCommand { get; }
    public RelayCommand ForgetAgentCommand { get; }

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    /// <summary>off | listening | active | error | stopped — drives the colour in XAML.</summary>
    public string StatusKind { get => _statusKind; private set { if (SetProperty(ref _statusKind, value)) { OnPropertyChanged(nameof(IsActive)); RelayCommand.RaiseCanExecuteChanged(); } } }
    public bool IsActive => _statusKind == "active";
    public string AgentLabel { get => _agentLabel; private set => SetProperty(ref _agentLabel, value); }
    public string AgentFingerprint { get => _agentFingerprint; private set => SetProperty(ref _agentFingerprint, value); }
    public string AppFingerprint { get => _appFingerprint; private set => SetProperty(ref _appFingerprint, value); }
    public string LastPollText { get => _lastPollText; private set => SetProperty(ref _lastPollText, value); }
    public bool HasActivity => Activity.Count > 0;
    public bool HasAgents => Agents.Count > 0;
    public bool HasSources => Sources.Count > 0;

    /// <summary>The branch URL the owner typed; pressing «افزودن شاخه» registers it as one more mailbox source.</summary>
    public string NewSourceUrl
    {
        get => _newSourceUrl;
        set { if (SetProperty(ref _newSourceUrl, value)) RelayCommand.RaiseCanExecuteChanged(); }
    }

    /// <summary>Persian feedback line under the add-branch box.</summary>
    public string SourceMessage { get => _sourceMessage; private set => SetProperty(ref _sourceMessage, value); }

    /// <summary>Set by the host: parses + persists a branch URL, and removes one by key. Both return null on success.</summary>
    public (Func<string, string?> Add, Func<string, string?> Remove)? SourceHandlers { get; set; }
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
            MailboxStatus.PendingApproval => "listening",
            MailboxStatus.Active => "active",
            MailboxStatus.Error => "error",
            MailboxStatus.Stopped => "stopped",
            _ => "off"
        };
        StatusText = status switch
        {
            MailboxStatus.Listening => "در حال گوش‌دادن — هنوز ایجنتی هویتش را نگذاشته است.",
            MailboxStatus.PendingApproval => string.IsNullOrEmpty(_service.StatusText) ? "ایجنت تازه در انتظار تأیید شماست؛ فرمانی اجرا نمی‌شود." : _service.StatusText,
            MailboxStatus.Active => "ایجنت وصل است؛ فرمان‌هایش بی‌درنگ اجرا می‌شوند.",
            MailboxStatus.Error => string.IsNullOrEmpty(_service.StatusText) ? "دسترسی به صندوق فرمان ممکن نشد؛ دوباره تلاش می‌شود." : _service.StatusText,
            MailboxStatus.Stopped => "گوش‌دادن متوقف است.",
            _ => string.IsNullOrEmpty(_service.StatusText) ? "صندوق فرمان خاموش است." : "خاموش — " + _service.StatusText
        };
        AgentLabel = _service.AgentLabel;
        AgentFingerprint = _service.AgentFingerprint;
        AppFingerprint = _service.AppFingerprint;
        LastPollText = _service.LastPoll is { } p ? "آخرین بررسی صندوق: " + p.ToString("HH:mm:ss") : "";
        RefreshAgents();
        RefreshSources();
        OnPropertyChanged(nameof(RestartText));
        RelayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Rebuilds the agent rows (approved first, then pending, then disabled).</summary>
    private void RefreshAgents()
    {
        if (_service is null) return;
        var rows = _service.Agents.Select(record => new MailboxAgentRow
        {
            Fingerprint = record.Fingerprint,
            Label = string.IsNullOrWhiteSpace(record.Label) ? "ایجنت بی‌نام" : record.Label,
            SourceText = SourceTitleOf(record.SourceKey),
            FirstSeenText = "نخستین بار: " + record.FirstSeenAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm"),
            StateText = record.State switch
            {
                AgentTrustState.Approved => "تأییدشده — فرمان‌ها اجرا می‌شوند",
                AgentTrustState.Disabled => "غیرفعال — فرمان‌ها رد می‌شوند",
                _ => "در انتظار تأیید مالک — فرمان‌ها رد می‌شوند"
            },
            StateKind = record.State switch
            {
                AgentTrustState.Approved => "active",
                AgentTrustState.Disabled => "error",
                _ => "listening"
            },
            IsApproved = record.State == AgentTrustState.Approved,
            IsPending = record.State == AgentTrustState.Pending,
            IsDisabled = record.State == AgentTrustState.Disabled
        }).ToList();
        Agents.Clear();
        foreach (var row in rows) Agents.Add(row);
        OnPropertyChanged(nameof(HasAgents));
    }

    /// <summary>Rebuilds the branch rows; the primary branch is marked and cannot be removed from this list.</summary>
    private void RefreshSources()
    {
        if (_service is null) return;
        var primary = _service.PrimarySourceKey;
        var rows = _service.SourceStates.Select(state => new MailboxSourceRow
        {
            Key = state.Key,
            Title = state.Label,
            Url = $"https://github.com/{state.Repository}/tree/{Uri.EscapeDataString(state.Branch)}",
            StatusText = string.IsNullOrEmpty(state.AgentFingerprint) ? state.StatusText : state.StatusText + " · " + state.AgentFingerprint,
            AgentText = string.IsNullOrEmpty(state.AgentLabel) ? "" : "ایجنت: " + state.AgentLabel,
            IsPrimary = string.Equals(state.Key, primary, StringComparison.OrdinalIgnoreCase),
            CanRemove = !string.Equals(state.Key, primary, StringComparison.OrdinalIgnoreCase)
        }).ToList();
        Sources.Clear();
        foreach (var row in rows) Sources.Add(row);
        OnPropertyChanged(nameof(HasSources));
    }

    private string SourceTitleOf(string key)
    {
        if (_service is null) return key;
        var match = _service.SourceStates.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
        return match is null ? key : $"{match.Repository} · {match.Branch}";
    }

    private void AddSource()
    {
        if (SourceHandlers is not { } handlers) { SourceMessage = "افزودن شاخه در این حالت ممکن نیست."; return; }
        var error = handlers.Add(NewSourceUrl);
        if (error is null)
        {
            SourceMessage = "شاخه ثبت شد؛ صندوق فرمان از همین حالا پیام‌های آن شاخه را هم می‌خواند.";
            NewSourceUrl = "";
            Refresh();
        }
        else SourceMessage = error;
    }

    private void RemoveSource(object? parameter)
    {
        if (parameter is not MailboxSourceRow row) return;
        if (SourceHandlers is not { } handlers) { SourceMessage = "حذف شاخه در این حالت ممکن نیست."; return; }
        var error = handlers.Remove(row.Key);
        SourceMessage = error ?? "شاخه از فهرست برداشته شد.";
        if (error is null) Refresh();
    }

    private void SetAgentState(object? parameter, string action)
    {
        if (_service is null || parameter is not MailboxAgentRow row) return;
        var done = action switch
        {
            "approve" => _service.ApproveAgent(row.Fingerprint),
            "disable" => _service.DisableAgent(row.Fingerprint),
            "forget" => _service.ForgetAgent(row.Fingerprint),
            _ => false
        };
        SourceMessage = done
            ? action switch
            {
                "approve" => "اثر انگشت تأیید شد؛ از این پس فرمان‌هایش اجرا می‌شود.",
                "disable" => "اثر انگشت غیرفعال شد؛ فرمان‌هایش رد می‌شود.",
                _ => "اثر انگشت از دفتر برنامه حذف شد."
            }
            : "این اثر انگشت در دفتر برنامه پیدا نشد.";
        Refresh();
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
        Agents.Add(new MailboxAgentRow
        {
            Fingerprint = "7f3a:91c2:0be4:5d18",
            Label = "Arena agent — نشست ۲۷ سپتامبر",
            SourceText = "paymanshafayan/bazino-gamenet-portal · main",
            FirstSeenText = "نخستین بار: 2026/09/27 14:02",
            StateText = "تأییدشده — فرمان‌ها اجرا می‌شوند",
            StateKind = "active",
            IsApproved = true
        });
        Sources.Add(new MailboxSourceRow
        {
            Key = "sample",
            Title = "شاخهٔ اصلی",
            Url = "https://github.com/paymanshafayan/bazino-gamenet-portal/tree/main",
            StatusText = "ایجنت «Arena agent» وصل و تأییدشده است",
            AgentText = "ایجنت: Arena agent",
            IsPrimary = true
        });
        OnPropertyChanged(nameof(HasAgents));
        OnPropertyChanged(nameof(HasSources));
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

/// <summary>One row of the «ایجنت‌های ثبت‌شده» list (phase 8).</summary>
public sealed class MailboxAgentRow
{
    public string Fingerprint { get; init; } = "";
    public string Label { get; init; } = "";
    public string SourceText { get; init; } = "";
    public string FirstSeenText { get; init; } = "";
    public string StateText { get; init; } = "";
    /// <summary>active | listening | error — drives the same colour dot the mailbox card uses.</summary>
    public string StateKind { get; init; } = "listening";
    public bool IsApproved { get; init; }
    public bool IsPending { get; init; }
    public bool IsDisabled { get; init; }
}

/// <summary>One row of the «شاخه‌های ثبت‌شده» list (phase 8 owner addition, 2026-10-04).</summary>
public sealed class MailboxSourceRow
{
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string Url { get; init; } = "";
    public string StatusText { get; init; } = "";
    public string AgentText { get; init; } = "";
    public bool IsPrimary { get; init; }
    public bool CanRemove { get; init; }
}
