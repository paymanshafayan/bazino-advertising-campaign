using System.Collections.ObjectModel;
using System.Windows;
using BazinoMarketing.App.Infrastructure;
using BazinoMarketing.App.Services;
using BazinoMarketing.Core.Diagnostics;
using BazinoMarketing.Core.Logging;
using BazinoMarketing.Core.Settings;
using BazinoMarketing.Core.Tools;

namespace BazinoMarketing.App.ViewModels;

/// <summary>
/// صفحهٔ «اتصال»: وضعیت زندهٔ همهٔ ابزارها، بررسی سلامت، و بستهٔ عیب‌یابی.
/// از فاز ۲ کارت «صندوق فرمان» (MailboxViewModel) هم اینجاست: ایجنت متصل و فهرست فرمان‌های اجراشده (بدون مرحلهٔ اجازه — تصمیم مالک).
/// </summary>
public sealed class ConnectionViewModel : ObservableObject
{
    private readonly AppServices _services;
    private string _overallText = "برای بررسی ابزارها «بررسی همه» را بزنید.";
    private string _probeText = "";
    private string _diagnosticsMessage = "";
    private bool _isBusy;
    private int _connectedCount;

    public ConnectionViewModel(AppServices services, IEnumerable<ToolCardViewModel> cards)
    {
        _services = services;
        Cards = new ObservableCollection<ToolCardViewModel>(cards);
        CheckAllCommand = new AsyncRelayCommand(CheckAllAsync, () => !IsBusy);
        ProbeCommand = new AsyncRelayCommand(ProbeAsync, () => !IsBusy);
        CopyDiagnosticsCommand = new AsyncRelayCommand(CopyDiagnosticsAsync);
        OpenDataFolderCommand = new RelayCommand(() => AppServices.OpenFolder(_services.Paths.Root));
        OpenLogsFolderCommand = new RelayCommand(() => AppServices.OpenFolder(_services.Paths.Logs));
        Mailbox = new MailboxViewModel(System.Windows.Threading.Dispatcher.CurrentDispatcher);
        foreach (var c in Cards) c.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ToolCardViewModel.State)) RecomputeOverall(); };
    }

    public ObservableCollection<ToolCardViewModel> Cards { get; }
    public MailboxViewModel Mailbox { get; }
    public AsyncRelayCommand CheckAllCommand { get; }
    public AsyncRelayCommand ProbeCommand { get; }
    public AsyncRelayCommand CopyDiagnosticsCommand { get; }
    public RelayCommand OpenDataFolderCommand { get; }
    public RelayCommand OpenLogsFolderCommand { get; }

    public string DataFolder => _services.Paths.Root;
    public string MailboxText => $"{_services.Settings.GitHub.Repository} › {_services.Settings.GitHub.Branch} › {_services.Settings.GitHub.MailboxPath}/";
    public string PhaseNote => "این صفحه سلامت اتصال ابزارها را نشان می‌دهد و صندوق فرمان را می‌بندد یا باز می‌کند. تا وقتی برنامه باز است، ایجنت فرمان‌هایش را بدون پرسش اجرا می‌کند؛ بستن برنامه یعنی قطع دسترسی.";

    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) RelayCommand.RaiseCanExecuteChanged(); } }
    public string OverallText { get => _overallText; private set => SetProperty(ref _overallText, value); }
    public int ConnectedCount { get => _connectedCount; private set => SetProperty(ref _connectedCount, value); }

    /// <summary>Colour used by the header chip: green as soon as at least one tool answers.</summary>
    public System.Windows.Media.Brush ConnectedCountBrush => _connectedCount > 0
        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x12, 0x80, 0x5C))
        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x66, 0x70, 0x85));
    public string ProbeText { get => _probeText; private set => SetProperty(ref _probeText, value); }
    public string DiagnosticsMessage { get => _diagnosticsMessage; private set => SetProperty(ref _diagnosticsMessage, value); }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(MailboxText));
        OnPropertyChanged(nameof(DataFolder));
    }

    /// <summary>Sample-mode helper used by the screenshot renderer.</summary>
    public void SetSampleConnectedCount(int count)
    {
        ConnectedCount = count;
        OnPropertyChanged(nameof(ConnectedCountBrush));
    }

    private void RecomputeOverall()
    {
        var connected = Cards.Count(c => c.State == ToolState.Connected);
        var failing = Cards.Count(c => c.State is ToolState.Error or ToolState.NetworkError or ToolState.NeedsLogin);
        var pending = Cards.Count(c => c.State is ToolState.Unknown or ToolState.Checking);
        ConnectedCount = connected;
        OnPropertyChanged(nameof(ConnectedCountBrush));
        OverallText = pending == Cards.Count
            ? "برای بررسی ابزارها «بررسی همه» را بزنید."
            : $"{connected} از {Cards.Count} ابزار متصل است" + (failing > 0 ? $" — {failing} مورد نیاز به توجه دارد" : "") + (pending > 0 ? $" — {pending} مورد بررسی نشده" : "");
    }

    public async Task CheckAllAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            _services.Info("app", "connect", "بررسی همهٔ ابزارها آغاز شد");
            // Sequential on purpose: clearer logs and no burst of parallel processes/requests.
            foreach (var card in Cards)
                await card.TestAsync();
            RecomputeOverall();
            _services.Info("app", "connect", "بررسی همهٔ ابزارها پایان یافت", OverallText);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ProbeAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ProbeText = "در حال بررسی دسترسی شبکه…";
        try
        {
            var results = await Task.Run(() => ConnectivityProbe.RunAsync(_services.Settings.GitHub.Proxy));
            ProbeText = ConnectivityProbe.Format(results);
            _services.Info("diag", "probe", "بررسی شبکه انجام شد", ProbeText);
        }
        catch (Exception ex)
        {
            ProbeText = "بررسی شبکه ناموفق بود: " + ex.Message;
            _services.Error("diag", "probe", "بررسی شبکه ناموفق بود", ex.ToString(), "probe_failed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public string BuildDiagnostics()
    {
        var states = Cards.ToDictionary(c => c.Title, c => $"{c.StateText} — {c.StatusSummary}" + (string.IsNullOrEmpty(c.StatusDetail) ? "" : $" [{c.StatusDetail}]"));
        var recent = _services.Log.Read(new LogQuery { Limit = 40 });
        return DiagnosticsBundle.Build(AppServices.Version, _services.Settings, states, recent, _services.Redactor,
            string.IsNullOrWhiteSpace(ProbeText) ? null : ProbeText, _services.Paths.Root);
    }

    private Task CopyDiagnosticsAsync()
    {
        try
        {
            var text = BuildDiagnostics();
            Clipboard.SetText(text);
            DiagnosticsMessage = $"بستهٔ عیب‌یابی ({text.Length:N0} کاراکتر، بدون هیچ کلید یا توکن) در کلیپ‌بورد کپی شد.";
            _services.Info("diag", "bundle", "بستهٔ عیب‌یابی کپی شد");
        }
        catch (Exception ex)
        {
            DiagnosticsMessage = "کپی ناموفق بود: " + ex.Message;
        }
        return Task.CompletedTask;
    }

    public void SetSampleProbe(string text) => ProbeText = text;
    public void SetSampleOverall() => RecomputeOverall();
}
