using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BazinoMarketing.App.Services;
using BazinoMarketing.App.ViewModels;
using BazinoMarketing.App.Views;
using BazinoMarketing.Core.Browser;
using BazinoMarketing.Core.Logging;
using BazinoMarketing.Core.Settings;
using BazinoMarketing.Core.Tools;

namespace BazinoMarketing.App.Screenshots;

/// <summary>
/// Renders the app pages to PNG files without showing a window, using sample data only
/// (no network, no DPAPI, temp folder). Used by CI so the agent can review the UI with vision.
/// </summary>
public static class ScreenshotRenderer
{
    public const int Width = 1280;
    public const int Height = 800;

    public static int RenderAll(string outDir)
    {
        Directory.CreateDirectory(outDir);
        // Screenshots must capture settled states, never mid-animation frames.
        Infrastructure.Motion.Enabled = false;
        var services = AppServices.CreateSample();
        SampleData.Populate(services);
        var main = new MainViewModel(services);
        SampleData.ApplyStates(main);

        var view = new MainView { DataContext = main, Width = Width, Height = Height };
        var pages = new (string Page, string File)[]
        {
            ("connection", "01-connection.png"),
            ("settings", "02-settings.png"),
            ("log", "03-log.png"),
            ("browser", "04-browser.png"),
            ("media", "05-media.png"),
            ("publish", "06-publish.png"),
            ("report", "07-report.png"),
        };

        var written = 0;
        foreach (var (page, file) in pages)
        {
            main.CurrentPage = page;
            Layout(view);
            DoEvents();
            Layout(view);
            DoEvents();
            var path = Path.Combine(outDir, file);
            Save(view, path);
            if (new FileInfo(path).Length > 1024) written++;
        }

        // The carousel reviewer must be provably browsable: shoot the publish card again after stepping one slide
        // forward, so the CI image shows slide «۲ از ۴» instead of the first slide.
        if (main.CurrentPage != "publish") main.CurrentPage = "publish";
        Layout(view);
        DoEvents();
        var firstCard = main.PublishQueue.Posts.FirstOrDefault();
        if (firstCard is not null)
        {
            firstCard.GoToSlide(1);
            Layout(view);
            DoEvents();
            Layout(view);
            DoEvents();
            Save(view, Path.Combine(outDir, "06b-publish-slide2.png"));
            firstCard.GoToSlide(0);
        }

        // Settings page is taller than the viewport: also capture it scrolled to the bottom.
        main.CurrentPage = "settings";
        Layout(view);
        DoEvents();
        if (FindScrollViewer(view) is { } sv)
        {
            sv.ScrollToEnd();
            Layout(view);
            DoEvents();
            Save(view, Path.Combine(outDir, "02b-settings-bottom.png"));
        }

        var info = $"rendered {written}/{pages.Length} pages at {Width}x{Height}, version {AppServices.Version}, {DateTimeOffset.Now:O}";
        // First line is a GitHub Actions workflow command: when CI prints this file, it becomes an annotation the agent can read via the API.
        File.WriteAllText(Path.Combine(outDir, "render-info.txt"), $"::notice title=marketing-app screenshots::{info}\n{info}\n");
        return written == pages.Length ? 0 : 3;
    }

    /// <summary>Writes render-error.txt with a single-line workflow-command header so the failure text surfaces as a CI annotation.</summary>
    public static void WriteRenderError(string outDir, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(outDir);
            var oneLine = ex.ToString().Replace("\r", " ").Replace("\n", " | ");
            if (oneLine.Length > 3500) oneLine = oneLine[..3500] + "…";
            File.AppendAllText(Path.Combine(outDir, "render-error.txt"), $"::error title=marketing-app render error::{oneLine}\n{ex}\n");
        }
        catch { }
    }

    private static void Layout(FrameworkElement view)
    {
        view.Measure(new Size(Width, Height));
        view.Arrange(new Rect(0, 0, Width, Height));
        view.UpdateLayout();
    }

    private static void Save(Visual visual, string path)
    {
        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    private static System.Windows.Controls.ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is System.Windows.Controls.ScrollViewer sv) return sv;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>Pumps the dispatcher so bindings, item containers and layout settle before rendering.</summary>
    public static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}

/// <summary>Believable but fake data for screenshots. No real key, token or account appears here.</summary>
public static class SampleData
{
    public static void Populate(AppServices services)
    {
        var s = services.Settings;
        s.Flux.Provider = "cloudflare";
        s.Flux.AccountId = "a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6";
        s.CustomCards.Add(new CustomCard
        {
            Id = "crm", Name = "CRM باشگاه مشتریان", Kind = "http", BaseUrl = "https://crm.example.com/api", AuthType = "header",
            HeaderName = "X-Api-Key", TestPath = "/v1/ping", ImportedFrom = "legacy-vault"
        });
        s.LegacyImport.Attempted = true;
        s.LegacyImport.LastImportedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-3);
        s.LegacyImport.LastSummary =
            "• توکن GitHub: وارد شد — *** 40 کاراکتر …Q7\n" +
            "• کلید Zernio: وارد شد — *** 32 کاراکتر …f9\n" +
            "• توکن Cloudflare (FLUX): وارد شد — *** 40 کاراکتر …aK\n" +
            "• شناسهٔ حساب Cloudflare: وارد شد — a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6\n" +
            "• سرویس سفارشی «CRM باشگاه مشتریان»: وارد شد — همراه کلید (*** 24 کاراکتر …x2)\n" +
            "• ورود Kling (MCP قدیمی): رد شد — در برنامهٔ جدید Kling فقط از CLI رسمی استفاده میکند؛ با «ورود» در کارت Kling دوباره وارد شوید.\n" +
            "• توکن GitHub از Bridge: از قبل موجود بود — توکن GitHub از vault یا تنظیمات فعلی حفظ شد";

