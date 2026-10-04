using System.Windows;
using BazinoMarketing.App.Infrastructure;
using BazinoMarketing.App.Services;
using BazinoMarketing.Core.Secrets;
using BazinoMarketing.Core.Settings;
using BazinoMarketing.Core.Tools;

namespace BazinoMarketing.App.ViewModels;

/// <summary>Shared behaviour of every tool card: status, secret entry, proxy, permissions, save/test.</summary>
public abstract class ToolCardViewModel : ObservableObject
{
    protected readonly AppServices Services;

    private ToolState _state = ToolState.Unknown;
    private string _statusSummary = "هنوز بررسی نشده";
    private string _statusDetail = "";
    private DateTimeOffset? _lastChecked;
    private bool _isBusy;
    private string _secretInput = "";
    private bool _hasSecret;
    private string _proxyMode = "system";
    private string _proxyUrl = "";
    private bool _allowWrites, _allowPublish, _allowSpend;
    private string _saveMessage = "";
    private string _title;

    protected ToolCardViewModel(AppServices services, string id, string title, string description, string glyph)
    {
        Services = services;
        Id = id;
        _title = title;
        Description = description;
        Glyph = glyph;
        TestCommand = new AsyncRelayCommand(TestAsync, () => !IsBusy);
        SaveCommand = new RelayCommand(Save);
        ClearSecretCommand = new RelayCommand(ClearSecret, () => HasSecret || !string.IsNullOrEmpty(SecretInput));
    }

    public string Id { get; }
    public string Title { get => _title; protected set => SetProperty(ref _title, value); }
    public string Description { get; }
    /// <summary>Two-letter badge kept for compact places (no icon fonts required).</summary>
    public string Glyph { get; }

    /// <summary>
    /// Recognizable mark for this tool's badge (owner request 2026-10-03: every tool gets its own icon in its brand
    /// colour). Simple unicode marks are used on purpose: no icon font and no licensed logo files, so the badge can
    /// never break or pull anything from the network. If the owner supplies the official logo files, they drop in here.
    /// </summary>
    public virtual string Icon => "🔧";

    /// <summary>Brand colour of the badge behind <see cref="Icon"/>.</summary>
    public virtual System.Windows.Media.Brush BrandBrush => Brush("BrandNeutralBrush");

    protected static System.Windows.Media.Brush Brush(string key) =>
        System.Windows.Application.Current?.TryFindResource(key) as System.Windows.Media.Brush
        ?? System.Windows.Media.Brushes.Gray;

    /// <summary>Label of the card's main button.</summary>
    public string ActionText => IsBusy ? "در حال بررسی…" : "آزمایش اتصال";
    public virtual string LogTool => Id;

    public abstract bool SupportsSecret { get; }
    public virtual string SecretLabel => "کلید / توکن";
    public virtual string SecretHint => "";
    protected abstract string? SecretKey { get; }
    protected abstract ProxySettings ProxyModel { get; }
    protected abstract ToolPermissions PermissionsModel { get; }
    public virtual bool ShowPublishPermission => false;
    public virtual bool ShowSpendPermission => false;
    /// <summary>Optional extra action shown next to "test" on the connection page (e.g. Kling login).</summary>
    public virtual System.Windows.Input.ICommand? SecondaryCommand => null;
    public virtual string SecondaryCommandText => "";
    public bool HasSecondaryCommand => SecondaryCommand is not null;

    public AsyncRelayCommand TestCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand ClearSecretCommand { get; }

    public ToolState State { get => _state; protected set { if (SetProperty(ref _state, value)) { OnPropertyChanged(nameof(StateText)); OnPropertyChanged(nameof(IsConnected)); } } }
    public string StateText => ToolStateToTextConverter.Text(State);
    public bool IsConnected => State == ToolState.Connected;
    public string StatusSummary { get => _statusSummary; protected set => SetProperty(ref _statusSummary, value); }
    public string StatusDetail { get => _statusDetail; protected set => SetProperty(ref _statusDetail, value); }
    public DateTimeOffset? LastChecked { get => _lastChecked; protected set { if (SetProperty(ref _lastChecked, value)) OnPropertyChanged(nameof(LastCheckedText)); } }
    public string LastCheckedText => LastChecked is { } t ? "آخرین بررسی: " + t.ToLocalTime().ToString("HH:mm:ss") : "";
    public bool IsBusy { get => _isBusy; protected set { if (SetProperty(ref _isBusy, value)) RelayCommand.RaiseCanExecuteChanged(); } }

