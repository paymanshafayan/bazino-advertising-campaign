using System.Text;
using BazinoMarketing.Core.Settings;

namespace BazinoMarketing.Core.Secrets.Legacy;

public enum ImportStatus { Imported, AlreadyPresent, Skipped, NotFound, Failed, Invalid, Removed }

public sealed record ImportItem(string Label, ImportStatus Status, string Note);

public sealed class ImportReport
{
    public List<ImportItem> Items { get; } = new();
    public DateTimeOffset At { get; } = DateTimeOffset.UtcNow;

    public bool AnyImported => Items.Any(i => i.Status == ImportStatus.Imported);
    public bool AnyFailed => Items.Any(i => i.Status is ImportStatus.Failed or ImportStatus.Invalid);
    public bool AnyChanged => Items.Any(i => i.Status is ImportStatus.Imported or ImportStatus.Removed);
    public bool AnySourceFound => Items.Any(i => i.Status != ImportStatus.NotFound);

    public void Add(string label, ImportStatus status, string note = "") => Items.Add(new ImportItem(label, status, note));

    public static string StatusText(ImportStatus s) => s switch
    {
        ImportStatus.Imported => "وارد شد",
        ImportStatus.AlreadyPresent => "از قبل موجود بود",
        ImportStatus.Skipped => "رد شد",
        ImportStatus.NotFound => "یافت نشد",
        ImportStatus.Failed => "ناموفق",
        ImportStatus.Invalid => "نامعتبر — وارد نشد",
        ImportStatus.Removed => "مقدار خرابِ قبلی حذف شد",
        _ => s.ToString()
    };

    public string ToSummary()
    {
        var sb = new StringBuilder();
        foreach (var i in Items)
        {
            sb.Append("• ").Append(i.Label).Append(": ").Append(StatusText(i.Status));
            if (!string.IsNullOrWhiteSpace(i.Note)) sb.Append(" — ").Append(i.Note);
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }
}

/// <summary>
/// Imports keys/tokens from the previous Windows apps so the owner does not have to re-enter them:
/// <list type="bullet">
/// <item><c>%APPDATA%\BazinoMarketingBrowser\vault.json</c> (GitHub token, Zernio key, Cloudflare/FLUX token + account, custom connectors)</item>
/// <item><c>%APPDATA%\BazinoBridge\settings.json</c> (GitHub token used by the browser bridge; fallback only)</item>
/// </list>
/// Secret values are written straight into the <see cref="ISecretStore"/>; the report never contains them.
/// </summary>
public sealed class LegacyImporter
{
    /// <summary>Bump when the importer learns to read something better; older installs re-run it once (non-destructively).</summary>
    public const int CurrentVersion = 2;

    private readonly Func<byte[], byte[]> _unprotect;
    private readonly string _appData;

    public static bool ShouldRunAutomatically(LegacyImportState state) => !state.Attempted || state.Version < CurrentVersion;

    public LegacyImporter(Func<byte[], byte[]> unprotect, string? appDataRoot = null)
    {
        _unprotect = unprotect ?? throw new ArgumentNullException(nameof(unprotect));
        _appData = appDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    }

    public string VaultPath => Path.Combine(_appData, "BazinoMarketingBrowser", "vault.json");
    public string BridgeSettingsPath => Path.Combine(_appData, "BazinoBridge", "settings.json");
    public bool AnySourceExists => File.Exists(VaultPath) || File.Exists(BridgeSettingsPath);

    public ImportReport Import(AppSettings settings, ISecretStore secrets, bool overwriteExisting = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(secrets);
        var report = new ImportReport();

        if (File.Exists(VaultPath))
        {
            try
            {
                var data = LegacyVaultReader.Read(File.ReadAllText(VaultPath), _unprotect);
                if (data.RepairedEncoding)
                    report.Add("متن‌های فارسی نرم‌افزار قدیمی", ImportStatus.Imported, "کدگذاری خراب‌شدهٔ PowerShell ترمیم شد");
                ApplyVault(data, settings, secrets, overwriteExisting, report);
            }
            catch (Exception ex)
            {
                report.Add("vault.json نرم‌افزار قدیمی", ImportStatus.Failed, Describe(ex));
            }
        }
        else
        {
            report.Add("vault.json نرم‌افزار قدیمی", ImportStatus.NotFound, VaultPath);
        }

        // Bridge token: only as a fallback when no GitHub token was imported/present.
        if (File.Exists(BridgeSettingsPath))
        {
            try
            {
                var token = LegacyBridgeSettingsReader.ReadToken(File.ReadAllText(BridgeSettingsPath), _unprotect);
                if (token is null)
                    report.Add("توکن GitHub از Bridge", ImportStatus.Skipped, "فایل توکن قابل‌خواندن نبود");
                else if (HasUsable(secrets, SecretKeys.GitHubToken) && !overwriteExisting)
                    report.Add("توکن GitHub از Bridge", ImportStatus.AlreadyPresent, "توکن GitHub از vault یا تنظیمات فعلی حفظ شد");
                else
                    ImportSecret(secrets, SecretKeys.GitHubToken, SecretKind.GitHubToken, token, "توکن GitHub از Bridge", true, report);
            }
            catch (Exception ex)
            {
                report.Add("توکن GitHub از Bridge", ImportStatus.Failed, Describe(ex));
            }
        }
        else
        {
            report.Add("تنظیمات Bridge (BazinoBridge)", ImportStatus.NotFound, BridgeSettingsPath);
        }

        settings.LegacyImport.Attempted = true;
        settings.LegacyImport.Version = CurrentVersion;
        settings.LegacyImport.LastSummary = report.ToSummary();
        if (report.AnyImported) settings.LegacyImport.LastImportedAtUtc = report.At;
        return report;
    }

