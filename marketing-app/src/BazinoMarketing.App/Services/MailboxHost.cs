using System.Windows.Threading;
using BazinoMarketing.App.ViewModels;
using BazinoMarketing.Core.Browser;
using BazinoMarketing.Core.Logging;
using BazinoMarketing.Core.Mailbox;
using BazinoMarketing.Core.Settings;
using BazinoMarketing.Core.Tools;

namespace BazinoMarketing.App.Services;

/// <summary>
/// Glue between the Core <see cref="MailboxService"/> and the WPF shell: tool checks routed through the cards
/// (so the page updates live), settings replacement with UI refresh, and the self-update/restart hand-off to <see cref="App"/>.
/// There are no confirmation dialogs any more (owner's decision, 2026-09-27).
/// </summary>
public sealed class MailboxHost
{
    private readonly AppServices _services;
    private readonly MainViewModel _main;
    private readonly Dispatcher _dispatcher;

    public MailboxService Service { get; }

    public MailboxHost(AppServices services, MainViewModel main, Dispatcher dispatcher)
    {
        _services = services;
        _main = main;
        _dispatcher = dispatcher;
        var executor = new CommandExecutor(new CommandExecutorOptions
        {
            GetSettings = () => _services.Settings,
            Secrets = _services.Secrets,
            Log = _services.Log,
            Redactor = _services.Redactor,
            AppVersion = AppServices.Version,
            DataFolder = _services.Paths.Root,
            DownloadFolder = _services.Paths.Downloads,
            Media = _services.Media,
            ApplySettings = ApplySettingsAsync,
            ToolStates = ToolStates,
            CheckTools = CheckToolsAsync,
            RunLegacyImport = RunLegacyImportAsync,
            RestartWith = (file, delay) => App.ScheduleRestartAsync(file, delay),
            Restart = delay => App.ScheduleRestartAsync(null, delay),
            Browser = () => _services.Browser
        });
        Service = new MailboxService(() => _services.Settings, _services.Secrets, _services.Log, AppServices.Version, _services.Paths.Root, executor);
        _main.Connection.Mailbox.Attach(Service);
    }

    public void Start()
    {
        Service.Start();
        _main.Connection.Mailbox.Refresh();
        _main.Connection.Mailbox.NotifyRunningChanged();
    }

    public Task StopAsync() => Service.StopAsync(TimeSpan.FromSeconds(4));

    private async Task<string?> ApplySettingsAsync(AppSettings settings)
    {
        var op = _dispatcher.InvokeAsync(() =>
        {
            try
            {
                _main.Settings.ApplyImported(settings);
                _main.Connection.RefreshTexts();
                return (string?)null;
            }
            catch (Exception ex)
            {
                App.ReportUnexpected(ex, "mailbox-apply-settings");
                return ex.Message;
            }
        });
        var error = await op.Task.ConfigureAwait(false);
        if (error is null) Service.PollNow();
        return error;
    }

    private IReadOnlyDictionary<string, string> ToolStates()
    {
        return _dispatcher.Invoke(() => _main.Connection.Cards.ToDictionary(c => c.Title,
            c => $"{c.StateText} — {c.StatusSummary}" + (string.IsNullOrEmpty(c.StatusDetail) ? "" : $" [{c.StatusDetail}]")));
    }

    private async Task<IReadOnlyDictionary<string, ToolCheckResult>> CheckToolsAsync(string selector, CancellationToken ct)
    {
        var op = _dispatcher.InvokeAsync(() => RunChecksOnUiThreadAsync(selector));
        var inner = await op.Task.ConfigureAwait(false);
        return await inner.ConfigureAwait(false);
    }

    private async Task<IReadOnlyDictionary<string, ToolCheckResult>> RunChecksOnUiThreadAsync(string selector)
    {
        var results = new Dictionary<string, ToolCheckResult>(StringComparer.Ordinal);
        foreach (var card in _main.Connection.Cards.ToList())
        {
            if (selector != "all" && !string.Equals(card.Id, selector, StringComparison.OrdinalIgnoreCase)) continue;
            await card.TestAsync();
            results[card.Id] = new ToolCheckResult(card.State, card.StatusSummary, card.StatusDetail);
        }
        return results;
    }

    private async Task<string> RunLegacyImportAsync(bool overwrite)
    {
        var op = _dispatcher.InvokeAsync(() =>
        {
            var report = _services.RunLegacyImport(overwrite);
            _main.Settings.LoadAll();
            return report.ToSummary();
        });
        return await op.Task.ConfigureAwait(false);
    }
}