    public string SecretInput { get => _secretInput; set { if (SetProperty(ref _secretInput, value)) OnPropertyChanged(nameof(SecretStatusText)); } }
    public bool HasSecret { get => _hasSecret; protected set { if (SetProperty(ref _hasSecret, value)) OnPropertyChanged(nameof(SecretStatusText)); } }
    public string SecretStatusText => !SupportsSecret ? "" :
        !string.IsNullOrEmpty(SecretInput) ? "مقدار جدید وارد شده — برای ثبت «ذخیره» را بزنید" :
        HasSecret ? "ذخیره شده (رمزگذاری‌شده با DPAPI؛ نمایش داده نمی‌شود)" : "ثبت نشده";

    public string ProxyMode { get => _proxyMode; set { if (SetProperty(ref _proxyMode, value)) OnPropertyChanged(nameof(IsCustomProxy)); } }
    public bool IsCustomProxy => ProxyMode == "custom";
    public string ProxyUrl { get => _proxyUrl; set => SetProperty(ref _proxyUrl, value); }
    public bool AllowWrites { get => _allowWrites; set => SetProperty(ref _allowWrites, value); }
    public bool AllowPublish { get => _allowPublish; set => SetProperty(ref _allowPublish, value); }
    public bool AllowSpend { get => _allowSpend; set => SetProperty(ref _allowSpend, value); }
    public string SaveMessage { get => _saveMessage; protected set => SetProperty(ref _saveMessage, value); }

    protected abstract Task<ToolCheckResult> RunCheckAsync(CancellationToken ct);
    protected abstract void LoadFields();
    protected abstract void ApplyFields();

    public void Load()
    {
        LoadFields();
        var p = ProxyModel;
        ProxyMode = string.IsNullOrWhiteSpace(p.Mode) ? "system" : p.Mode;
        ProxyUrl = p.Url;
        AllowWrites = PermissionsModel.AllowWrites;
        AllowPublish = PermissionsModel.AllowPublish;
        AllowSpend = PermissionsModel.AllowSpend;
        RefreshSecretPresence();
    }

    protected void RefreshSecretPresence() => HasSecret = SecretKey is { } k && Services.Secrets.Has(k);

    protected string CurrentSecret()
    {
        if (!string.IsNullOrEmpty(SecretInput)) return SecretInput.Trim();
        return SecretKey is { } k ? Services.Secrets.GetOrEmpty(k) : "";
    }

    public virtual void Save()
    {
        try
        {
            ApplyFields();
            ProxyModel.Mode = ProxyMode;
            ProxyModel.Url = ProxyUrl?.Trim() ?? "";
            PermissionsModel.AllowWrites = AllowWrites;
            PermissionsModel.AllowPublish = AllowPublish;
            PermissionsModel.AllowSpend = AllowSpend;

            var secretChanged = false;
            if (SupportsSecret && SecretKey is { } key && !string.IsNullOrWhiteSpace(SecretInput))
            {
                if (!Services.Secrets.IsAvailable)
                {
                    SaveMessage = Services.Secrets.UnavailableReason ?? "ذخیرهٔ امن در دسترس نیست";
                    return;
                }
                Services.Secrets.Set(key, SecretInput.Trim());
                SecretInput = "";
                secretChanged = true;
            }
            Services.SaveSettings();
            RefreshSecretPresence();
            SaveMessage = secretChanged ? "تنظیمات و کلید ذخیره شدند" : "تنظیمات ذخیره شدند";
            Services.Info(LogTool, "settings.save", $"تنظیمات «{Title}» ذخیره شد", secretChanged ? "key-updated" : "key-unchanged");
            App.MailboxPollNow();
        }
        catch (Exception ex)
        {
            SaveMessage = "ذخیره ناموفق بود: " + ex.Message;
            Services.Error(LogTool, "settings.save", $"ذخیرهٔ «{Title}» ناموفق بود", ex.ToString(), "save_failed");
        }
    }

