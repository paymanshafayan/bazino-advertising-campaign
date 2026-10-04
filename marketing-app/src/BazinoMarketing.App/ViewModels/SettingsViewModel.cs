using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using BazinoMarketing.App.Infrastructure;
using BazinoMarketing.App.Services;
using BazinoMarketing.Core.Secrets;
using BazinoMarketing.Core.Settings;

namespace BazinoMarketing.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private string _legacySummary = "";
    private string _newCardName = "";
    private string _generalMessage = "";

    public SettingsViewModel(AppServices services, GitHubCardViewModel github, KlingCardViewModel kling, ZernioCardViewModel zernio, FluxCardViewModel flux, GroqCardViewModel groq)
    {
        _services = services;
        GitHub = github;
        Kling = kling;
        Zernio = zernio;
        PortalIngest = new PortalIngestSettingsViewModel(services);
        Flux = flux;
        Groq = groq;
        CustomCards = new ObservableCollection<CustomCardViewModel>(
            services.Settings.CustomCards.Select(c => { var vm = new CustomCardViewModel(services, c); vm.Load(); return vm; }));

        AddCustomCardCommand = new RelayCommand(AddCustomCard, () => !string.IsNullOrWhiteSpace(NewCardName));
        RemoveCustomCardCommand = new RelayCommand(p => RemoveCustomCard(p as CustomCardViewModel));
        ImportLegacyCommand = new RelayCommand(() => ImportLegacy(overwrite: false));
        ImportLegacyOverwriteCommand = new RelayCommand(() => ImportLegacy(overwrite: true));
        ExportSettingsCommand = new RelayCommand(ExportSettings);
        ImportSettingsCommand = new RelayCommand(ImportSettings);
        OpenDataFolderCommand = new RelayCommand(() => AppServices.OpenFolder(_services.Paths.Root));
        LegacySummary = string.IsNullOrWhiteSpace(services.Settings.LegacyImport.LastSummary)
            ? "هنوز وارد کردن از نرم‌افزار قدیمی انجام نشده است."
            : services.Settings.LegacyImport.LastSummary;
    }

    public GitHubCardViewModel GitHub { get; }
    public KlingCardViewModel Kling { get; }
    public ZernioCardViewModel Zernio { get; }
    public PortalIngestSettingsViewModel PortalIngest { get; }
    public FluxCardViewModel Flux { get; }
    public GroqCardViewModel Groq { get; }
    public ObservableCollection<CustomCardViewModel> CustomCards { get; }

    public RelayCommand AddCustomCardCommand { get; }
    public RelayCommand RemoveCustomCardCommand { get; }
    public RelayCommand ImportLegacyCommand { get; }
    public RelayCommand ImportLegacyOverwriteCommand { get; }
    public RelayCommand ExportSettingsCommand { get; }
    public RelayCommand ImportSettingsCommand { get; }
    public RelayCommand OpenDataFolderCommand { get; }

    public event Action<CustomCardViewModel>? CustomCardAdded;
    public event Action<CustomCardViewModel>? CustomCardRemoved;

    public string DataFolder => _services.Paths.Root;
    public string SecretStoreText => _services.Secrets.IsAvailable
        ? "کلیدها با Windows DPAPI (فقط همین کاربر و همین رایانه) در secrets.json رمزگذاری می‌شوند."
        : _services.Secrets.UnavailableReason ?? "ذخیرهٔ امن در دسترس نیست";
    public string LegacyPathsText => "مسیرهای بررسی‌شده: %APPDATA%\\BazinoMarketingBrowser\\vault.json و %APPDATA%\\BazinoBridge\\settings.json";
    public string LegacySummary { get => _legacySummary; private set => SetProperty(ref _legacySummary, value); }
    public string NewCardName { get => _newCardName; set { if (SetProperty(ref _newCardName, value)) RelayCommand.RaiseCanExecuteChanged(); } }
    public string GeneralMessage { get => _generalMessage; private set => SetProperty(ref _generalMessage, value); }

    public void LoadAll()
    {
        GitHub.Load(); Kling.Load(); Zernio.Load(); PortalIngest.Load(); Flux.Load(); Groq.Load();
        foreach (var c in CustomCards) c.Load();
    }

    private void AddCustomCard()
    {
        var name = NewCardName.Trim();
        if (name.Length == 0) return;
        var id = CustomCardIds.FromName(name);
        if (_services.Settings.CustomCards.Any(c => c.Id == id))
        {
            GeneralMessage = $"سرویسی با شناسهٔ «{id}» از قبل وجود دارد.";
            return;
        }
        var model = new CustomCard { Id = id, Name = name };
        _services.Settings.CustomCards.Add(model);
        _services.SaveSettings();
        var vm = new CustomCardViewModel(_services, model);
        vm.Load();
        CustomCards.Add(vm);
        CustomCardAdded?.Invoke(vm);
        NewCardName = "";
        GeneralMessage = $"سرویس «{name}» اضافه شد. آدرس و کلید آن را تکمیل و ذخیره کنید.";
        _services.Info("custom:" + id, "card.add", $"سرویس سفارشی «{name}» اضافه شد");
    }

    private void RemoveCustomCard(CustomCardViewModel? vm)
    {
        if (vm is null) return;
        if (MessageBox.Show($"سرویس «{vm.Title}» و کلید ذخیره‌شدهٔ آن حذف شود؟", "حذف سرویس", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign) != MessageBoxResult.Yes)
            return;
        _services.Settings.CustomCards.Remove(vm.Model);
        _services.Secrets.RemoveByPrefix(SecretKeys.CustomPrefix(vm.Model.Id));
        _services.SaveSettings();
        CustomCards.Remove(vm);
        CustomCardRemoved?.Invoke(vm);
        GeneralMessage = $"سرویس «{vm.Title}» حذف شد.";
        _services.Warn("custom:" + vm.Model.Id, "card.remove", $"سرویس سفارشی «{vm.Title}» حذف شد");
    }

    private void ImportLegacy(bool overwrite)
    {
        if (_services.IsSampleMode) return;
        if (!OperatingSystem.IsWindows()) { LegacySummary = "فقط روی Windows ممکن است."; return; }
        if (overwrite && MessageBox.Show("مقادیر فعلی با مقادیر نرم‌افزار قدیمی جایگزین شوند؟", "وارد کردن با جایگزینی", MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign) != MessageBoxResult.Yes)
            return;
        try
        {
            var report = _services.RunLegacyImport(overwrite);
            LegacySummary = report.ToSummary();
            SyncCustomCardsFromSettings();
            LoadAll();
            GeneralMessage = report.AnyImported ? "کلیدها و تنظیمات قدیمی وارد شدند." : "چیز جدیدی برای وارد کردن نبود.";
        }
        catch (Exception ex)
        {
            LegacySummary = "وارد کردن ناموفق بود: " + ex.Message;
            _services.Error("import", "legacy", "وارد کردن از نرم‌افزار قدیمی ناموفق بود", ex.ToString(), "import_failed");
        }
    }

    private void SyncCustomCardsFromSettings()
    {
        foreach (var model in _services.Settings.CustomCards)
        {
            if (CustomCards.Any(c => ReferenceEquals(c.Model, model) || c.Model.Id == model.Id)) continue;
            var vm = new CustomCardViewModel(_services, model);
            vm.Load();
            CustomCards.Add(vm);
            CustomCardAdded?.Invoke(vm);
        }
    }

    private void ExportSettings()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "خروجی تنظیمات (بدون کلیدها)",
            FileName = "bazino-marketing-settings.json",
            Filter = "JSON (*.json)|*.json"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, _services.SettingsStore.Export(_services.Settings));
            GeneralMessage = "تنظیمات (بدون هیچ کلید یا توکن) ذخیره شد: " + dlg.FileName;
            _services.Info("app", "settings.export", "خروجی تنظیمات گرفته شد", dlg.FileName);
        }
        catch (Exception ex)
        {
            GeneralMessage = "خروجی گرفتن ناموفق بود: " + ex.Message;
        }
    }

    private void ImportSettings()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "وارد کردن تنظیمات", Filter = "JSON (*.json)|*.json" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var imported = SettingsStore.Import(File.ReadAllText(dlg.FileName));
            ApplyImported(imported);
            GeneralMessage = "تنظیمات وارد شد. کلیدها تغییر نکردند.";
            _services.Info("app", "settings.import", "تنظیمات از فایل وارد شد", dlg.FileName);
        }
        catch (Exception ex)
        {
            GeneralMessage = "وارد کردن ناموفق بود: " + ex.Message;
            _services.Error("app", "settings.import", "وارد کردن تنظیمات ناموفق بود", ex.ToString(), "settings_import_failed");
        }
    }

    /// <summary>Replaces the whole settings object (file import or an agent change the owner approved) and rebuilds the cards.</summary>
    public void ApplyImported(AppSettings imported)
    {
        imported.LegacyImport = _services.Settings.LegacyImport;
        imported.Ui.LastPage = _services.Settings.Ui.LastPage;
        _services.ReplaceSettings(imported);
        foreach (var old in CustomCards.ToList()) CustomCardRemoved?.Invoke(old);
        CustomCards.Clear();
        foreach (var model in _services.Settings.CustomCards)
        {
            var vm = new CustomCardViewModel(_services, model);
            vm.Load();
            CustomCards.Add(vm);
            CustomCardAdded?.Invoke(vm);
        }
        LoadAll();
    }

    public void SetSampleLegacySummary(string text) => LegacySummary = text;
}

