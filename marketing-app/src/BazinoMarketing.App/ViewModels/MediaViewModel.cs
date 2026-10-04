using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using Microsoft.Win32;
using BazinoMarketing.App.Infrastructure;
using BazinoMarketing.App.Services;
using BazinoMarketing.Core.Media;
using BazinoMarketing.Core.Secrets;

namespace BazinoMarketing.App.ViewModels;

public sealed class MediaViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private bool _isBusy;
    private string _folderPath = "";
    private string _pendingFolderPath = "";
    private string _ffmpegPath = "";
    private string _instagramUrl = "";
    private string _clipStart = "0";
    private string _clipEnd = "15";
    private string _selectedFormat = "mp4";
    private string _transcriptText = "";
    private bool _translateToEnglish;
    private string _groqApiKey = "";
    private string _groqKeyHint = "";
    private string _message = "";
    private string _durationText = "";
    private MediaFileEntry? _selectedFile;
    private Uri? _previewUri;
    private Uri? _imagePreviewUri;

    public MediaViewModel(AppServices services)
    {
        _services = services;
        Files = new ObservableCollection<MediaFileEntry>();
        Formats = new[] { "mp4", "webm", "mp3" };
        _folderPath = services.Media.CurrentFolder;
        _pendingFolderPath = _folderPath;
        _ffmpegPath = services.Settings.Media.FfmpegPath;

        RefreshCommand = new RelayCommand(Refresh, () => !IsBusy);
        BrowseFolderCommand = new RelayCommand(BrowseFolder, () => !IsBusy);
        ConfirmFolderCommand = new RelayCommand(ConfirmFolder, () => !IsBusy && !string.IsNullOrWhiteSpace(PendingFolderPath));
        OpenFolderCommand = new RelayCommand(() => AppServices.OpenFolder(FolderPath), () => !IsBusy);
        BrowseFfmpegCommand = new RelayCommand(BrowseFfmpeg, () => !IsBusy);
        SaveFfmpegCommand = new RelayCommand(SaveFfmpegPath, () => !IsBusy);
        LoadCurrentTabCommand = new AsyncRelayCommand(LoadCurrentTabAsync, () => !IsBusy);
        DownloadCommand = new AsyncRelayCommand(DownloadAsync, () => !IsBusy);
        UploadCommand = new AsyncRelayCommand(UploadAsync, () => !IsBusy && SelectedFile is not null);
        PreviewCommand = new AsyncRelayCommand(CreatePreviewAsync, () => !IsBusy && SelectedFile is { IsVideo: true });
        TrimCommand = new AsyncRelayCommand(TrimAsync, () => !IsBusy && SelectedFile is { IsVideo: true });
        ConvertCommand = new AsyncRelayCommand(ConvertAsync, () => !IsBusy && SelectedFile is { IsMedia: true });
        TranscribeCommand = new AsyncRelayCommand(TranscribeSelectedAsync, () => !IsBusy && SelectedFile is not null && (SelectedFile.IsVideo || SelectedFile.IsAudio));
        CopyTranscriptCommand = new RelayCommand(CopyTranscript, () => !string.IsNullOrWhiteSpace(TranscriptText));
        SaveGroqKeyCommand = new RelayCommand(SaveGroqKey, () => !IsBusy);

        RefreshGroqHint();
        if (services.IsSampleMode)
            Message = "پوشهٔ رسانه، دریافت پست انتخابی، ارسال فایل برای بررسی و ابزارهای ویرایش در یک‌جا هستند.";
    }

    public ObservableCollection<MediaFileEntry> Files { get; }
    public IReadOnlyList<string> Formats { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand BrowseFolderCommand { get; }
    public RelayCommand ConfirmFolderCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand BrowseFfmpegCommand { get; }
    public RelayCommand SaveFfmpegCommand { get; }
    public AsyncRelayCommand LoadCurrentTabCommand { get; }
    public AsyncRelayCommand DownloadCommand { get; }
    public AsyncRelayCommand UploadCommand { get; }
    public AsyncRelayCommand PreviewCommand { get; }
    public AsyncRelayCommand TrimCommand { get; }
    public AsyncRelayCommand ConvertCommand { get; }
    public AsyncRelayCommand TranscribeCommand { get; }
    public RelayCommand CopyTranscriptCommand { get; }
    public RelayCommand SaveGroqKeyCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            RelayCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(BusyText));
        }
    }

    public string BusyText => IsBusy ? "در حال انجام…" : "";
    public string FolderPath { get => _folderPath; private set => SetProperty(ref _folderPath, value); }
    public string PendingFolderPath { get => _pendingFolderPath; set => SetProperty(ref _pendingFolderPath, value); }
    public string FfmpegPath { get => _ffmpegPath; set => SetProperty(ref _ffmpegPath, value); }
    public string InstagramUrl { get => _instagramUrl; set => SetProperty(ref _instagramUrl, value); }
    public string ClipStart { get => _clipStart; set => SetProperty(ref _clipStart, value); }
    public string ClipEnd { get => _clipEnd; set => SetProperty(ref _clipEnd, value); }
    public string SelectedFormat { get => _selectedFormat; set => SetProperty(ref _selectedFormat, value); }
    public bool TranslateToEnglish { get => _translateToEnglish; set => SetProperty(ref _translateToEnglish, value); }
    public string GroqApiKey { get => _groqApiKey; set => SetProperty(ref _groqApiKey, value); }
    public string GroqKeyHint { get => _groqKeyHint; private set => SetProperty(ref _groqKeyHint, value); }

    public string TranscriptText
    {
        get => _transcriptText;
        set
        {
            if (!SetProperty(ref _transcriptText, value)) return;
            RelayCommand.RaiseCanExecuteChanged();
        }
    }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public string DurationText { get => _durationText; private set => SetProperty(ref _durationText, value); }
    public Uri? PreviewUri { get => _previewUri; private set => SetProperty(ref _previewUri, value); }
    public Uri? ImagePreviewUri { get => _imagePreviewUri; private set => SetProperty(ref _imagePreviewUri, value); }

    public MediaFileEntry? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (!SetProperty(ref _selectedFile, value)) return;
            PreviewUri = MakePreviewUri(value);
            ImagePreviewUri = MakeImagePreviewUri(value);
            DurationText = value is null ? "" : $"{value.Extension.ToUpperInvariant()} • {FormatSize(value.SizeBytes)}";
            OnPropertyChanged(nameof(SelectedFileText));
            OnPropertyChanged(nameof(IsVideoSelected));
            OnPropertyChanged(nameof(IsImageSelected));
            OnPropertyChanged(nameof(IsPreviewEmpty));
            RelayCommand.RaiseCanExecuteChanged();
            if (value is { IsMedia: true } && !_services.IsSampleMode) _ = ProbeSelectionAsync(value);
        }
    }

    public string SelectedFileText => SelectedFile?.RelativePath ?? "فایلی انتخاب نشده";
    public bool IsVideoSelected => SelectedFile?.IsVideo == true;
    public bool IsImageSelected => SelectedFile?.Extension is ".jpg" or ".jpeg" or ".png" or ".gif";
    public bool IsPreviewEmpty => !IsVideoSelected && !IsImageSelected;

    /// <summary>Stops any child downloader/FFmpeg process before the app exits.</summary>
    public void CancelCurrentOperation()
    {
        try { _lifetimeCts.Cancel(); } catch (ObjectDisposedException) { }
    }

    public void Refresh()
    {
        if (_services.IsSampleMode) return;
        try
        {
            FolderPath = _services.Media.CurrentFolder;
            if (string.IsNullOrWhiteSpace(PendingFolderPath)) PendingFolderPath = FolderPath;
            var previouslySelected = SelectedFile?.RelativePath;
            var files = _services.Media.ListFiles();
            SelectedFile = null;
            Files.Clear();
            foreach (var file in files) Files.Add(file);
            SelectedFile = files.FirstOrDefault(f => string.Equals(f.RelativePath, previouslySelected, StringComparison.OrdinalIgnoreCase))
                           ?? files.FirstOrDefault(f => f.IsVideo)
                           ?? files.FirstOrDefault(f => f.IsMedia)
                           ?? files.FirstOrDefault();
            Message = files.Count == 0 ? "در این پوشه هنوز فایلی نیست." : $"{files.Count} فایل در پوشه پیدا شد.";
        }
        catch (Exception ex)
        {
            Message = "خواندن پوشهٔ رسانه ممکن نشد: " + ex.Message;
        }
    }

    public void SetSample(string folder, IEnumerable<MediaFileEntry> files)
    {
        FolderPath = folder;
        PendingFolderPath = folder;
        Files.Clear();
        foreach (var file in files) Files.Add(file);
        SelectedFile = Files.FirstOrDefault(f => f.IsVideo) ?? Files.FirstOrDefault();
        DurationText = "00:18 • نمونهٔ چیدمان";
        FfmpegPath = @"C:\Program Files\FFmpeg\bin\ffmpeg.exe";
        Message = "نمونهٔ ساختگی برای نمایش صفحه؛ فایل واقعی خوانده یا فرستاده نشده است.";
    }

    private void BrowseFolder()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var initial = Directory.Exists(PendingFolderPath) ? PendingFolderPath
            : Directory.Exists(FolderPath) ? FolderPath
            : Directory.Exists(profile) ? profile : Environment.CurrentDirectory;
        var dialog = new OpenFolderDialog
        {
            Title = "پوشهٔ مقصد رسانه را انتخاب کنید",
            InitialDirectory = initial,
            Multiselect = false
        };
        if (dialog.ShowDialog() == true) PendingFolderPath = dialog.FolderName;
    }

    private void ConfirmFolder()
    {
        try
        {
            var chosen = _services.Media.ConfirmFolder(PendingFolderPath);
            _services.SaveSettings();
            FolderPath = chosen;
            PendingFolderPath = chosen;
            Message = "پوشه تأیید شد؛ برنامه اکنون فایل‌های همین پوشه را می‌خواند و در آن ذخیره می‌کند.";
            _services.Success("media", "folder", "پوشهٔ رسانه تأیید شد");
            Refresh();
        }
        catch (Exception ex)
        {
            Message = "تأیید پوشه ممکن نشد: " + ex.Message;
            _services.Error("media", "folder", "تأیید پوشهٔ رسانه ناموفق بود", ex.Message, "media_folder_failed");
        }
    }

    private void BrowseFfmpeg()
    {
        var dialog = new OpenFileDialog
        {
            Title = "فایل ffmpeg.exe را انتخاب کنید",
            Filter = "FFmpeg (ffmpeg.exe)|ffmpeg.exe|برنامه‌ها (*.exe)|*.exe|همهٔ فایل‌ها (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true) FfmpegPath = dialog.FileName;
    }

    private void SaveFfmpegPath()
    {
        try
        {
            var value = (FfmpegPath ?? "").Trim();
            if (value.Length > 0 && !File.Exists(value))
            {
                Message = "این فایل پیدا نشد؛ مسیر ffmpeg.exe را انتخاب کنید.";
                return;
            }
            _services.Settings.Media.FfmpegPath = value;
            _services.SaveSettings();
            Message = value.Length == 0 ? "مسیر ذخیره شد؛ برنامه FFmpeg را از PATH پیدا می‌کند." : "مسیر FFmpeg ذخیره شد.";
            _services.Success("media", "ffmpeg.settings", "مسیر FFmpeg ذخیره شد", value.Length == 0 ? "source=PATH" : "source=custom");
        }
        catch (Exception ex)
        {
            Message = "ذخیرهٔ مسیر FFmpeg ممکن نشد: " + ex.Message;
        }
    }

    private async Task LoadCurrentTabAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _services.Browser.ReadAsync(_services.Settings.Browser.MaxReadChars, _lifetimeCts.Token);
            if (!result.Ok || result.Value is null)
            {
                Message = result.Error ?? "تب فعالی برای خواندن پیدا نشد.";
                return;
            }
            var url = result.Value["url"]?.GetValue<string>() ?? "";
            if (string.IsNullOrWhiteSpace(url))
            {
                Message = "نشانی تب فعلی پیدا نشد.";
                return;
            }
            InstagramUrl = url;
            Message = "نشانی تب فعلی در کادر قرار گرفت؛ با دکمهٔ دریافت، همین یک پست دانلود می‌شود.";
        }
        catch (Exception ex)
        {
            Message = "خواندن نشانی تب فعلی ممکن نشد: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    private async Task DownloadAsync()
    {
        IsBusy = true;
        try
        {
            var url = (InstagramUrl ?? "").Trim();
            if (url.Length == 0)
            {
                var page = await _services.Browser.ReadAsync(_services.Settings.Browser.MaxReadChars, _lifetimeCts.Token);
                if (!page.Ok || page.Value is null)
                {
                    Message = page.Error ?? "نشانی پست را وارد کنید یا یک تب مرورگر انتخاب کنید.";
                    return;
                }
                url = page.Value["url"]?.GetValue<string>() ?? "";
                InstagramUrl = url;
            }

            var result = await _services.Media.DownloadInstagramAsync(url, _lifetimeCts.Token);
            Message = result.Message;
            if (result.Ok)
            {
                _services.Success("media", "instagram.download", "یک پست انتخاب‌شده دریافت شد", $"files={result.Files?.Count ?? 0}", result.DurationMs);
                Refresh();
                if (!string.IsNullOrWhiteSpace(result.RelativePath))
                    SelectedFile = Files.FirstOrDefault(f => f.RelativePath.StartsWith(result.RelativePath, StringComparison.OrdinalIgnoreCase));
                Message = result.Message;
            }
            else
                _services.Error("media", "instagram.download", "دریافت پست انتخاب‌شده ناموفق بود", result.Code, result.Code, result.DurationMs);
        }
        catch (Exception ex)
        {
            Message = "دریافت پست ناموفق بود: " + ex.Message;
            _services.Error("media", "instagram.download", "دریافت پست ناموفق بود", ex.Message, "instagram_download_failed");
        }
        finally { IsBusy = false; }
    }

    private async Task UploadAsync()
    {
        var selected = SelectedFile;
        if (selected is null) return;
        IsBusy = true;
        try
        {
            var result = await _services.Media.UploadToGitHubAsync(selected.RelativePath, _lifetimeCts.Token);
            if (!string.IsNullOrWhiteSpace(result.Transcript))
                TranscriptText = result.Transcript;
            Message = result.Ok && !string.IsNullOrWhiteSpace(result.Url)
                ? result.Message + " «" + selected.Name + "» — " + result.Url
                : result.Message;
            if (result.Ok)
                _services.Success("media", "github.upload", "فایل به گیت‌هاب فرستاده شد", selected.Name, result.DurationMs);
            else
                _services.Error("media", "github.upload", "ارسال فایل ناموفق بود", result.Code, result.Code, result.DurationMs);
        }
        catch (Exception ex)
        {
            Message = "ارسال فایل ناموفق بود: " + ex.Message;
            _services.Error("media", "github.upload", "ارسال فایل رسانه‌ای ناموفق بود", ex.Message, "github_upload_failed");
        }
        finally { IsBusy = false; }
    }

    private async Task TranscribeSelectedAsync()
    {
        var selected = SelectedFile;
        if (selected is null) return;
        IsBusy = true;
        try
        {
            var result = await _services.Media.TranscribeAsync(
                selected.RelativePath,
                translateToEnglish: TranslateToEnglish,
                uploadToGitHub: true,
                ct: _lifetimeCts.Token);
            Message = result.Message;
            if (result.Ok)
            {
                TranscriptText = string.IsNullOrWhiteSpace(result.Text)
                    ? "(گفتاری در این فایل تشخیص داده نشد)"
                    : result.Text;
                _services.Success("media", "media.transcribe", "صدای فایل به متن تبدیل شد",
                    $"{selected.Name} • {result.Provider}/{result.Model} • lang={result.Language}", result.DurationMs);
            }
            else
            {
                _services.Error("media", "media.transcribe", "تبدیل صدا به متن ناموفق بود", result.Code, result.Code, result.DurationMs);
            }
        }
        catch (Exception ex)
        {
            Message = "تبدیل صدا به متن ناموفق بود: " + ex.Message;
            _services.Error("media", "media.transcribe", "تبدیل صدا به متن ناموفق بود", ex.Message, "transcribe_failed");
        }
        finally { IsBusy = false; }
    }

    private void CopyTranscript()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(TranscriptText)) return;
            System.Windows.Clipboard.SetText(TranscriptText);
            Message = "متن پیاده‌شده در کلیپ‌بورد کپی شد.";
        }
        catch (Exception ex)
        {
            Message = "کپی در کلیپ‌بورد ممکن نشد: " + ex.Message;
        }
    }

    private void SaveGroqKey()
    {
        try
        {
            var key = (GroqApiKey ?? "").Trim();
            if (key.Length == 0)
            {
                _services.Secrets.Remove(BazinoMarketing.Core.Secrets.SecretKeys.GroqApiKey);
                RefreshGroqHint();
                Message = "کلید Groq پاک شد؛ برنامه به‌طور خودکار از سرویس Cloudflare Whisper متصل استفاده می‌کند.";
                return;
            }
            if (!BazinoMarketing.Core.Secrets.SecretFormat.IsHeaderSafe(key))
            {
                Message = "کلید واردشده معتبر نیست (باید شامل کاراکترهای انگلیسی تک‌خطی باشد).";
                return;
            }
            _services.Secrets.Set(BazinoMarketing.Core.Secrets.SecretKeys.GroqApiKey, key);
            GroqApiKey = "";
            RefreshGroqHint();
            Message = "کلید Groq در مخزن امن ویندوز ذخیره شد؛ پیاده‌سازی گفتار اکنون با اولویت Groq Whisper انجام می‌شود.";
            _services.Success("media", "groq.key", "کلید سرویس Groq ذخیره شد");
        }
        catch (Exception ex)
        {
            Message = "ذخیرهٔ کلید Groq ممکن نشد: " + ex.Message;
        }
    }

    private void RefreshGroqHint()
    {
        var (groqKey, _, _) = SpeechTranscriber.ResolveGroqCredentials(_services.Settings, _services.Secrets);
        if (!string.IsNullOrWhiteSpace(groqKey))
        {
            GroqKeyHint = $"کلید Groq ثبت شده است (طول {groqKey.Length}) — اولویت با Groq Whisper + پشتیبان خودکار Cloudflare";
            return;
        }
        var cfKey = _services.Secrets.GetOrEmpty(BazinoMarketing.Core.Secrets.SecretKeys.FluxApiKey);
        GroqKeyHint = !string.IsNullOrWhiteSpace(cfKey) && !string.IsNullOrWhiteSpace(_services.Settings.Flux.AccountId)
            ? "سرویس Cloudflare Whisper متصل و فعال است (ثبت کلید رایگان Groq اختیاری است)"
            : "کلید رایگان Groq (gsk_...) را وارد کنید یا کارت Cloudflare را در ابزارها متصل کنید";
    }

    private async Task CreatePreviewAsync()
    {
        if (SelectedFile is null) return;
        IsBusy = true;
        try
        {
            var result = await _services.Media.CreatePreviewAsync(SelectedFile.RelativePath, 1, _lifetimeCts.Token);
            Message = result.Message;
            if (result.Ok)
            {
                _services.Success("media", "ffmpeg.preview", "تصویر پیش‌نمایش ساخته شد", result.RelativePath ?? "", result.DurationMs);
                Refresh();
                if (result.RelativePath is not null) SelectedFile = Files.FirstOrDefault(f => f.RelativePath == result.RelativePath);
                Message = result.Message;
            }
            else _services.Error("media", "ffmpeg.preview", "ساخت پیش‌نمایش ناموفق بود", result.Code, result.Code, result.DurationMs);
        }
        catch (Exception ex) { Message = "ساخت پیش‌نمایش ناموفق بود: " + ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task TrimAsync()
    {
        if (SelectedFile is null) return;
        if (!TrySeconds(ClipStart, out var start) || !TrySeconds(ClipEnd, out var end))
        {
            Message = "زمان آغاز و پایان باید عدد باشند؛ نمونه: 0 و 15.5";
            return;
        }
        IsBusy = true;
        try
        {
            var result = await _services.Media.TrimAsync(SelectedFile.RelativePath, start, end, _lifetimeCts.Token);
            Message = result.Message;
            if (result.Ok)
            {
                _services.Success("media", "ffmpeg.trim", "ویدئو برش خورد", result.RelativePath ?? "", result.DurationMs);
                Refresh();
                if (result.RelativePath is not null) SelectedFile = Files.FirstOrDefault(f => f.RelativePath == result.RelativePath);
                Message = result.Message;
            }
            else _services.Error("media", "ffmpeg.trim", "برش ویدئو ناموفق بود", result.Code, result.Code, result.DurationMs);
        }
        catch (Exception ex) { Message = "برش ویدئو ناموفق بود: " + ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task ConvertAsync()
    {
        if (SelectedFile is null) return;
        IsBusy = true;
        try
        {
            var result = await _services.Media.ConvertAsync(SelectedFile.RelativePath, SelectedFormat, _lifetimeCts.Token);
            Message = result.Message;
            if (result.Ok)
            {
                _services.Success("media", "ffmpeg.convert", "فایل رسانه‌ای تبدیل شد", result.RelativePath ?? "", result.DurationMs);
                Refresh();
                if (result.RelativePath is not null) SelectedFile = Files.FirstOrDefault(f => f.RelativePath == result.RelativePath);
                Message = result.Message;
            }
            else _services.Error("media", "ffmpeg.convert", "تبدیل فایل ناموفق بود", result.Code, result.Code, result.DurationMs);
        }
        catch (Exception ex) { Message = "تبدیل فایل ناموفق بود: " + ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task ProbeSelectionAsync(MediaFileEntry selected)
    {
        try
        {
            var result = await _services.Media.ProbeAsync(selected.RelativePath, _lifetimeCts.Token);
            if (!ReferenceEquals(SelectedFile, selected) || result is null) return;
            if (result.DurationSeconds is { } seconds)
            {
                DurationText = $"{TimeSpan.FromSeconds(seconds):hh\\:mm\\:ss} • {result.Width?.ToString() ?? "?"}×{result.Height?.ToString() ?? "?"}";
                if (string.Equals(ClipEnd, "15", StringComparison.Ordinal) && seconds > 0)
                    ClipEnd = Math.Min(seconds, 15).ToString("0.###", CultureInfo.InvariantCulture);
            }
        }
        catch { }
    }

    private Uri? MakePreviewUri(MediaFileEntry? file) => file is { IsVideo: true } ? MakeLocalUri(file) : null;

    private Uri? MakeImagePreviewUri(MediaFileEntry? file) =>
        file is not null && (file.Extension is ".jpg" or ".jpeg" or ".png" or ".gif") ? MakeLocalUri(file) : null;

    private Uri? MakeLocalUri(MediaFileEntry file)
    {
        try
        {
            var path = Path.GetFullPath(Path.Combine(FolderPath, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            return File.Exists(path) ? new Uri(path, UriKind.Absolute) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { return null; }
    }

    private static bool TrySeconds(string value, out double seconds) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) && double.IsFinite(seconds);

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => bytes + " بایت",
        < 1024 * 1024 => (bytes / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " کیلوبایت",
        _ => (bytes / (1024d * 1024d)).ToString("0.0", CultureInfo.InvariantCulture) + " مگابایت"
    };
}