    public void ClearSecret()
    {
        if (!string.IsNullOrEmpty(SecretInput)) { SecretInput = ""; }
        if (SecretKey is { } key && Services.Secrets.Has(key))
        {
            if (MessageBox.Show($"کلید ذخیره‌شدهٔ «{Title}» حذف شود؟", "حذف کلید", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                    MessageBoxResult.No, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign) != MessageBoxResult.Yes)
                return;
            Services.Secrets.Remove(key);
            RefreshSecretPresence();
            SaveMessage = "کلید حذف شد";
            Services.Warn(LogTool, "secret.clear", $"کلید «{Title}» حذف شد");
            SetResult(ToolCheckResult.NotConfigured("کلید حذف شد"));
        }
    }

    public async Task TestAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        State = ToolState.Checking;
        StatusSummary = "در حال بررسی…";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(150));
            var result = await ToolCheckResult.WithRetryAsync(RunCheckAsync, cts.Token);
            SetResult(result);
            var msg = $"بررسی «{Title}»: {result.Summary}";
            if (result.IsOk) Services.Success(LogTool, "tool.test", msg, result.Detail, result.DurationMs);
            else if (result.State == ToolState.NotConfigured) Services.Info(LogTool, "tool.test", msg, result.Detail);
            else Services.Error(LogTool, "tool.test", msg, result.Detail, result.ErrorCode, result.DurationMs);
        }
        catch (Exception ex)
        {
            SetResult(ToolCheckResult.FromException(ex, Id));
            Services.Error(LogTool, "tool.test", $"بررسی «{Title}» با خطا متوقف شد", ex.ToString(), "exception");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SetResult(ToolCheckResult result)
    {
        State = result.State;
        StatusSummary = result.Summary;
        StatusDetail = Services.Redactor.Redact(result.Detail);
        LastChecked = DateTimeOffset.Now;
    }

    /// <summary>Used by the screenshot renderer: a believable state without touching the network.</summary>
    public void SetSample(ToolState state, string summary, string detail = "")
    {
        State = state;
        StatusSummary = summary;
        StatusDetail = detail;
        LastChecked = DateTimeOffset.Now;
    }
}

public sealed class GitHubCardViewModel : ToolCardViewModel
{
    private string _repository = "", _branch = "", _mailboxPath = "";
    private int _pollSeconds = 8;

    public override string Icon => "🐙";
    public override System.Windows.Media.Brush BrandBrush => Brush("BrandGitHubBrush");

    public GitHubCardViewModel(AppServices services)
        : base(services, "github", "GitHub (صندوق فرمان)", "مخزن و شاخه‌ای که ایجنت و برنامه از طریق آن پیام ردوبدل می‌کنند.", "GH")
    {
        OpenTokenPageCommand = new RelayCommand(() => AppServices.OpenUrl("https://github.com/settings/tokens?type=beta"));
    }

    public RelayCommand OpenTokenPageCommand { get; }
    public string Repository { get => _repository; set => SetProperty(ref _repository, value); }
    public string Branch { get => _branch; set => SetProperty(ref _branch, value); }
    public string MailboxPath { get => _mailboxPath; set => SetProperty(ref _mailboxPath, value); }
    public int PollSeconds { get => _pollSeconds; set => SetProperty(ref _pollSeconds, value); }
    public string PollSecondsText { get => PollSeconds.ToString(); set { if (int.TryParse(value, out var n)) PollSeconds = n; } }

    public override bool SupportsSecret => true;
    public override string SecretLabel => "توکن دسترسی GitHub (fine-grained یا classic با دسترسی contents: write)";
    public override string SecretHint => "توکن فقط روی همین رایانه و با DPAPI ذخیره می‌شود.";
    protected override string? SecretKey => SecretKeys.GitHubToken;
    protected override ProxySettings ProxyModel => Services.Settings.GitHub.Proxy;
    protected override ToolPermissions PermissionsModel => Services.Settings.GitHub.Permissions;

