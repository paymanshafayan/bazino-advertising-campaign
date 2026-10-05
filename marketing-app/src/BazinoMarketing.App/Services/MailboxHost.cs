using System.Windows;
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
    private readonly Window? _window;

    public MailboxService Service { get; }

    public MailboxHost(AppServices services, MainViewModel main, Dispatcher dispatcher, Window? window = null)
    {
        _services = services;
        _main = main;
        _dispatcher = dispatcher;
        _window = window;
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
            Browser = () => _services.Browser,
            // Phase 7: the agent asks the app itself to restore its window (normal, never topmost) after a capture run.
            WindowControl = mode => _dispatcher.Invoke(() => _window is MainWindow main
                ? main.ApplyWindowMode(mode)
                : (object)new { ok = false, mode, error = "window_unavailable", message = "پنجرهٔ برنامه در دسترس نیست." })
        });
        Service = new MailboxService(() => _services.Settings, _services.Secrets, _services.Log, AppServices.Version, _services.Paths.Root, executor);
        _main.Connection.Mailbox.Attach(Service);
        // Phase 8 owner addition (2026-10-04): the owner registers extra branches by pasting their public URL.
        _main.Connection.Mailbox.SourceHandlers = (AddSourceFromUrl, RemoveSource);
    }

    public void Start()
    {
        Service.Start();
        _main.Connection.Mailbox.Refresh();
        _main.Connection.Mailbox.NotifyRunningChanged();
    }

    public Task StopAsync() => Service.StopAsync(TimeSpan.FromSeconds(4));

    /// <summary>
    /// Phase 8 owner addition (2026-10-04): registers one more mailbox source from the public URL of a branch
    /// (for example <c>https://github.com/owner/repo/tree/arena%2Fmy-branch</c>). Returns null on success or a Persian error.
    /// </summary>
    public string? AddSourceFromUrl(string url)
    {
        if (!MailboxSourceUrl.TryParse(url, out var repository, out var branch, out var parseError)) return parseError;
        var settings = _services.Settings.Clone();
        var mailboxPath = string.IsNullOrWhiteSpace(settings.GitHub.MailboxPath) ? "marketing-app-mailbox" : settings.GitHub.MailboxPath;
        var primary = settings.GitHub.PrimarySource();
        if (string.Equals(repository, primary.Repository, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(branch, primary.Branch, StringComparison.OrdinalIgnoreCase))
            return "این همان شاخهٔ اصلی است و از قبل ثبت شده.";
        var key = MailboxSourceUrl.KeyOf(repository, branch, mailboxPath);
        if (settings.GitHub.Sources.Any(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase)))
            return "این شاخه از قبل ثبت شده است.";
        settings.GitHub.Sources.Add(new MailboxSourceSettings
        {
            Repository = repository,
            Branch = branch,
            MailboxPath = mailboxPath,
            Enabled = true,
            Label = ""
        });
        try
        {
            _services.SettingsStore.Save(settings);
        }
        catch (Exception ex)
        {
            return "ذخیرهٔ تنظیمات ناموفق بود: " + ex.Message;
        }
        _main.Settings.ApplyImported(settings);
        _main.Settings.LoadAll();
        Service.ReloadSources();
        _services.Info("mailbox", "source.add", "شاخهٔ تازه به صندوق فرمان افزوده شد", $"{repository} · {branch}");
        return null;
    }

    /// <summary>Removes an extra source by its key. The primary repository/branch cannot be removed here.</summary>
    public string? RemoveSource(string key)
    {
        var settings = _services.Settings.Clone();
        var primary = settings.GitHub.PrimarySource();
        if (string.Equals(primary.Key, key, StringComparison.OrdinalIgnoreCase))
            return "شاخهٔ اصلی از این فهرست حذف نمی‌شود؛ برای تغییر آن از تنظیمات گیت‌هاب استفاده کنید.";
        var removed = settings.GitHub.Sources.RemoveAll(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
        if (removed == 0) return "این شاخه در فهرست پیدا نشد.";
        try
        {
            _services.SettingsStore.Save(settings);
        }
        catch (Exception ex)
        {
            return "ذخیرهٔ تنظیمات ناموفق بود: " + ex.Message;
        }
        _main.Settings.ApplyImported(settings);
        Service.ReloadSources();
        _services.Info("mailbox", "source.remove", "یک شاخه از صندوق فرمان برداشته شد", key);
        return null;
    }

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
