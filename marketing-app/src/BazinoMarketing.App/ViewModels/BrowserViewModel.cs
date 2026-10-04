using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using BazinoMarketing.App.Infrastructure;
using BazinoMarketing.App.Services;
using BazinoMarketing.Core.Browser;
using BazinoMarketing.Core.Settings;

namespace BazinoMarketing.App.ViewModels;

/// <summary>
/// صفحهٔ «مرورگر»: همین برنامه به مرورگر کرومِ خود مالک وصل می‌شود و متن صفحهٔ باز را می‌خواند.
/// هیچ برنامهٔ جدا، هیچ سرور واسط و هیچ ابری در میان نیست؛ مالک با بستن برنامه یا کروم، دسترسی را می‌بندد.
/// </summary>
public sealed class BrowserViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Dispatcher _dispatcher;
    private readonly BrowserBridge _bridge;

    private bool _isBusy;
    private string _statusText = "برای شروع، وضعیت را نوسازی کنید.";
    private bool _chromeRunning;
    private string _url = "";
    private string _pageText = "";
    private string _pageTitle = "";
    private string _message = "";
    private BrowserTarget? _selectedTab;
    private string _portText = "9334";
    private string _chromePath = "";

    public BrowserViewModel(AppServices services)
    {
        _services = services;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _bridge = services.Browser;
        Tabs = new ObservableCollection<BrowserTarget>();

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        LaunchCommand = new AsyncRelayCommand(LaunchAsync, () => !IsBusy);
        SelectCommand = new AsyncRelayCommand(SelectAsync, () => !IsBusy && SelectedTab is not null);
        OpenCommand = new AsyncRelayCommand(OpenAsync, () => !IsBusy);
        ReadCommand = new AsyncRelayCommand(ReadAsync, () => !IsBusy);
        CopyTextCommand = new RelayCommand(CopyPageText);
        SaveSettingsCommand = new RelayCommand(SaveSettings);
        DetachCommand = new RelayCommand(Detach);

        _portText = _services.Settings.Browser.Port.ToString();
        _chromePath = _services.Settings.Browser.ChromePath;
    }

    public ObservableCollection<BrowserTarget> Tabs { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand LaunchCommand { get; }
    public AsyncRelayCommand SelectCommand { get; }
    public AsyncRelayCommand OpenCommand { get; }
    public AsyncRelayCommand ReadCommand { get; }
    public RelayCommand CopyTextCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }
    public RelayCommand DetachCommand { get; }

    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) RelayCommand.RaiseCanExecuteChanged(); } }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public bool ChromeRunning { get => _chromeRunning; private set => SetProperty(ref _chromeRunning, value); }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public string PageText { get => _pageText; private set => SetProperty(ref _pageText, value); }
    public string PageTitle { get => _pageTitle; private set => SetProperty(ref _pageTitle, value); }
    public string SelectedTargetText => _bridge.SelectedTargetId is null ? "هیچ تبی انتخاب نشده" : "تب انتخاب‌شده: " + _bridge.SelectedTargetId;

    /// <summary>The URL box. Also the tab the agent currently works in.</summary>
    public string Url { get => _url; set => SetProperty(ref _url, value); }
    public BrowserTarget? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (!SetProperty(ref _selectedTab, value)) return;
            RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public string PortText { get => _portText; set => SetProperty(ref _portText, value); }
    public string ChromePath { get => _chromePath; set => SetProperty(ref _chromePath, value); }

    public string ExplainText =>
        "اینجنت با همین برنامه به مرورگر خودِ شما وصل می‌شود و فقط متنِ همان صفحهٔ باز را می‌خواند. " +
        "هر نشانی http یا https باز می‌شود؛ رمز، کوکی و اطلاعات ذخیره‌شدهٔ مرورگر همچنان خوانده یا فرستاده نمی‌شود. " +
        "بستن برنامه یعنی قطع کامل دسترسی.";

    /// <summary>Called once when the app opens: honours the owner's "open Chrome with the app" switch, then reads the status.</summary>
    public async Task PrepareAsync()
    {
        if (!_services.Settings.Browser.AutoLaunch)
        {
            await RefreshAsync().ConfigureAwait(false);
            return;
        }
        var running = await Task.Run(() => _bridge.IsChromeRunningAsync(CancellationToken.None)).ConfigureAwait(false);
        if (running) await RefreshAsync().ConfigureAwait(false);
        else await LaunchAsync().ConfigureAwait(false);
    }

    /// <summary>Fills the page with believable fake content for --render-screenshots (sample mode only; no Chrome is contacted).</summary>
    public void SetSample(bool chromeRunning, string statusText, BrowserTarget[] tabs, string url, string title, string pageText)
    {
        ChromeRunning = chromeRunning;
        StatusText = statusText;
        Tabs.Clear();
        foreach (var tab in tabs) Tabs.Add(tab);
        Url = url;
        PageTitle = title;
        PageText = pageText;
        Message = "";
    }

    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var result = await Task.Run(() => _bridge.StatusAsync(CancellationToken.None)).ConfigureAwait(false);
            ChromeRunning = result.Value?["chromeRunning"]?.GetValue<bool>() ?? false;
            var tabs = await Task.Run(() => _bridge.ListTargetsAsync(CancellationToken.None)).ConfigureAwait(false);
            _dispatcher.Invoke(() =>
            {
                Tabs.Clear();
                foreach (var tab in tabs) Tabs.Add(tab);
            });
            StatusText = ChromeRunning
                ? $"کروم باز است و {tabs.Count} تب قابل خواندن دارد."
                : "کروم با پورت اشکال‌یابی باز نیست. دکمهٔ «باز کردن کروم» را بزنید.";
            _services.Info("browser", "status", "وضعیت مرورگر نوسازی شد", StatusText);
        }
        catch (Exception ex)
        {
            StatusText = "وضعیت مرورگر خوانده نشد: " + ex.Message;
            _services.Error("browser", "status", "خواندن وضعیت مرورگر ناموفق بود", ex.ToString(), "browser_status_failed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LaunchAsync()
    {
        IsBusy = true;
        Message = "در حال باز کردن کروم…";
        try
        {
            var result = await Task.Run(() =>
            {
                if (!OperatingSystem.IsWindows()) return BrowserResult.Fail("باز کردن خودکار کروم فقط روی ویندوز ممکن است.");
                var process = _bridge.TryLaunchChrome();
                return process is null
                    ? BrowserResult.Fail("کروم پیدا نشد. مسیر کامل chrome.exe را در کادر پایین بنویسید و دوباره تلاش کنید.")
                    : BrowserResult.Good(null);
            }).ConfigureAwait(false);
            Message = result.Ok ? "کروم باز شد. حالا «وضعیت» را نوسازی کنید و یک تب را انتخاب کنید." : result.Error ?? "";
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Message = "باز کردن کروم ناموفق بود: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SelectAsync()
    {
        var tab = SelectedTab;
        if (tab is null) return;
        IsBusy = true;
        Message = "";
        try
        {
            var result = await Task.Run(() => _bridge.SelectAsync(tab.Id, CancellationToken.None)).ConfigureAwait(false);
            Message = result.Ok
                ? $"به تب «{tab.Title}» وصل شد."
                : result.Error ?? "اتصال به تب ممکن نشد.";
            Url = tab.Url;
            OnPropertyChanged(nameof(SelectedTargetText));
            if (result.Ok) await ReadAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Message = "اتصال به تب ممکن نشد: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task OpenAsync()
    {
        var url = (Url ?? "").Trim();
        if (url.Length == 0)
        {
            Message = "نشانی‌ای وارد نشده.";
            return;
        }
        IsBusy = true;
        Message = "در حال باز کردن نشانی…";
        try
        {
            var result = await Task.Run(() => _bridge.OpenAsync(url, CancellationToken.None)).ConfigureAwait(false);
            Message = result.Ok ? "صفحه باز شد و متنش خوانده شد." : result.Error ?? "باز کردن نشانی ممکن نشد.";
            if (result.Ok) await ReadAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Message = "باز کردن نشانی ممکن نشد: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ReadAsync()
    {
        IsBusy = true;
        try
        {
            var result = await Task.Run(() => _bridge.ReadAsync(_services.Settings.Browser.MaxReadChars, CancellationToken.None)).ConfigureAwait(false);
            if (result.Ok && result.Value is not null)
            {
                PageTitle = result.Value["title"]?.GetValue<string>() ?? "";
                PageText = result.Value["text"]?.GetValue<string>() ?? "";
                var total = result.Value["totalChars"]?.GetValue<int>() ?? 0;
                var truncated = result.Value["truncated"]?.GetValue<bool>() ?? false;
                Message = $"متن صفحه خوانده شد ({total} کاراکتر)" + (truncated ? " — بخش پایانی برای نمایش کوتاه شده است." : ".");
            }
            else
            {
                Message = result.Error ?? "خواندن متن صفحه ممکن نشد.";
            }
        }
        catch (Exception ex)
        {
            Message = "خواندن متن صفحه ممکن نشد: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CopyPageText()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(PageText))
            {
                Message = "متنی برای کپی نیست.";
                return;
            }
            Clipboard.SetText(PageText);
            Message = "متن صفحه کپی شد.";
        }
        catch (Exception ex)
        {
            Message = "کپی ممکن نشد: " + ex.Message;
        }
    }

    private void Detach()
    {
        _ = Task.Run(async () => await _bridge.DetachAsync().ConfigureAwait(false));
        OnPropertyChanged(nameof(SelectedTargetText));
        Message = "ارتباط با تب قطع شد؛ تب خودش باز می‌ماند.";
    }

    /// <summary>Writes the owner's own choices (debugging port and Chrome path) into the settings file.</summary>
    private void SaveSettings()
    {
        var settings = _services.Settings;
        if (!int.TryParse((PortText ?? "").Trim(), out var port) || port is < 1024 or > 65535)
        {
            Message = "شمارهٔ پورت باید عددی بین ۱۰۲۴ و ۶۵۵۳۵ باشد.";
            return;
        }

        settings.Browser.Port = port;
        settings.Browser.ChromePath = (ChromePath ?? "").Trim();
        try
        {
            _services.SaveSettings();
            Message = "تنظیمات ذخیره شد؛ همهٔ نشانی‌های http و https قابل بازکردن‌اند.";
            _services.Info("browser", "settings", "تنظیمات مرورگر ذخیره شد", $"port={port} urlPolicy=any-http-https");
        }
        catch (Exception ex)
        {
            Message = "ذخیرهٔ تنظیمات ممکن نشد: " + ex.Message;
            _services.Error("browser", "settings", "ذخیرهٔ تنظیمات مرورگر ناموفق بود", ex.ToString(), "browser_settings_failed");
        }
    }

}