    protected override void LoadFields()
    {
        var g = Services.Settings.GitHub;
        Repository = g.Repository; Branch = g.Branch; MailboxPath = g.MailboxPath; PollSeconds = g.PollSeconds;
        OnPropertyChanged(nameof(PollSecondsText));
    }

    protected override void ApplyFields()
    {
        var g = Services.Settings.GitHub;
        g.Repository = Repository.Trim(); g.Branch = Branch.Trim(); g.MailboxPath = MailboxPath.Trim().Trim('/');
        g.PollSeconds = Math.Clamp(PollSeconds, 3, 300);
    }

    protected override Task<ToolCheckResult> RunCheckAsync(CancellationToken ct)
    {
        var snapshot = new GitHubSettings
        {
            Repository = Repository.Trim(), Branch = Branch.Trim(), MailboxPath = MailboxPath, PollSeconds = PollSeconds,
            Proxy = new ProxySettings { Mode = ProxyMode, Url = ProxyUrl }
        };
        return GitHubClient.CheckAsync(snapshot, CurrentSecret(), ct);
    }
}

public sealed class KlingCardViewModel : ToolCardViewModel
{
    private string _region = "global", _cliPath = "", _nodePath = "", _locationText = "", _environmentWarning = "";
    private int _detectSerial;

    public override string Icon => "🎬";
    public override System.Windows.Media.Brush BrandBrush => Brush("BrandKlingBrush");

    public KlingCardViewModel(AppServices services)
        : base(services, "kling", "Kling (ویدئو)", "تولید ویدئو با CLI رسمی Kling. ورود فقط از طریق مرورگر و دستور kling login انجام می‌شود.", "KL")
    {
        InstallCliCommand = new RelayCommand(InstallCli);
        LoginCommand = new RelayCommand(Login);
        LogoutCommand = new RelayCommand(Logout);
        DetectCommand = new RelayCommand(Detect);
    }

    public RelayCommand InstallCliCommand { get; }
    public RelayCommand LoginCommand { get; }
    public RelayCommand LogoutCommand { get; }
    public RelayCommand DetectCommand { get; }
    public override System.Windows.Input.ICommand? SecondaryCommand => LoginCommand;
    public override string SecondaryCommandText => "ورود به Kling";

    public string Region { get => _region; set => SetProperty(ref _region, value); }
    public string CliPath { get => _cliPath; set => SetProperty(ref _cliPath, value); }
    public string NodePath { get => _nodePath; set => SetProperty(ref _nodePath, value); }
    public string LocationText { get => _locationText; private set => SetProperty(ref _locationText, value); }
    /// <summary>Persian note about proxy/Node compatibility (empty when there is nothing to warn about).</summary>
    public string EnvironmentWarning { get => _environmentWarning; private set => SetProperty(ref _environmentWarning, value); }
    public string InstallCommandText => KlingCli.InstallCommand(Region);

    public override bool SupportsSecret => false;
    protected override string? SecretKey => null;
    protected override ProxySettings ProxyModel => Services.Settings.Kling.Proxy;
    protected override ToolPermissions PermissionsModel => Services.Settings.Kling.Permissions;
    public override bool ShowSpendPermission => true;

    protected override void LoadFields()
    {
        var k = Services.Settings.Kling;
        Region = k.Region; CliPath = k.CliPath; NodePath = k.NodePath;
        OnPropertyChanged(nameof(InstallCommandText));
        if (!Services.IsSampleMode) Detect();
    }

    protected override void ApplyFields()
    {
        var k = Services.Settings.Kling;
        k.Region = Region == "cn" ? "cn" : "global"; k.CliPath = CliPath.Trim(); k.NodePath = NodePath.Trim();
        OnPropertyChanged(nameof(InstallCommandText));
        Detect();
    }