    /// <summary>A stored value counts as present only when it could actually be sent; broken values get replaced.</summary>
    private static bool HasUsable(ISecretStore secrets, string key) =>
        secrets.TryGet(key, out var existing) && SecretFormat.IsHeaderSafe(existing);

    private static void ApplyVault(LegacyVaultData data, AppSettings settings, ISecretStore secrets, bool overwrite, ImportReport report)
    {
        ImportSecret(secrets, SecretKeys.GitHubToken, SecretKind.GitHubToken, data.GitHubToken, "توکن GitHub", overwrite, report);
        ImportSecret(secrets, SecretKeys.ZernioApiKey, SecretKind.ZernioKey, data.ZernioKey, "کلید Zernio", overwrite, report);

        var fluxImported = ImportSecret(secrets, SecretKeys.FluxApiKey, SecretKind.CloudflareToken, data.CloudflareToken, "توکن Cloudflare (FLUX)", overwrite, report);

        var accountCheck = SecretFormat.Check(SecretKind.CloudflareAccountId, data.CloudflareAccountId);
        var currentAccountOk = SecretFormat.LooksLikeHex32(settings.Flux.AccountId?.Trim() ?? "");
        if (!string.IsNullOrWhiteSpace(data.CloudflareAccountId) && (overwrite || !currentAccountOk))
        {
            if (accountCheck.IsUsable)
            {
                settings.Flux.AccountId = accountCheck.Value!;
                report.Add("شناسهٔ حساب Cloudflare", ImportStatus.Imported, accountCheck.Extracted ? "از داخل متن اضافه جدا شد" : "");
            }
            else
                report.Add("شناسهٔ حساب Cloudflare", ImportStatus.Invalid, "باید ۳۲ نویسهٔ hex باشد — " + accountCheck.Problem);
        }
        if ((fluxImported || HasUsable(secrets, SecretKeys.FluxApiKey)) && !string.IsNullOrWhiteSpace(settings.Flux.AccountId) &&
            settings.Flux.Provider == "none")
        {
            settings.Flux.Provider = "cloudflare";
        }

        foreach (var con in data.Connectors)
        {
            var label = $"سرویس سفارشی «{(string.IsNullOrWhiteSpace(con.Name) ? con.Id : con.Name)}»";
            var existing = settings.CustomCards.FirstOrDefault(c => string.Equals(c.Id, con.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is not null && !overwrite)
            {
                // An earlier importer may have stored the OEM-mangled name; fix the text without touching anything else.
                var wantedName = string.IsNullOrWhiteSpace(con.Name) ? con.Id : con.Name;
                if (existing.Name != wantedName && OemMojibake.TryRepair(existing.Name, out var repairedOld) && repairedOld == wantedName)
                {
                    existing.Name = wantedName;
                    report.Add(label, ImportStatus.Imported, "فقط نام ترمیم شد؛ بقیهٔ تنظیمات دست نخورد");
                    continue;
                }
                report.Add(label, ImportStatus.AlreadyPresent);
                continue;
            }
            var card = existing ?? new CustomCard { Id = con.Id };
            card.Name = string.IsNullOrWhiteSpace(con.Name) ? con.Id : con.Name;
            card.Kind = con.Kind == "mcp" ? "mcp" : "http";
            card.BaseUrl = con.Url;
            card.AuthType = con.Auth switch { "bearer" => "bearer", "header" => "header", _ => "none" };
            card.HeaderName = con.HeaderName;
            card.ImportedFrom = "legacy-vault";
            var notes = new List<string>();
            if (con.Auth == "oauth")
                notes.Add("اتصال OAuth نسخهٔ قدیمی منتقل نشد؛ در صورت نیاز دوباره مجوز بگیرید.");
            if (con.AgentAccess != "none")
                notes.Add($"سطح دسترسی قدیمی ایجنت: {con.AgentAccess} (در برنامهٔ جدید از کلیدهای مجوز کارت استفاده می‌شود).");
            card.Notes = string.Join(" ", notes);
            if (existing is null) settings.CustomCards.Add(card);

            if (!string.IsNullOrWhiteSpace(con.Credential))
            {
                var check = SecretFormat.Check(SecretKind.Generic, con.Credential);
                if (check.IsUsable)
                {
                    secrets.Set(SecretKeys.ForCustomCredential(card.Id), check.Value!);
                    report.Add(label, ImportStatus.Imported, $"همراه کلید ({Masked(check.Value!)}{(check.Extracted ? "؛ از داخل متن اضافه جدا شد" : "")})");
                }
                else
                    report.Add(label, ImportStatus.Imported, "بدون کلید — کلید ذخیره‌شده نامعتبر بود (" + check.Problem + ")؛ در تنظیمات دوباره وارد کنید");
            }
            else
            {
                report.Add(label, ImportStatus.Imported, con.Auth == "oauth" ? "بدون توکن OAuth" : "بدون کلید");
            }
        }

        if (data.HasKlingOAuth)
            report.Add("ورود Kling (MCP قدیمی)", ImportStatus.Skipped, "در برنامهٔ جدید Kling فقط از CLI رسمی استفاده می‌کند؛ با «ورود» در کارت Kling دوباره وارد شوید.");
        var relay = data.RelayKeyNames.ToList();
        if (relay.Count > 0)
            report.Add("کلیدهای relay قدیمی", ImportStatus.Skipped, $"{relay.Count} کلید؛ در طرح جدید استفاده نمی‌شوند.");
    }

    private static bool ImportSecret(ISecretStore secrets, string key, SecretKind kind, string value, string label, bool overwrite, ImportReport report)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            report.Add(label, ImportStatus.NotFound, "در vault قدیمی ثبت نشده بود");
            return false;
        }
        var existingBroken = secrets.TryGet(key, out var existing) && !SecretFormat.IsHeaderSafe(existing);
        if (secrets.Has(key) && !existingBroken && !overwrite)
        {
            report.Add(label, ImportStatus.AlreadyPresent);
            return false;
        }

