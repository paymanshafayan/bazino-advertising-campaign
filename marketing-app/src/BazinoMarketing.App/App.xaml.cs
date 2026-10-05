using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using BazinoMarketing.App.Screenshots;
using BazinoMarketing.App.Services;
using BazinoMarketing.App.ViewModels;
using BazinoMarketing.Core.Publishing;

namespace BazinoMarketing.App;

public partial class App : Application
{
    private static AppServices? _services;
    private static MailboxHost? _mailbox;
    private Mutex? _singleInstance;
    private CancellationTokenSource? _publishingCts;
    private Task? _publishingTask;

    public static AppServices? Services => _services;
    /// <summary>Wakes the command mailbox right away (after settings/token changes); harmless when it is not running.</summary>
    public static void MailboxPollNow()
    {
        try { _mailbox?.Service.PollNow(); } catch { }
    }

    private static int _restartScheduled;

    /// <summary>
    /// Self-update / restart requested through the command mailbox. After <paramref name="delay"/> (so the reply reaches the agent first)
    /// the running EXE is renamed to «.old», the verified download (if any) is copied in its place, the single-instance lock is released,
    /// the new EXE is started and this instance shuts down. Returns an error text (no restart scheduled) or null.
    /// </summary>
    public static Task<string?> ScheduleRestartAsync(string? newExe, TimeSpan delay)
    {
        var current = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(current) || !File.Exists(current)) return Task.FromResult<string?>("current executable path is unknown");
        if (newExe is not null && !File.Exists(newExe)) return Task.FromResult<string?>("downloaded file not found: " + newExe);
        if (IsRenderMode) return Task.FromResult<string?>("not available in render mode");
        if (Interlocked.Exchange(ref _restartScheduled, 1) == 1) return Task.FromResult<string?>("a restart is already scheduled");
        if (delay < TimeSpan.FromSeconds(1)) delay = TimeSpan.FromSeconds(1);
        _services?.Info("app", "restart", newExe is null ? "راه‌اندازی دوباره برنامه زمان‌بندی شد" : "به‌روزرسانی و راه‌اندازی دوباره زمان‌بندی شد",
            $"in {delay.TotalSeconds:0}s; exe={current}; new={newExe ?? "-"}");
        _ = Task.Run(async () =>
        {
            await Task.Delay(delay).ConfigureAwait(false);
            // Let the mailbox finish writing the reply / heartbeat first (bounded).
            for (var i = 0; i < 60 && _mailbox?.Service.IsBusy == true; i++)
                await Task.Delay(500).ConfigureAwait(false);
            try
            {
                await Current.Dispatcher.InvokeAsync(() => PerformRestart(current, newExe)).Task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _restartScheduled, 0);
                ReportUnexpected(ex, "restart");
            }
        });
        return Task.FromResult<string?>(null);
    }

    private static void PerformRestart(string current, string? newExe)
    {
        // 1) Stop the mailbox first (bounded) so the heartbeat says «stopped» before the new instance takes over.
        try { _mailbox?.StopAsync().Wait(TimeSpan.FromSeconds(5)); } catch { }

        // 2) Swap the executable. A running EXE can be renamed on Windows (not deleted), so: current → .old, download → current.
        if (newExe is not null)
        {
            var old = current + ".old";
            try { if (File.Exists(old)) File.Delete(old); } catch { }
            File.Move(current, old);
            try
            {
                File.Copy(newExe, current, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(current)) File.Delete(current); } catch { }
                File.Move(old, current); // roll back, keep running the old version
                throw;
            }
            try { _services?.Info("app", "update", "فایل برنامه با نسخهٔ تازه جایگزین شد", $"from {newExe}"); } catch { }
        }

        // 3) Release the single-instance lock so the child can start, then launch it and leave.
        var app = Current as App;
        try { app?._singleInstance?.ReleaseMutex(); } catch { }
        try { app?._singleInstance?.Dispose(); } catch { }
        if (app is not null) app._singleInstance = null;
        Process.Start(new ProcessStartInfo(current) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(current) ?? Environment.CurrentDirectory });
        Current.Shutdown(0);
    }

    /// <summary>Best-effort removal of the previous executable left behind by a self-update (the old process may still be exiting).</summary>
    private static void CleanUpOldExecutable()
    {
        try
        {
            var current = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(current)) return;
            var old = current + ".old";
            if (!File.Exists(old)) return;
            _ = Task.Run(async () =>
            {
                for (var i = 0; i < 10; i++)
                {
                    try { File.Delete(old); return; } catch { }
                    await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                }
            });
        }
        catch { }
    }
    /// <summary>True while rendering screenshots for CI: never show a dialog (it would hang the runner).</summary>
    public static bool IsRenderMode { get; private set; }
    public static string? RenderDir { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Diagnostic first step: even if startup dies inside WPF, this marker shows how far the process got.
        var earlyRenderDir = RenderDirFromArgs(e.Args);
        if (earlyRenderDir is not null) ScreenshotRenderer.Stage(earlyRenderDir, "stage-00-onstartup-enter");
        base.OnStartup(e);
        if (earlyRenderDir is not null) ScreenshotRenderer.Stage(earlyRenderDir, "stage-01-base-onstartup");
        var culture = CultureInfo.GetCultureInfo("fa-IR");
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => ReportUnexpected(args.ExceptionObject as Exception ?? new Exception("unknown"), "domain");
        TaskScheduler.UnobservedTaskException += (_, args) => { ReportUnexpected(args.Exception, "task"); args.SetObserved(); };

        var args = e.Args;
        var shotIndex = Array.FindIndex(args, a => string.Equals(a, "--render-screenshots", StringComparison.OrdinalIgnoreCase));
        if (shotIndex >= 0)
        {
            // A path with spaces may arrive split into several arguments when the caller did not quote it; re-join it.
            var outDir = shotIndex + 1 < args.Length
                ? string.Join(" ", args.Skip(shotIndex + 1)).Trim().Trim('"')
                : Path.Combine(Environment.CurrentDirectory, "screenshots");
            IsRenderMode = true;
            RenderDir = outDir;
            var code = 1;
            try
            {
                code = ScreenshotRenderer.RenderAll(outDir);
            }
            catch (Exception ex)
            {
                ScreenshotRenderer.Stage(outDir, "stage-02-renderall-caught");
                ScreenshotRenderer.WriteRenderError(outDir, ex);
            }
            ScreenshotRenderer.Stage(outDir, "stage-03-before-shutdown");
            Shutdown(code);
            return;
        }

        var smoke = args.Any(a => string.Equals(a, "--smoke", StringComparison.OrdinalIgnoreCase));
        var dailyInsights = args.Any(a => string.Equals(a, "--daily-insights", StringComparison.OrdinalIgnoreCase));
        if (!smoke && !dailyInsights)
        {
            _singleInstance = new Mutex(true, "Local\\BazinoMarketingStudio", out var createdNew);
            if (!createdNew)
            {
                MessageBox.Show("برنامه از قبل باز است.", "استودیوی بازینو", MessageBoxButton.OK, MessageBoxImage.Information,
                    MessageBoxResult.OK, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                Shutdown(0);
                return;
            }
        }

        try
        {
            _services = AppServices.CreateReal();
            _services.Info("app", "start", $"برنامه اجرا شد — نسخهٔ {AppServices.Version}", $"data={_services.Paths.Root}; exe={Environment.ProcessPath}");
            if (dailyInsights)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                _ = RunDailyInsightsHeadlessAsync(_services);
                return;
            }
            CleanUpOldExecutable();
            if (!smoke && OperatingSystem.IsWindows()) _ = EnsureDailyInsightsTaskAsync(_services);
            // Owner rule (2026-10-03): every launch past 08:00 must make sure today's report exists, judged only by the
            // marker file the app keeps in its own folder. The scheduler job and this check back each other up.
            if (!smoke && OperatingSystem.IsWindows()) _ = StartupDailyReportCheckAsync(_services);
            var wasImportedBefore = _services.Settings.LegacyImport.Attempted;
            var report = _services.RunLegacyImportIfNeeded();
            if (wasImportedBefore && report is { AnyChanged: false, AnyFailed: false }) report = null;

            var vm = new MainViewModel(_services);
            var window = new MainWindow(vm);
            MainWindow = window;
            window.Closed += (_, _) =>
            {
                vm.Media.CancelCurrentOperation();
                Shutdown(0);
            };
            window.Show();

            // Phase 2: the command mailbox listens automatically whenever GitHub is configured. No approval step (owner's decision, 2026-09-27).
            try
            {
                _mailbox = new MailboxHost(_services, vm, Dispatcher, window);
                if (!smoke) _mailbox.Start();
            }
            catch (Exception ex)
            {
                ReportUnexpected(ex, "mailbox-start");
            }

            if (!smoke)
            {
                _publishingCts = new CancellationTokenSource();
                _publishingTask = Task.Run(() => _services.PublishQueue.RunSchedulerAsync(_publishingCts.Token));
            }

            if (report is { AnySourceFound: true })
            {
                var text = report.AnyImported
                    ? "کلیدها و تنظیمات نرم‌افزار قدیمی به‌صورت خودکار وارد شدند:\n\n" + report.ToSummary()
                    : "نرم‌افزار قدیمی پیدا شد اما چیزی وارد نشد:\n\n" + report.ToSummary();
                MessageBox.Show(window, text, "وارد کردن از نرم‌افزار قدیمی", MessageBoxButton.OK,
                    report.AnyFailed ? MessageBoxImage.Warning : MessageBoxImage.Information,
                    MessageBoxResult.OK, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            }

            if (smoke)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                timer.Tick += (_, _) => { timer.Stop(); _services.Info("app", "smoke", "smoke test ok"); Shutdown(0); };
                timer.Start();
            }
        }
        catch (Exception ex)
        {
            ReportUnexpected(ex, dailyInsights ? "daily-insights-startup" : "startup");
            if (!dailyInsights)
                MessageBox.Show("اجرای برنامه ممکن نشد:\n" + ex.Message, "استودیوی بازینو", MessageBoxButton.OK, MessageBoxImage.Error,
                    MessageBoxResult.OK, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            Shutdown(2);
        }
    }

    private static async Task EnsureDailyInsightsTaskAsync(AppServices services)
    {
        try
        {
            var result = await WindowsDailyInsightsTask.EnsureInstalledAsync(Environment.ProcessPath).ConfigureAwait(false);
            if (result.Succeeded) services.Info("instagram-insights", "schedule", "گزارش روزانه برای ساعت ۸ صبح ویندوز زمان‌بندی شد", result.Detail);
            else services.Warn("instagram-insights", "schedule", "زمان‌بندی گزارش روزانه در Task Scheduler انجام نشد", result.Detail, "task_registration_failed");
        }
        catch (Exception ex) { services.Warn("instagram-insights", "schedule", "زمان‌بندی گزارش روزانه انجام نشد", ex.Message, "task_registration_failed"); }
    }

    /// <summary>
    /// Runs on every launch: if the local clock is past 08:00 and the marker file has no entry for today, the daily
    /// report is produced now and committed to the configured branch. Failures are only logged — never a dialog.
    /// </summary>
    private static async Task StartupDailyReportCheckAsync(AppServices services)
    {
        try
        {
            var now = DateTimeOffset.Now;
            var markerPath = DailyReportMarker.PathFor(services.Paths.Root);
            var marker = DailyReportMarker.Read(services.Paths.Root);
            if (!DailyReportMarker.IsDue(now, marker))
            {
                services.Info("instagram-insights", "startup-check",
                    marker.HasReportFor(DateOnly.FromDateTime(now.DateTime))
                        ? "گزارش روزانهٔ امروز از قبل ثبت شده است"
                        : $"زمان محلی هنوز به ساعت {DailyReportMarker.ProductionHour:00}:00 نرسیده است؛ بررسی گزارش امروز به بعد سپرده شد",
                    $"marker={markerPath}; lastReportDate={marker.LastReportDate}");
                return;
            }

            services.Info("instagram-insights", "startup-check",
                "گزارش امروز در فایل نشانه ثبت نشده است؛ ساخت و ارسال گزارش روزانه آغاز شد",
                $"marker={markerPath}; localTime={DailyReportMarker.Stamp(now)}");
            var result = await services.DailyInsights.RunAsync().ConfigureAwait(false);
            RecordDailyReport(services, result, now);
            if (result.Succeeded) services.Success("instagram-insights", "startup-check", result.Message, result.Path ?? "");
            else services.Error("instagram-insights", "startup-check", result.Message, result.Path ?? "", "daily_report_failed");
        }
        catch (Exception ex)
        {
            services.Warn("instagram-insights", "startup-check", "بررسی گزارش روزانه در شروع برنامه انجام نشد", ex.Message, "daily_report_check_failed");
        }
    }

    /// <summary>Writes the marker file that makes the next launch skip today (success or "already in the branch").</summary>
    private static void RecordDailyReport(AppServices services, DailyInsightsResult result, DateTimeOffset now)
    {
        var marker = DailyReportMarker.Read(services.Paths.Root);
        marker.LastAttemptAtLocal = DailyReportMarker.Stamp(now);
        marker.LastAttemptResult = result.Message;
        if (result.Succeeded)
        {
            marker.LastReportDate = DateOnly.FromDateTime(now.DateTime).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            marker.LastReportAtLocal = DailyReportMarker.Stamp(now);
            marker.LastReportPath = result.Path ?? "";
            marker.LastReportBranch = services.Settings.GitHub.Branch;
            marker.LastReportStatus = result.Skipped ? "already-in-branch" : "uploaded";
        }
        DailyReportMarker.Write(services.Paths.Root, marker);
    }

    private static async Task RunDailyInsightsHeadlessAsync(AppServices services)
    {
        try
        {
            services.Info("instagram-insights", "daily-run", "اجرای headless گزارش روزانه آغاز شد");
            var startedAt = DateTimeOffset.Now;
            var result = await services.DailyInsights.RunAsync().ConfigureAwait(false);
            RecordDailyReport(services, result, startedAt);
            if (result.Succeeded) services.Success("instagram-insights", "daily-run", result.Message, result.Path ?? "");
            else services.Error("instagram-insights", "daily-run", result.Message, result.Path ?? "", "daily_report_failed");
            Current.Dispatcher.Invoke(() => Current.Shutdown(result.Succeeded ? 0 : 1));
        }
        catch (Exception ex)
        {
            services.Error("instagram-insights", "daily-run", "اجرای headless گزارش روزانه شکست خورد", ex.Message, "daily_report_failed");
            Current.Dispatcher.Invoke(() => Current.Shutdown(2));
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _publishingCts?.Cancel(); } catch { }
        try { _publishingTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _publishingCts?.Dispose();
        try
        {
            // Tell the agent we are gone (bounded: never keep the process alive on a bad network).
            _mailbox?.StopAsync().Wait(TimeSpan.FromSeconds(5));
        }
        catch { }
        try { _services?.Info("app", "exit", "برنامه بسته شد", $"code={e.ApplicationExitCode}"); } catch { }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ReportUnexpected(e.Exception, "ui");
        if (IsRenderMode)
        {
            // No dialogs on the CI runner: record the error next to the screenshots and stop with a distinct exit code.
            e.Handled = true;
            ScreenshotRenderer.WriteRenderError(RenderDir ?? Path.GetTempPath(), e.Exception);
            Shutdown(4);
            return;
        }
        e.Handled = true;
        try
        {
            MessageBox.Show("خطای غیرمنتظره رخ داد و در لاگ ثبت شد:\n" + e.Exception.Message, "استودیوی بازینو",
                MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        }
        catch { }
    }

    /// <summary>Reads the screenshot output folder straight from the command line (before anything else runs).</summary>
    private static string? RenderDirFromArgs(string[] args)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, "--render-screenshots", StringComparison.OrdinalIgnoreCase));
        if (index < 0) return null;
        return index + 1 < args.Length ? string.Join(" ", args.Skip(index + 1)).Trim().Trim('"') : null;
    }

    public static void ReportUnexpected(Exception ex, string where)
    {
        try
        {
            if (IsRenderMode && !string.IsNullOrWhiteSpace(RenderDir))
                File.WriteAllText(Path.Combine(RenderDir!, "render-unhandled.txt"), $"{DateTimeOffset.Now:O} [{where}] {ex}");
        }
        catch { }
        try
        {
            _services?.Error("app", "unhandled", $"خطای غیرمنتظره ({where}): {ex.Message}", ex.ToString(), "unhandled");
        }
        catch { }
        try
        {
            var fallback = Path.Combine(Path.GetTempPath(), "BazinoMarketing-crash.log");
            File.AppendAllText(fallback, $"{DateTimeOffset.Now:O} [{where}] {ex}\n");
        }
        catch { }
    }
}