    private KlingSettings Snapshot() => new()
    {
        Region = Region, CliPath = CliPath.Trim(), NodePath = NodePath.Trim(),
        Proxy = new ProxySettings { Mode = ProxyMode, Url = ProxyUrl }
    };

    /// <summary>Finds CLI/Node/proxy in the background (node --version is a short process) and updates the card.</summary>
    public async void Detect()
    {
        var serial = ++_detectSerial;
        var snapshot = Snapshot();
        var quick = KlingCli.Locate(snapshot);
        LocationText = (quick.CliFound ? "CLI: " + quick.CliPath : "CLI: not found") + "   |   " + (quick.NodeFound ? "Node: " + quick.NodePath : "Node: not found");
        try
        {
            var env = await Task.Run(() => KlingCli.InspectAsync(snapshot));
            if (serial != _detectSerial) return;
            LocationText = env.Describe();
            EnvironmentWarning = env.Warning();
        }
        catch (Exception ex)
        {
            App.ReportUnexpected(ex, "kling-detect");
        }
    }

    public void SetSampleLocation(string text, string warning = "") { LocationText = text; EnvironmentWarning = warning; }

    protected override Task<ToolCheckResult> RunCheckAsync(CancellationToken ct) => KlingCli.CheckAsync(Snapshot(), ct);

    private IReadOnlyDictionary<string, string?> ConsoleEnvironment() => KlingCli.ChildEnvironment(Snapshot().Proxy);

    private void InstallCli()
    {
        var env = ConsoleEnvironment();
        Services.Info(LogTool, "cli.install", "پنجرهٔ نصب Kling CLI باز شد", KlingCli.InstallCommand(Region) + ProxyDetail(env));
        var process = AppServices.RunInVisibleConsole(KlingCli.InstallCommand(Region), "Bazino - Kling CLI install", env);
        WatchConsole(process, "cli.install", pollLocation: true);
    }

    private void Login()
    {
        var loc = KlingCli.Locate(Snapshot());
        var cmd = loc.CliFound ? "\"" + loc.CliPath + "\" login" : KlingCli.LoginCommand;
        var env = ConsoleEnvironment();
        Services.Info(LogTool, "cli.login", "پنجرهٔ ورود Kling باز شد (مرورگر باز می‌شود)", ProxyDetail(env).TrimStart(' ', '|'));
        var process = AppServices.RunInVisibleConsole(cmd, "Bazino - Kling login", env);
        WatchConsole(process, "cli.login", pollLocation: false);
    }

    private void Logout()
    {
        var loc = KlingCli.Locate(Snapshot());
        var cmd = loc.CliFound ? "\"" + loc.CliPath + "\" logout" : "kling logout";
        Services.Warn(LogTool, "cli.logout", "خروج از Kling درخواست شد");
        var process = AppServices.RunInVisibleConsole(cmd, "Bazino - Kling logout", ConsoleEnvironment());
        WatchConsole(process, "cli.logout", pollLocation: false);
    }

    private static string ProxyDetail(IReadOnlyDictionary<string, string?> env) =>
        env.TryGetValue("HTTPS_PROXY", out var p) && p is not null ? " | proxy=" + p + " NODE_USE_ENV_PROXY=1" : " | proxy=direct";

    /// <summary>
    /// While the console is open, keeps the CLI location fresh (npm install finishes long before the owner closes the
    /// window); when it closes, re-runs the health check so the card reflects the new state without a manual click.
    /// </summary>
    private void WatchConsole(System.Diagnostics.Process? process, string category, bool pollLocation)
    {
        if (process is null) return;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        void OnUi(Action a) { if (dispatcher is null || dispatcher.CheckAccess()) a(); else dispatcher.InvokeAsync(a); }

        var timer = pollLocation ? new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) } : null;
        if (timer is not null)
        {
            var started = DateTimeOffset.UtcNow;
            timer.Tick += (_, _) =>
            {
                try
                {
                    if (process.HasExited || DateTimeOffset.UtcNow - started > TimeSpan.FromMinutes(15)) { timer.Stop(); return; }
                    var loc = KlingCli.Locate(Snapshot());
                    if (loc.CliFound && !LocationText.Contains(loc.CliPath!, StringComparison.OrdinalIgnoreCase)) Detect();
                }
                catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
                {
                    timer.Stop();
                }
            };
            timer.Start();
        }

        process.Exited += (_, _) => OnUi(async () =>
        {
            timer?.Stop();
            try { Services.Info(LogTool, category, "پنجرهٔ فرمان بسته شد؛ وضعیت Kling دوباره بررسی می‌شود", $"exit={process.ExitCode}"); } catch { }
            process.Dispose();
            Detect();
            await TestAsync();
        });
        if (process.HasExited) { timer?.Stop(); Detect(); _ = TestAsync(); }
    }
}