/// <summary>Secure, independent portal ingest credential; the value is never loaded back into the UI.</summary>
public sealed class PortalIngestSettingsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private string _baseUrl = "https://bazino.pro";
    private string _tokenInput = "";
    private string _message = "";
    private bool _hasToken;

    public PortalIngestSettingsViewModel(AppServices services)
    {
        _services = services;
        SaveCommand = new RelayCommand(Save);
        ClearTokenCommand = new RelayCommand(ClearToken, () => HasToken || !string.IsNullOrWhiteSpace(TokenInput));
    }

    public string BaseUrl { get => _baseUrl; set => SetProperty(ref _baseUrl, value); }
    public string TokenInput { get => _tokenInput; set { if (SetProperty(ref _tokenInput, value)) RelayCommand.RaiseCanExecuteChanged(); } }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public bool HasToken { get => _hasToken; private set { if (SetProperty(ref _hasToken, value)) { OnPropertyChanged(nameof(TokenStatus)); RelayCommand.RaiseCanExecuteChanged(); } } }
    public string TokenStatus => HasToken ? "توکن ذخیره شده است؛ مقدار آن هرگز دوباره نمایش داده نمی‌شود." : "توکن ingest هنوز ثبت نشده است.";
    public RelayCommand SaveCommand { get; }
    public RelayCommand ClearTokenCommand { get; }

    public void Load()
    {
        BaseUrl = string.IsNullOrWhiteSpace(_services.Settings.BazinoPortal.BaseUrl)
            ? "https://bazino.pro"
            : _services.Settings.BazinoPortal.BaseUrl;
        HasToken = _services.Secrets.Has(SecretKeys.BazinoPortalIngestToken);
        TokenInput = "";
        Message = "";
        OnPropertyChanged(nameof(TokenStatus));
    }

    private void Save()
    {
        var candidate = (BaseUrl ?? "").Trim().TrimEnd('/');
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "bazino.pro", StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath != "/")
        {
            Message = "نشانی باید فقط ریشهٔ HTTPS دامنهٔ رسمی bazino.pro باشد.";
            return;
        }
        var token = (TokenInput ?? "").Trim();
        if (token.Length != 0 && !SecretFormat.IsHeaderSafe(token))
        {
            Message = "توکن واردشده قالب هدر مجاز ندارد؛ مقدار ذخیره نشد.";
            return;
        }
        if (token.Length != 0 && !_services.Secrets.IsAvailable)
        {
            Message = _services.Secrets.UnavailableReason ?? "ذخیرهٔ امن توکن در این دستگاه در دسترس نیست.";
            return;
        }
        _services.Settings.BazinoPortal.BaseUrl = candidate;
        if (token.Length != 0) _services.Secrets.Set(SecretKeys.BazinoPortalIngestToken, token);
        _services.SaveSettings();
        TokenInput = "";
        HasToken = _services.Secrets.Has(SecretKeys.BazinoPortalIngestToken);
        OnPropertyChanged(nameof(TokenStatus));
        Message = token.Length == 0 ? "نشانی پورتال ذخیره شد؛ کلید موجود تغییر نکرد." : "نشانی و توکن پورتال به‌شکل امن ذخیره شدند.";
    }

    private void ClearToken()
    {
        _services.Secrets.Remove(SecretKeys.BazinoPortalIngestToken);
        TokenInput = "";
        HasToken = false;
        OnPropertyChanged(nameof(TokenStatus));
        Message = "توکن ingest پورتال حذف شد.";
    }
}