        var check = SecretFormat.Check(kind, value);
        if (!check.IsUsable)
        {
            // Never store something that can only produce header/network errors later; drop a broken earlier import too.
            if (existingBroken) secrets.Remove(key);
            report.Add(label, ImportStatus.Invalid,
                $"مقدار ذخیره‌شده در نرم‌افزار قدیمی شکل یک کلید را ندارد ({check.Problem})" +
                (existingBroken ? "؛ نسخهٔ خرابی که قبلاً وارد شده بود حذف شد" : "") + " — لطفاً کلید را در تنظیمات دوباره وارد کنید");
            return false;
        }

        secrets.Set(key, check.Value!);
        var note = Masked(check.Value!);
        if (check.Extracted) note += " — از داخل متن اضافهٔ ذخیره‌شده جدا شد؛ با «آزمایش اتصال» درستی‌اش را بررسی کنید";
        else if (!check.Recognised && kind is SecretKind.GitHubToken or SecretKind.CloudflareToken) note += " — شکل ناآشنا؛ با «آزمایش اتصال» بررسی کنید";
        if (existingBroken) note += " — جایگزین مقدار خرابِ قبلی شد";
        report.Add(label, ImportStatus.Imported, note);
        return true;
    }

    /// <summary>Never reveals the value: only its length and last two characters.</summary>
    internal static string Masked(string value)
    {
        var v = value.Trim();
        var tail = v.Length >= 8 ? v[^2..] : "";
        return $"*** {v.Length} کاراکتر{(tail.Length > 0 ? " …" + tail : "")}";
    }

    private static string Describe(Exception ex) => ex switch
    {
        System.Security.Cryptography.CryptographicException => "رمزگشایی DPAPI ناموفق بود (باید با همان حساب Windows اجرا شود)",
        PlatformNotSupportedException => "فقط روی Windows ممکن است",
        InvalidDataException or FormatException or System.Text.Json.JsonException => "قالب فایل شناخته نشد: " + ex.Message,
        IOException or UnauthorizedAccessException => "خواندن فایل ممکن نبود: " + ex.Message,
        _ => ex.GetType().Name + ": " + ex.Message
    };
}