public sealed class ZernioCardViewModel : ToolCardViewModel
{
    private string _baseUrl = "";

    public override string Icon => "🌐";
    public override System.Windows.Media.Brush BrandBrush => Brush("BrandZernioBrush");

    public ZernioCardViewModel(AppServices services)
        : base(services, "zernio", "Zernio (شبکه‌های اجتماعی)", "تنها کانال انتشار در شبکه‌های اجتماعی. انتشار فقط با مجوز صریح شما انجام می‌شود.", "ZR") { }

    public string BaseUrl { get => _baseUrl; set => SetProperty(ref _baseUrl, value); }
    public override bool SupportsSecret => true;
    public override string SecretLabel => "کلید API زرنیو";
    protected override string? SecretKey => SecretKeys.ZernioApiKey;
    protected override ProxySettings ProxyModel => Services.Settings.Zernio.Proxy;
    protected override ToolPermissions PermissionsModel => Services.Settings.Zernio.Permissions;
    public override bool ShowPublishPermission => true;

    protected override void LoadFields()
    {
        BaseUrl = Services.Settings.Zernio.BaseUrl;
    }

    protected override void ApplyFields() => Services.Settings.Zernio.BaseUrl = BaseUrl.Trim();

    protected override Task<ToolCheckResult> RunCheckAsync(CancellationToken ct) =>
        ZernioClient.CheckAsync(new ZernioSettings { BaseUrl = BaseUrl.Trim(), Proxy = new ProxySettings { Mode = ProxyMode, Url = ProxyUrl } }, CurrentSecret(), ct);
}

public sealed class FluxCardViewModel : ToolCardViewModel
{
    private string _provider = "none", _accountId = "", _baseUrl = "", _model = "", _testUrl = "";

    public override string Icon => "🎨";
    public override System.Windows.Media.Brush BrandBrush => Brush("BrandFluxBrush");

    public FluxCardViewModel(AppServices services)
        : base(services, "flux", "FLUX (تصویر)", "تولید تصویر با FLUX. ارائه‌دهنده را خودتان انتخاب می‌کنید؛ هیچ گزینهٔ پیش‌فرضی هزینه ایجاد نمی‌کند.", "FX") { }

    public string Provider { get => _provider; set { if (SetProperty(ref _provider, value)) { OnPropertyChanged(nameof(IsCloudflare)); OnPropertyChanged(nameof(IsGeneric)); } } }
    public bool IsCloudflare => Provider == "cloudflare";
    public bool IsGeneric => Provider == "generic";
    public string AccountId { get => _accountId; set => SetProperty(ref _accountId, value); }
    public string BaseUrl { get => _baseUrl; set => SetProperty(ref _baseUrl, value); }
    public string Model { get => _model; set => SetProperty(ref _model, value); }
    public string TestUrl { get => _testUrl; set => SetProperty(ref _testUrl, value); }

    public override bool SupportsSecret => true;
    public override string SecretLabel => "توکن API ارائه‌دهنده (مثلاً Cloudflare API Token با دسترسی Workers AI)";
    protected override string? SecretKey => SecretKeys.FluxApiKey;
    protected override ProxySettings ProxyModel => Services.Settings.Flux.Proxy;
    protected override ToolPermissions PermissionsModel => Services.Settings.Flux.Permissions;
    public override bool ShowSpendPermission => true;

