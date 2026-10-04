using System.Diagnostics;
using System.IO;
using System.Reflection;
using BazinoMarketing.Core.Browser;
using BazinoMarketing.Core.Logging;
using BazinoMarketing.Core.Media;
using BazinoMarketing.Core.Publishing;
using BazinoMarketing.Core.Secrets;
using BazinoMarketing.Core.Secrets.Legacy;
using BazinoMarketing.Core.Settings;

namespace BazinoMarketing.App.Services;

/// <summary>Composition root: one instance per process, created in App.OnStartup (or in sample mode for screenshots).</summary>
public sealed class AppServices
{
    public AppPaths Paths { get; }
    public SettingsStore SettingsStore { get; }
    public AppSettings Settings { get; private set; }
    public ISecretStore Secrets { get; }
    public Redactor Redactor { get; }
    public JsonlLogStore Log { get; }
    /// <summary>The bridge to the owner's Chrome. One per process: the UI page and the mailbox share it.</summary>
    public BrowserBridge Browser { get; }
    public MediaService Media { get; }
    public PublishQueueService PublishQueue { get; }
    public DailyInsightsService DailyInsights { get; }
    public bool IsSampleMode { get; }

    public static string Version =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    private AppServices(AppPaths paths, ISecretStore secrets, bool sampleMode)
    {
        Paths = paths;
        IsSampleMode = sampleMode;
        SettingsStore = new SettingsStore(paths.Root);
        Settings = sampleMode ? new AppSettings() : SafeLoadSettings();
        Secrets = secrets;
        Redactor = new Redactor(Secrets.AllValues);
        Log = new JsonlLogStore(paths.Logs, Redactor);
        Browser = new BrowserBridge(() => Settings.Browser, (tool, category, message, detail) =>
            Log.Append(LogLevel.Info, tool, category, message, detail));
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var mediaFolder = string.IsNullOrWhiteSpace(userProfile)
            ? paths.Downloads
            : Path.Combine(userProfile, "Downloads", "BazinoMarketing");
        Media = new MediaService(() => Settings, Secrets, mediaFolder);
        PublishQueue = new PublishQueueService(() => Settings, Secrets, Log, Media);
        DailyInsights = new DailyInsightsService(() => Settings, Secrets, (level, category, message, detail) =>
        {
            var logLevel = level switch
            {
                "success" => LogLevel.Success,
                "warning" => LogLevel.Warning,
                "error" => LogLevel.Error,
                _ => LogLevel.Info
            };
            Log.Append(logLevel, "instagram-insights", category, message, detail, outcome: level == "error" ? "failed" : "ok");
        });
    }

    public static AppServices CreateReal()
    {
        var paths = AppPaths.Default();
        paths.EnsureCreated();
        return new AppServices(paths, new DpapiSecretStore(paths.Root), sampleMode: false);
    }

    /// <summary>Everything in a temp folder + in-memory secrets. Used by --render-screenshots; no network, no DPAPI.</summary>
    public static AppServices CreateSample()
    {
        var paths = AppPaths.Temporary("sample-" + Environment.ProcessId);
        paths.EnsureCreated();
        return new AppServices(paths, new InMemorySecretStore(), sampleMode: true);
    }

    private AppSettings SafeLoadSettings()
    {
        try
        {
            return SettingsStore.Load();
        }
        catch (Exception ex)
        {
            // Keep the broken file for inspection and start with defaults.
            try { File.Copy(SettingsStore.FilePath, SettingsStore.FilePath + ".broken", overwrite: true); } catch { }
            var s = new AppSettings();
            s.LegacyImport.LastSummary = "settings.json قابل خواندن نبود و بازنشانی شد: " + ex.Message;
            return s;
        }
    }

    public void SaveSettings()
    {
        if (IsSampleMode) return;
        SettingsStore.Save(Settings);
    }

    public void ReplaceSettings(AppSettings settings)
    {
        Settings = settings;
        SaveSettings();
    }

    public LogEvent Info(string tool, string category, string message, string detail = "", Dictionary<string, string>? data = null) =>
        Log.Append(LogLevel.Info, tool, category, message, detail, data: data);

    public LogEvent Success(string tool, string category, string message, string detail = "", long? durationMs = null) =>
        Log.Append(LogLevel.Success, tool, category, message, detail, durationMs, outcome: "ok");

    public LogEvent Warn(string tool, string category, string message, string detail = "", string? errorCode = null) =>
        Log.Append(LogLevel.Warning, tool, category, message, detail, errorCode: errorCode);

    public LogEvent Error(string tool, string category, string message, string detail = "", string? errorCode = null, long? durationMs = null) =>
        Log.Append(LogLevel.Error, tool, category, message, detail, durationMs, errorCode, outcome: "failed");

    /// <summary>Runs the one-time legacy import on first launch. Returns the report or null when nothing was attempted.</summary>
    public ImportReport? RunLegacyImportIfNeeded()
    {
        if (IsSampleMode || !OperatingSystem.IsWindows() || !LegacyImporter.ShouldRunAutomatically(Settings.LegacyImport)) return null;
        if (Settings.LegacyImport.Attempted)
            Info("import", "legacy", $"بازبینی دوبارهٔ کلیدهای واردشده با واردکنندهٔ نسخهٔ {LegacyImporter.CurrentVersion} (مقدارهای معتبر دست نمی‌خورند)");
        return RunLegacyImport(overwrite: false);
    }

    public ImportReport RunLegacyImport(bool overwrite)
    {
        var importer = new LegacyImporter(DpapiSecretStore.UnprotectRaw);
        var report = importer.Import(Settings, Secrets, overwrite);
        SaveSettings();
        var level = report.AnyFailed ? LogLevel.Warning : report.AnyImported ? LogLevel.Success : LogLevel.Info;
        Log.Append(level, "import", "legacy", report.AnyImported ? "کلیدهای نرم‌افزار قدیمی وارد شدند" : "چیزی برای وارد کردن از نرم‌افزار قدیمی پیدا نشد",
            report.ToSummary(), outcome: report.AnyFailed ? "failed" : "ok");
        return report;
    }

    public static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.ReportUnexpected(ex, "open-folder");
        }
    }

    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.ReportUnexpected(ex, "open-url");
        }
    }

    /// <summary>Opens a visible console window running one command (used for `npm i -g …` and `kling login`, which need the owner's eyes).</summary>
    /// <summary>
    /// Opens a visible console that runs <paramref name="command"/> and waits for a key press before closing, so the owner
    /// can read the output. Returns the process so callers can react when the window is closed (e.g. re-check a tool).
    /// <paramref name="environment"/> entries with a null value are removed from the child's environment.
    /// </summary>
    public static Process? RunInVisibleConsole(string command, string title, IReadOnlyDictionary<string, string?>? environment = null)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var safeTitle = title.Replace("\"", "").Replace("&", "");
        var echo = command.Replace("\"", "").Replace("&", "^&").Replace("|", "^|").Replace(">", "^>").Replace("<", "^<");
        var script = $"title {safeTitle} & echo {echo} & {command} & echo. & echo Finished. Close this window (press any key) to let the app re-check. & pause >nul";
        var psi = new ProcessStartInfo
        {
            FileName = cmd,
            Arguments = "/d /c \"" + script + "\"",
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        if (environment is not null)
            foreach (var (key, value) in environment)
            {
                if (value is null) psi.Environment.Remove(key);
                else psi.Environment[key] = value;
            }
        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.Start();
        return process;
    }
}