        services.Secrets.Set(Core.Secrets.SecretKeys.GitHubToken, "sample-github-token-value-not-real-0001");
        services.Secrets.Set(Core.Secrets.SecretKeys.ZernioApiKey, "sample-zernio-key-not-real-0002");
        services.Secrets.Set(Core.Secrets.SecretKeys.FluxApiKey, "sample-cloudflare-token-not-real-0003");
        services.Secrets.Set(Core.Secrets.SecretKeys.GroqApiKey, "sample-groq-key-not-real-0005");
        services.Secrets.Set(Core.Secrets.SecretKeys.ForCustomCredential("crm"), "sample-crm-key-not-real-0004");

        var log = services.Log;
        var t0 = DateTimeOffset.Now.AddMinutes(-4);
        log.Append(new LogEvent { Timestamp = t0, Level = LogLevel.Info, Tool = "app", Category = "start", Message = "برنامه اجرا شد — نسخهٔ 0.1.0", Detail = "data=C:\\Users\\Payman\\AppData\\Local\\BazinoMarketing" });
        log.Append(new LogEvent { Timestamp = t0.AddSeconds(1), Level = LogLevel.Success, Tool = "import", Category = "legacy", Message = "کلیدهای نرمافزار قدیمی وارد شدند", Detail = "github=imported zernio=imported flux=imported custom=1 kling-oauth=skipped" });
        log.Append(new LogEvent { Timestamp = t0.AddSeconds(20), Level = LogLevel.Info, Tool = "app", Category = "connect", Message = "بررسی همهٔ ابزارها آغاز شد" });
        log.Append(new LogEvent { Timestamp = t0.AddSeconds(22), Level = LogLevel.Success, Tool = "github", Category = "tool.test", Message = "بررسی «GitHub (صندوق فرمان)»: متصل (paymanshafayan) — مخزن و شاخه در دسترساند", Detail = "repo=paymanshafayan/bazino-gamenet-portal private=True push=True branch=ok rate=4987/5000", DurationMs = 812 });
        log.Append(new LogEvent { Timestamp = t0.AddSeconds(31), Level = LogLevel.Error, Tool = "kling", Category = "tool.test", Message = "بررسی «Kling (ویدئو)»: وارد حساب Kling نشدهاید — دکمهٔ «ورود» را بزنید", Detail = "version=0.2.4 who_am_i status=401 exit=1", ErrorCode = "needs_login", DurationMs = 2380, Outcome = "failed" });
        log.Append(new LogEvent { Timestamp = t0.AddSeconds(33), Level = LogLevel.Success, Tool = "zernio", Category = "tool.test", Message = "بررسی «Zernio (شبکههای اجتماعی)»: متصل — 3 حساب اجتماعی وصل است", Detail = "health=200 accounts=3 platforms=instagram,telegram,linkedin", DurationMs = 1204 });
        log.Append(new LogEvent { Timestamp = t0.AddSeconds(35), Level = LogLevel.Success, Tool = "flux", Category = "tool.test", Message = "بررسی «FLUX (تصویر)»: متصل — مدل @cf/black-forest-labs/flux-1-schnell در دسترس است", Detail = "models=@cf/black-forest-labs/flux-1-schnell,@cf/black-forest-labs/flux-1-dev", DurationMs = 955 });
        log.Append(new LogEvent { Timestamp = t0.AddSeconds(37), Level = LogLevel.Error, Tool = "custom:crm", Category = "tool.test", Message = "بررسی «CRM باشگاه مشتریان»: دسترسی به شبکه/سرویس ممکن نشد", Detail = "custom:crm: HostNotFound — No such host is known.", ErrorCode = "network:HostNotFound", DurationMs = 61, Outcome = "failed" });
        log.Append(new LogEvent { Timestamp = t0.AddSeconds(38), Level = LogLevel.Info, Tool = "app", Category = "connect", Message = "بررسی همهٔ ابزارها پایان یافت", Detail = "3 از 5 ابزار متصل است — 2 مورد نیاز به توجه دارد" });
        log.Append(new LogEvent { Timestamp = t0.AddSeconds(70), Level = LogLevel.Warning, Tool = "kling", Category = "cli.login", Message = "پنجرهٔ ورود Kling باز شد (مرورگر باز میشود)" });
        log.Append(new LogEvent { Timestamp = t0.AddSeconds(120), Level = LogLevel.Info, Tool = "diag", Category = "probe", Message = "بررسی شبکه انجام شد", Detail = "api.github.com 200 (140 ms); zernio.com 200 (310 ms); api.cloudflare.com 200 (95 ms); api.klingai.com 403 (620 ms); registry.npmjs.org 200 (210 ms)" });
        log.Append(new LogEvent { Timestamp = t0.AddSeconds(125), Level = LogLevel.Info, Tool = "diag", Category = "bundle", Message = "بستهٔ عیبیابی کپی شد" });
    }

    public static void ApplyStates(MainViewModel main)
    {
        main.GitHub.SetSample(ToolState.Connected, "متصل (paymanshafayan) — مخزن و شاخه در دسترساند",
            "repo=paymanshafayan/bazino-gamenet-portal private=True push=True branch=ok user=paymanshafayan rate=4987/5000");
        main.Kling.SetSample(ToolState.NeedsLogin, "وارد حساب Kling نشدهاید — دکمهٔ «ورود» را بزنید", "version=0.2.4 who_am_i status=401 exit=1");
        main.Kling.SetSampleLocation("CLI: C:\\Users\\Payman\\AppData\\Roaming\\npm\\kling.cmd   |   Node: C:\\Program Files\\nodejs\\node.exe (24.5.0)   |   proxy: http://127.0.0.1:10809 (system)",
            "فرمانهای Kling از پراکسی http://127.0.0.1:10809 عبور میکنند.");
        main.Zernio.SetSample(ToolState.Connected, "متصل — 3 حساب اجتماعی وصل است", "health=200 accounts=3 platforms=instagram,telegram,linkedin");
        main.Flux.SetSample(ToolState.Connected, "متصل — مدل @cf/black-forest-labs/flux-1-schnell در دسترس است", "models=@cf/black-forest-labs/flux-1-schnell,@cf/black-forest-labs/flux-1-dev");
        main.Groq.SetSample(ToolState.Connected, "متصل — مدل صوتی whisper-large-v3 فعال و در دسترس است", "model=whisper-large-v3 whisper=whisper-large-v3,whisper-large-v3-turbo");
        foreach (var c in main.Settings.CustomCards)
            c.SetSample(ToolState.NetworkError, "دسترسی به شبکه/سرویس ممکن نشد", "custom:crm: HostNotFound — No such host is known.");
        main.Connection.SetSampleOverall();
        main.Connection.Mailbox.SetSample();
        main.Connection.SetSampleProbe(
            "✔ api.github.com HTTP 200 (140 ms)\n✔ zernio.com HTTP 200 (310 ms)\n✔ api.cloudflare.com HTTP 200 (95 ms)\n✔ api.klingai.com HTTP 403 (620 ms)\n✔ registry.npmjs.org HTTP 200 (210 ms)");
        main.Log.Refresh();
        main.Browser.SetSample(chromeRunning: false,
            statusText: "کروم با پورت اشکالیابی باز نیست. دکمهٔ «باز کردن کروم» را بزنید.",
            tabs: new[]
            {
                new BrowserTarget("A1B2C3", "Instagram — بازینو", "https://www.instagram.com/bazinopro/", "page", "ws://127.0.0.1:9334/devtools/page/A1B2C3"),
                new BrowserTarget("D4E5F6", "صفحهٔ اصلی سایت", "https://bazino.pro/", "page", "ws://127.0.0.1:9334/devtools/page/D4E5F6")
            },
            url: "https://www.instagram.com/bazinopro/",
            title: "نمونه برای نمایش چیدمان — BAZINO Gaming Lounge (@bazinopro)",
            pageText: "این متن یک نمونهٔ ساختگی است و هیچ دادهٔ واقعی حساب در آن نیست.\n\n" +
                      "وقتی کروم با پورت اشکالیابی باز شود، این کادر متنِ همان صفحهٔ باز را نشان میدهد: عنوان پستها، توضیحها، تعداد بازدید و همهٔ چیزی که خودتان در مرورگر میبینید.\n\n" +
                      "اینجانت فقط این متن را میخواند؛ رمز، کوکی و اطلاعات ذخیرهشدهٔ مرورگر هرگز خوانده یا فرستاده نمیشود.");
        main.Media.SetSample(
            @"C:\Users\Payman\Downloads\BazinoMarketing",
            new[]
            {
                new BazinoMarketing.Core.Media.MediaFileEntry("instagram_SamplePost_20260929/video.mp4", "video.mp4", ".mp4", 8421376, DateTimeOffset.UtcNow.AddMinutes(-4)),
                new BazinoMarketing.Core.Media.MediaFileEntry("edited/video_trimmed.mp4", "video_trimmed.mp4", ".mp4", 2185620, DateTimeOffset.UtcNow.AddMinutes(-2)),
                new BazinoMarketing.Core.Media.MediaFileEntry("previews/video.jpg", "video.jpg", ".jpg", 186422, DateTimeOffset.UtcNow.AddMinutes(-1))
            });
        main.PublishQueue.SetSample();
        main.Report.SetSample();
        main.Connection.RefreshTexts();
        main.RaiseHeaderChips();
    }
}