    protected override void LoadFields()
    {
        var f = Services.Settings.Flux;
        Provider = f.Provider; AccountId = f.AccountId; BaseUrl = f.BaseUrl; Model = f.Model; TestUrl = f.TestUrl;
    }

    protected override void ApplyFields()
    {
        var f = Services.Settings.Flux;
        f.Provider = Provider; f.AccountId = AccountId.Trim(); f.BaseUrl = BaseUrl.Trim(); f.Model = Model.Trim(); f.TestUrl = TestUrl.Trim();
    }

    protected override Task<ToolCheckResult> RunCheckAsync(CancellationToken ct) =>
        FluxClient.CheckAsync(new FluxSettings
        {
            Provider = Provider, AccountId = AccountId.Trim(), BaseUrl = BaseUrl.Trim(), Model = Model.Trim(), TestUrl = TestUrl.Trim(),
            Proxy = new ProxySettings { Mode = ProxyMode, Url = ProxyUrl }
        }, CurrentSecret(), ct);
}

public sealed class GroqCardViewModel : ToolCardViewModel
{
    private string _baseUrl = "https://api.groq.com/openai/v1", _model = "whisper-large-v3", _language = "";

    public override string Icon => "⚡";
    public override System.Windows.Media.Brush BrandBrush => Brush("BrandGroqBrush");

    public GroqCardViewModel(AppServices services)
        : base(services, "groq", "Groq (تبدیل صدا به متن Whisper)", "پیاده‌سازی گفتار ویدیو و صوت با مدل‌های Whisper در Groq (۲٬۰۰۰ درخواست رایگان در روز) + پشتیبان خودکار Cloudflare.", "GQ")
    {
        OpenKeysPageCommand = new RelayCommand(() => AppServices.OpenUrl("https://console.groq.com/keys"));
    }

    public RelayCommand OpenKeysPageCommand { get; }
    public string BaseUrl { get => _baseUrl; set => SetProperty(ref _baseUrl, value); }
    public string Model { get => _model; set => SetProperty(ref _model, value); }
    public string Language { get => _language; set => SetProperty(ref _language, value); }

    public override bool SupportsSecret => true;
    public override string SecretLabel => "کلید API سرویس Groq (شروع با gsk_...)";
    public override string SecretHint => "در صورت خالی بودن، برنامه به‌طور خودکار از سرویس Cloudflare Whisper متصل استفاده می‌کند.";
    protected override string? SecretKey => SecretKeys.GroqApiKey;
    protected override ProxySettings ProxyModel => Services.Settings.Groq.Proxy;
    protected override ToolPermissions PermissionsModel => Services.Settings.Groq.Permissions;

    protected override void LoadFields()
    {
        // Migrate any key previously entered in the temporary custom 'groq' card
        if (!Services.Secrets.Has(SecretKeys.GroqApiKey))
        {
            var customKey = Services.Secrets.GetOrEmpty(SecretKeys.ForCustomCredential("groq")).Trim();
            if (!string.IsNullOrWhiteSpace(customKey))
                Services.Secrets.Set(SecretKeys.GroqApiKey, customKey);
        }

        var g = Services.Settings.Groq;
        BaseUrl = string.IsNullOrWhiteSpace(g.BaseUrl) ? "https://api.groq.com/openai/v1" : g.BaseUrl;
        Model = string.IsNullOrWhiteSpace(g.Model) ? "whisper-large-v3" : g.Model;
        Language = g.Language ?? "";
    }

    protected override void ApplyFields()
    {
        var g = Services.Settings.Groq;
        g.BaseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? "https://api.groq.com/openai/v1" : BaseUrl.Trim();
        g.Model = string.IsNullOrWhiteSpace(Model) ? "whisper-large-v3" : Model.Trim();
        g.Language = (Language ?? "").Trim();
        Services.Settings.Media.GroqModel = g.Model;
        Services.Settings.Media.TranscriptionLanguage = g.Language;
    }

