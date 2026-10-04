using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using BazinoMarketing.App.Infrastructure;
using BazinoMarketing.App.Services;
using BazinoMarketing.Core.Publishing;

namespace BazinoMarketing.App.ViewModels;

public sealed class PublishQueueViewModel : ObservableObject
{
    private readonly AppServices _services;
    private bool _isBusy;
    private string _message = "پیش‌نویس‌ها از پوشهٔ صف انتشار خوانده می‌شوند.";
    private PublishQueuePostViewModel? _activePost;

    public PublishQueueViewModel(AppServices services)
    {
        _services = services;
        Posts = new ObservableCollection<PublishQueuePostViewModel>();
        Reports = new ObservableCollection<PublishQueueReportViewModel>();
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        if (services.IsSampleMode) SetSample();
    }

    public ObservableCollection<PublishQueuePostViewModel> Posts { get; }
    public ObservableCollection<PublishQueueReportViewModel> Reports { get; }
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>The card the keyboard arrows act on: the one the mouse touched last.</summary>
    public PublishQueuePostViewModel? ActivePost
    {
        get => _activePost;
        set => SetProperty(ref _activePost, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(BusyText));
            RelayCommand.RaiseCanExecuteChanged();
        }
    }
    public string BusyText => IsBusy ? "در حال به‌روزرسانی…" : "";
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public bool HasPosts => Posts.Count > 0;
    public bool HasReports => Reports.Count > 0;

    public async Task RefreshAsync()
    {
        if (_services.IsSampleMode) return;
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var cards = await _services.PublishQueue.LoadReadyAsync();
            var reports = await _services.PublishQueue.LoadRecentResultsAsync();
            Posts.Clear();
            foreach (var card in cards) Posts.Add(new PublishQueuePostViewModel(card, ApproveAsync, SubmitFeedbackAsync));
            Reports.Clear();
            foreach (var report in reports) Reports.Add(new PublishQueueReportViewModel(report));
            Message = cards.Count == 0
                ? "محتوای تازه‌ای برای بازبینی نیست. محتوای تأییدشده از همین‌جا وارد صف زمان‌بندی می‌شود."
                : $"{PublishPreviewText.PersianDigits(cards.Count)} محتوای آماده برای بازبینی پیدا شد. زمان انتشار بالای هر پست نمایش داده شده است.";
            OnPropertyChanged(nameof(HasPosts));
            OnPropertyChanged(nameof(HasReports));
        }
        catch (Exception ex)
        {
            Message = "خواندن فهرست محتوا ممکن نشد: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    public void SetSample()
    {
        Posts.Clear();
        Reports.Clear();

        var carousel = new PublishQueueItem
        {
            Id = "sample-carousel",
            Title = "نمونهٔ نمایشی — کاروسل چهار اسلایدی",
            MediaFormat = "carousel",
            ContentType = "carousel",
            Topic = "gaming-news",
            GuideVersion = PublishContentCatalog.GuideVersion,
            TopicCycle = "2026-10-03",
            ContentSlot = "12:00 — پست اسلایدی خبر",
            Language = "fa",
            Caption = "این فقط نمونهٔ نمایشی رابط است. محتوای واقعی فقط پس از قرارگرفتن در فهرست و تأیید مالک زمان‌بندی می‌شود.\n\n#BAZINO #PS5",
            PublishAt = DateTimeOffset.Now.AddHours(2),
            TimeZoneId = TimeZoneInfo.Local.Id,
            TargetPlatform = "instagram",
            TargetAccountId = ZernioAutomationBuilder.InstagramAccountId,
            Cta = "نمونهٔ نمایشی؛ هیچ اقدام یا انتشار واقعی انجام نمی‌شود.",
            ProductionStatus = "final",
            PreviewReviewed = true
        };
        var slides = Enumerable.Range(1, 4)
            .Select(i => new QueueMediaFile($"marketing-app-mailbox/publish-queue/media/sample-{i}.jpg",
                SampleSlidePath(i) ?? $"marketing-app-mailbox/publish-queue/media/sample-{i}.jpg", "image", null))
            .ToArray();
        Posts.Add(new PublishQueuePostViewModel(new PublishQueueCard(carousel, "sample", slides),
            _ => Task.CompletedTask, (_, _) => Task.CompletedTask));

        var reel = new PublishQueueItem
        {
            Id = "sample-reel",
            Title = "نمونهٔ نمایشی — ریل عمودی",
            MediaFormat = "reel",
            ContentType = "reels",
            Topic = "daily-reels",
            GuideVersion = PublishContentCatalog.GuideVersion,
            TopicCycle = "2026-10-03",
            ContentSlot = "15:00 — ریلز روزانه",
            Language = "fa",
            Caption = "نمونهٔ نمایشی ریل؛ هیچ انتشار واقعی انجام نمی‌شود.",
            PublishAt = DateTimeOffset.Now.AddHours(5),
            TimeZoneId = TimeZoneInfo.Local.Id,
            TargetPlatform = "instagram",
            TargetAccountId = ZernioAutomationBuilder.InstagramAccountId,
            Cta = "نمونهٔ نمایشی.",
            ProductionStatus = "final",
            PreviewReviewed = true
        };
        Posts.Add(new PublishQueuePostViewModel(
            new PublishQueueCard(reel, "sample", new[]
            {
                new QueueMediaFile("marketing-app-mailbox/publish-queue/media/sample.mp4", "", "video", null,
                    Transcript: "نمونهٔ نمایشی متن گفتار؛ ویدئوی واقعی باید متن پیاده‌شدهٔ خودش را همراه داشته باشد.",
                    TranscriptLanguage: "fa")
            }),
            _ => Task.CompletedTask, (_, _) => Task.CompletedTask));

        Reports.Add(new PublishQueueReportViewModel(new PublishQueueReportEntry("sample-result", DateTimeOffset.Now.AddDays(-1), true,
            "Instagram: منتشر شد؛ YouTube: رد شد", "https://www.instagram.com/reel/example/", "نمونهٔ نمایشی گزارش است؛ نتیجهٔ واقعی نیست.")));
        Message = "نمونهٔ نمایشی است؛ هیچ پستی منتشر یا تأیید نمی‌شود.";
        OnPropertyChanged(nameof(HasPosts));
        OnPropertyChanged(nameof(HasReports));
    }

    /// <summary>Writes one bundled sample slide to a temp file so the sample carousel shows real slides in the render.</summary>
    private static string? SampleSlidePath(int index)
    {
        try
        {
            var folder = Path.Combine(Path.GetTempPath(), "BazinoMarketing-sample");
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, $"carousel-{index}.png");
            if (!File.Exists(target))
            {
                using var stream = typeof(PublishQueueViewModel).Assembly
                    .GetManifestResourceStream($"BazinoMarketing.App.Assets.sample.carousel-{index}.png");
                if (stream is null) return null;
                using var file = File.Create(target);
                stream.CopyTo(file);
            }
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private async Task ApproveAsync(PublishQueuePostViewModel post)
    {
        if (_services.IsSampleMode) { Message = "نمونهٔ نمایشی است و قابل تأیید نیست."; return; }
        IsBusy = true;
        post.IsWorking = true;
        try
        {
            await _services.PublishQueue.ApproveAsync(post.Id);
            Message = $"«{post.Title}» تأیید شد و برای زمان نمایش‌داده‌شده در صف انتشار قرار گرفت.";
            Posts.Remove(post);
            OnPropertyChanged(nameof(HasPosts));
        }
        catch (Exception ex) { Message = "تأیید محتوا انجام نشد: " + ex.Message; }
        finally { post.IsWorking = false; IsBusy = false; }
    }

    private async Task SubmitFeedbackAsync(PublishQueuePostViewModel post, string feedback)
    {
        if (_services.IsSampleMode) { Message = "نمونهٔ نمایشی است و نظری برایش ثبت نمی‌شود."; return; }
        IsBusy = true;
        post.IsWorking = true;
        try
        {
            await _services.PublishQueue.SubmitFeedbackAsync(post.Id, feedback);
            Message = $"نظر دربارهٔ «{post.Title}» ثبت شد؛ محتوا برای ویرایش به ایجنت برگشت و از فهرست کنار رفت.";
            Posts.Remove(post);
            OnPropertyChanged(nameof(HasPosts));
        }
        catch (Exception ex) { Message = "ثبت نظر انجام نشد: " + ex.Message; }
        finally { post.IsWorking = false; IsBusy = false; }
    }
}

public sealed class PublishQueueReportViewModel
{
    public PublishQueueReportViewModel(PublishQueueReportEntry report) => Report = report;
    public PublishQueueReportEntry Report { get; }
    public string ItemId => Report.ItemId;
    public string StatusText => Report.Published ? "منتشر شد" :
        Report.Summary.StartsWith("نتیجهٔ ارسال نامشخص", StringComparison.Ordinal) ? "نیاز به بررسی" : "ناموفق";
    public string TimeText => Report.At == DateTimeOffset.MinValue ? "زمان نامشخص" : Report.At.ToLocalTime().ToString("yyyy/MM/dd — HH:mm", CultureInfo.GetCultureInfo("fa-IR"));
    public string Summary => Report.Summary;
    public string Detail => Report.Detail;
    public string? Url => Report.Url;
    public bool Published => Report.Published;
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
    public bool HasUrl => !string.IsNullOrWhiteSpace(Url);
}

/// <summary>One media of the review card, exactly as Instagram shows one slide of a carousel.</summary>
public sealed class PublishQueueSlideViewModel : ObservableObject
{
    private bool _isCurrent;

    public PublishQueueSlideViewModel(PublishPreviewSlide slide, Action<int> select)
    {
        Slide = slide;
        ImageUri = MakeFileUri(slide.ImagePath);
        VideoUri = MakeFileUri(slide.VideoPath);
        SelectCommand = new RelayCommand(() => select(Slide.Index));
    }

    public PublishPreviewSlide Slide { get; }
    public int Index => Slide.Index;
    public string NumberText => Slide.NumberText;
    public string PositionText => Slide.PositionText;
    public Uri? ImageUri { get; }
    public Uri? VideoUri { get; }
    public bool HasImage => ImageUri is not null;
    public bool HasVideo => VideoUri is not null;
    public bool HasThumb => ImageUri is not null;

    /// <summary>Owner law 2026-10-03 (§7.4): the card shows the spoken text of the video next to the video itself.</summary>
    public bool HasTranscript => Slide.HasTranscript;

    public string TranscriptHeader => Slide.TranscriptHeader;

    public string TranscriptText => Slide.TranscriptText;
    public bool ShowPlaceholder => !HasImage && !HasVideo;
    public string PlaceholderText => Slide.PlaceholderText;
    public RelayCommand SelectCommand { get; }

    public bool IsCurrent
    {
        get => _isCurrent;
        internal set => SetProperty(ref _isCurrent, value);
    }

    private static Uri? MakeFileUri(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? new Uri(Path.GetFullPath(path)) : null;
}

public sealed class PublishQueuePostViewModel : ObservableObject
{
    private readonly Func<PublishQueuePostViewModel, Task> _approve;
    private readonly Func<PublishQueuePostViewModel, string, Task> _feedback;
    private readonly PublishPreviewModel _preview;
    private bool _isCommentOpen;
    private bool _isWorking;
    private string _feedbackText = "";
    private int _currentSlideIndex;

    public PublishQueuePostViewModel(PublishQueueCard card,
        Func<PublishQueuePostViewModel, Task> approve,
        Func<PublishQueuePostViewModel, string, Task> feedback)
    {
        Card = card;
        _approve = approve;
        _feedback = feedback;
        _preview = PublishPreviewModel.From(card.Item, card.MediaFiles);
        Fields = _preview.Fields;
        Slides = new ObservableCollection<PublishQueueSlideViewModel>(
            _preview.Slides.Select(s => new PublishQueueSlideViewModel(s, GoToSlide)));
        ApproveCommand = new AsyncRelayCommand(() => _approve(this), () => !IsWorking);
        CommentCommand = new RelayCommand(() => IsCommentOpen = !IsCommentOpen, () => !IsWorking);
        SubmitFeedbackCommand = new AsyncRelayCommand(() => _feedback(this, FeedbackText), () => !IsWorking && !string.IsNullOrWhiteSpace(FeedbackText));
        NextSlideCommand = new RelayCommand(() => Step(1), () => HasMultipleSlides);
        PreviousSlideCommand = new RelayCommand(() => Step(-1), () => HasMultipleSlides);
        SyncCurrent();
    }

    public PublishQueueCard Card { get; }
    public string Id => Card.Item.Id;
    public string Title => _preview.Title;
    public string Caption => Card.Item.Caption;
    public string CtaText => Card.Item.Cta;
    public string AffiliateDisclosureText => Card.Item.AffiliateDisclosure ?? "";
    public bool HasAffiliateDisclosure => _preview.HasAffiliateDisclosure;
    public IReadOnlyList<PublishPreviewField> Fields { get; }
    public ObservableCollection<PublishQueueSlideViewModel> Slides { get; }
    public bool HasSlides => Slides.Count > 0;
    public bool HasMultipleSlides => Slides.Count > 1;
    public bool HasNoSlides => Slides.Count == 0;

    /// <summary>The slide the viewer shows now; the card itself keeps Instagram's 4:5 frame.</summary>
    public PublishQueueSlideViewModel? CurrentSlide =>
        Slides.Count == 0 ? null : Slides[Math.Clamp(_currentSlideIndex, 0, Slides.Count - 1)];

    public string CurrentPositionText => CurrentSlide?.PositionText ?? PublishPreviewModel.EmptyValue;

    /// <summary>Every carousel slide uses Instagram's 4:5 feed frame; a single video keeps its vertical 9:16 frame.</summary>
    public double FrameWidth => CurrentSlide is { HasVideo: true } && !HasMultipleSlides ? 380 : 460;
    public double FrameHeight => Math.Round(FrameWidth / (HasMultipleSlides || CurrentSlide is not { HasVideo: true } ? 4d / 5d : 9d / 16d));

    public bool IsWorking { get => _isWorking; set { if (SetProperty(ref _isWorking, value)) { RelayCommand.RaiseCanExecuteChanged(); } } }
    public bool IsCommentOpen { get => _isCommentOpen; set => SetProperty(ref _isCommentOpen, value); }
    public string FeedbackText { get => _feedbackText; set { if (SetProperty(ref _feedbackText, value)) RelayCommand.RaiseCanExecuteChanged(); } }
    public AsyncRelayCommand ApproveCommand { get; }
    public RelayCommand CommentCommand { get; }
    public AsyncRelayCommand SubmitFeedbackCommand { get; }
    public RelayCommand NextSlideCommand { get; }
    public RelayCommand PreviousSlideCommand { get; }

    /// <summary>Moves the viewer; 1 = next slide, -1 = previous. Instagram wraps around at both ends.</summary>
    public void Step(int delta)
    {
        if (Slides.Count == 0) return;
        var next = (_currentSlideIndex + delta) % Slides.Count;
        if (next < 0) next += Slides.Count;
        GoToSlide(next);
    }

    public void GoToSlide(int index)
    {
        if (Slides.Count == 0) return;
        _currentSlideIndex = Math.Clamp(index, 0, Slides.Count - 1);
        OnPropertyChanged(nameof(CurrentSlide));
        OnPropertyChanged(nameof(CurrentPositionText));
        OnPropertyChanged(nameof(FrameWidth));
        OnPropertyChanged(nameof(FrameHeight));
        SyncCurrent();
    }

    private void SyncCurrent()
    {
        for (var i = 0; i < Slides.Count; i++) Slides[i].IsCurrent = i == _currentSlideIndex;
    }
}