    protected override Task<ToolCheckResult> RunCheckAsync(CancellationToken ct) =>
        GroqClient.CheckAsync(new GroqSettings
        {
            BaseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? "https://api.groq.com/openai/v1" : BaseUrl.Trim(),
            Model = string.IsNullOrWhiteSpace(Model) ? "whisper-large-v3" : Model.Trim(),
            Language = (Language ?? "").Trim(),
            Proxy = new ProxySettings { Mode = ProxyMode, Url = ProxyUrl }
        }, CurrentSecret(), ct);
}

public sealed class CustomCardViewModel : ToolCardViewModel
{
    public CustomCard Model { get; }
    private string _name, _kind, _baseUrl, _authType, _headerName, _testPath, _notes;

    public override string Icon => "🧩";
    public override System.Windows.Media.Brush BrandBrush => Brush("BrandNeutralBrush");

    public CustomCardViewModel(AppServices services, CustomCard model)
        : base(services, "custom:" + model.Id, string.IsNullOrWhiteSpace(model.Name) ? model.Id : model.Name,
            "سرویس سفارشی (HTTP یا MCP). فقط یک درخواست GET امن برای آزمایش ارسال می‌شود.", "SV")
    {
        Model = model;
        _name = model.Name; _kind = model.Kind; _baseUrl = model.BaseUrl; _authType = model.AuthType;
        _headerName = model.HeaderName; _testPath = model.TestPath; _notes = model.Notes;
    }

    public string Name { get => _name; set { if (SetProperty(ref _name, value)) Title = string.IsNullOrWhiteSpace(value) ? Model.Id : value; } }
    public string Kind { get => _kind; set => SetProperty(ref _kind, value); }
    public string BaseUrl { get => _baseUrl; set => SetProperty(ref _baseUrl, value); }
    public string AuthType { get => _authType; set { if (SetProperty(ref _authType, value)) { OnPropertyChanged(nameof(NeedsHeaderName)); OnPropertyChanged(nameof(SupportsSecret)); } } }
    public bool NeedsHeaderName => AuthType == "header";
    public string HeaderName { get => _headerName; set => SetProperty(ref _headerName, value); }
    public string TestPath { get => _testPath; set => SetProperty(ref _testPath, value); }
    public string Notes { get => _notes; set => SetProperty(ref _notes, value); }
    public string ImportedFromText => string.IsNullOrEmpty(Model.ImportedFrom) ? "" : "وارد شده از نرم‌افزار قدیمی";

    public override bool SupportsSecret => AuthType != "none";
    public override string SecretLabel => AuthType == "basic" ? "نام‌کاربری:رمز" : "کلید / توکن";
    protected override string? SecretKey => SecretKeys.ForCustomCredential(Model.Id);
    protected override ProxySettings ProxyModel => Model.Proxy;
    protected override ToolPermissions PermissionsModel => Model.Permissions;

    protected override void LoadFields()
    {
        Name = Model.Name; Kind = Model.Kind; BaseUrl = Model.BaseUrl; AuthType = Model.AuthType; HeaderName = Model.HeaderName; TestPath = Model.TestPath; Notes = Model.Notes;
    }

    protected override void ApplyFields()
    {
        Model.Name = Name.Trim(); Model.Kind = Kind == "mcp" ? "mcp" : "http"; Model.BaseUrl = BaseUrl.Trim(); Model.AuthType = AuthType;
        Model.HeaderName = HeaderName.Trim(); Model.TestPath = string.IsNullOrWhiteSpace(TestPath) ? "/" : TestPath.Trim(); Model.Notes = Notes.Trim();
    }

    protected override Task<ToolCheckResult> RunCheckAsync(CancellationToken ct)
    {
        var snapshot = new CustomCard
        {
            Id = Model.Id, Name = Name, Kind = Kind, BaseUrl = BaseUrl.Trim(), AuthType = AuthType, HeaderName = HeaderName.Trim(), TestPath = TestPath,
            Proxy = new ProxySettings { Mode = ProxyMode, Url = ProxyUrl }
        };
        return CustomCardClient.CheckAsync(snapshot, CurrentSecret(), ct);
    }
}
