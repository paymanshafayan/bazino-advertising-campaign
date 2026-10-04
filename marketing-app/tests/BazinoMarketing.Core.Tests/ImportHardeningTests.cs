using System.Text;
using System.Text.Json;
using BazinoMarketing.Core.Secrets;
using BazinoMarketing.Core.Secrets.Legacy;
using BazinoMarketing.Core.Settings;
using BazinoMarketing.Core.Tools;
using Xunit;

namespace BazinoMarketing.Core.Tests;

/// <summary>Lessons from the first real run on the owner's PC: OEM-mangled vault text, unusable tokens, slow networks.</summary>
public class ImportHardeningTests : IDisposable
{
    private const string DpapiPrefixHex = "01000000d08c9ddf0115d1118c7a00c04fc297eb";
    private readonly string _appData = Path.Combine(Path.GetTempPath(), "bazino-hardening-tests-" + Guid.NewGuid().ToString("N"));

    public ImportHardeningTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Directory.CreateDirectory(_appData);
    }

    public void Dispose()
    {
        try { Directory.Delete(_appData, recursive: true); } catch { }
    }

    private static byte[] FakeProtect(byte[] plain) =>
        Convert.FromHexString(DpapiPrefixHex).Concat(plain.Select(b => (byte)(b ^ 0x5A))).ToArray();

    private static byte[] FakeUnprotect(byte[] blob)
    {
        var header = Convert.FromHexString(DpapiPrefixHex);
        if (blob.Length < header.Length || !blob.Take(header.Length).SequenceEqual(header))
            throw new System.Security.Cryptography.CryptographicException("bad blob");
        return blob.Skip(header.Length).Select(b => (byte)(b ^ 0x5A)).ToArray();
    }

    /// <summary>What Windows PowerShell 5.1 did to the old app's UTF-8 stdin: one CP437 character per byte.</summary>
    private static string Cp437Damage(string utf8Text) => Encoding.GetEncoding(437).GetString(Encoding.UTF8.GetBytes(utf8Text));

    private static string BuildVaultFile(object payload, bool damaged)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        if (damaged) json = Cp437Damage(json);
        var hex = Convert.ToHexString(FakeProtect(Encoding.Unicode.GetBytes(json))).ToLowerInvariant();
        return JsonSerializer.Serialize(new { v = 1, ciphertext = Convert.ToBase64String(Encoding.ASCII.GetBytes(hex)) });
    }

    [Fact]
    public void OemMojibake_Reverses_Cp437_Damage_And_Leaves_Real_Text_Alone()
    {
        const string original = "{\"secrets\":{\"cloudflareToken\":\"توکن نمونه\"},\"name\":\"سرویس ۱\"}";
        var damaged = Cp437Damage(original);
        Assert.NotEqual(original, damaged);
        Assert.Contains("╪", damaged); // the tell-tale box-drawing characters seen in the owner's log

        Assert.Equal(original, OemMojibake.Repair(damaged));
        Assert.Equal("plain-ascii_token", OemMojibake.Repair("plain-ascii_token"));
        Assert.Equal("café", OemMojibake.Repair("café")); // single accented char is not valid UTF-8 after re-encoding → untouched
        Assert.False(OemMojibake.TryRepair(original, out _)); // genuine Persian stays genuine
    }

    [Theory]
    [InlineData(SecretKind.CloudflareToken, "aB3dEfGhIjKlMnOpQrStUvWxYz0123456789_-Ab", false, true)]
    [InlineData(SecretKind.CloudflareToken, "توکن کلودفلر: aB3dEfGhIjKlMnOpQrStUvWxYz0123456789_-Ab (فقط تصویر)", true, true)]
    [InlineData(SecretKind.GitHubToken, "ghp_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", false, true)]
    [InlineData(SecretKind.GitHubToken, "token = ghp_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 ; keep", true, true)]
    [InlineData(SecretKind.CloudflareAccountId, "Account: 1234567890abcdef1234567890abcdef", true, true)]
    [InlineData(SecretKind.ZernioKey, "zk_live_abcdefghijklmnop", false, true)]
    public void SecretFormat_Accepts_Or_Extracts_Usable_Tokens(SecretKind kind, string raw, bool extracted, bool usable)
    {
        var check = SecretFormat.Check(kind, raw);
        Assert.Equal(usable, check.IsUsable);
        Assert.Equal(extracted, check.Extracted);
        Assert.True(SecretFormat.IsHeaderSafe(check.Value));
    }

    [Theory]
    [InlineData(SecretKind.CloudflareToken, "این فقط یک متن فارسی طولانی بدون هیچ توکنی است")]
    [InlineData(SecretKind.CloudflareToken, "")]
    [InlineData(SecretKind.CloudflareAccountId, "acc-77")]
    [InlineData(SecretKind.GitHubToken, "not a token at all")]
    public void SecretFormat_Rejects_Values_That_Can_Never_Work(SecretKind kind, string raw)
    {
        var check = SecretFormat.Check(kind, raw);
        Assert.False(check.IsUsable);
        Assert.Null(check.Value);
        Assert.False(SecretFormat.IsHeaderSafe("abc def"));
        Assert.False(SecretFormat.IsHeaderSafe("توکن"));
        Assert.True(SecretFormat.IsHeaderSafe("ghp_abc"));
    }

    [Fact]
    public void Importer_Repairs_Damaged_Vault_Rejects_Persian_Token_And_Drops_Broken_Previous_Import()
    {
        Directory.CreateDirectory(Path.Combine(_appData, "BazinoMarketingBrowser"));
        File.WriteAllText(Path.Combine(_appData, "BazinoMarketingBrowser", "vault.json"), BuildVaultFile(new
        {
            secrets = new
            {
                githubToken = "github_pat_" + new string('A', 82),
                zernioKey = "zk_live_abcdefghijklmnop",
                cloudflareToken = "یادداشت: این مقدار توکن نیست و فقط متن فارسی است" // what the owner's vault effectively contained
            },
            cloudflareAccountId = "1234567890abcdef1234567890abcdef",
            connectors = new object[]
            {
                new { id = "crm", name = "سامانهٔ مشتریان", kind = "api", url = "https://crm.example.com", auth = "header", headerName = "X-Api-Key", credential = "crm-key-value-1" }
            }
        }, damaged: true));

        var settings = new AppSettings();
        settings.LegacyImport.Attempted = true; // first (v1) import already happened …
        settings.LegacyImport.Version = 1;
        var secrets = new InMemorySecretStore();
        secrets.Set(SecretKeys.FluxApiKey, "╪¬╪▒╪¼╘à╪¬ ╪«╪▒╪º╪¿");           // … and stored the mangled token
        secrets.Set(SecretKeys.GitHubToken, "github_pat_" + new string('A', 82)); // valid values must survive untouched

        Assert.True(LegacyImporter.ShouldRunAutomatically(settings.LegacyImport));
        var report = new LegacyImporter(FakeUnprotect, _appData).Import(settings, secrets);

        Assert.False(secrets.Has(SecretKeys.FluxApiKey));                    // broken value removed, nothing bad stored
        Assert.Equal("github_pat_" + new string('A', 82), secrets.GetOrEmpty(SecretKeys.GitHubToken));
        Assert.Equal("zk_live_abcdefghijklmnop", secrets.GetOrEmpty(SecretKeys.ZernioApiKey));
        Assert.Equal("1234567890abcdef1234567890abcdef", settings.Flux.AccountId);
        Assert.Equal("none", settings.Flux.Provider);                         // no usable token → provider not forced
        Assert.Equal("سامانهٔ مشتریان", Assert.Single(settings.CustomCards).Name); // Persian name repaired
        Assert.Equal(LegacyImporter.CurrentVersion, settings.LegacyImport.Version);
        Assert.False(LegacyImporter.ShouldRunAutomatically(settings.LegacyImport));

        var summary = report.ToSummary();
        Assert.Contains("نامعتبر", summary);
        Assert.Contains("ترمیم شد", summary);
        Assert.DoesNotContain("یادداشت", summary); // never echo the stored value
        Assert.True(report.AnyFailed);
    }

    [Fact]
    public async Task WithRetry_Repeats_Once_After_A_Transient_Failure_Only()
    {
        var calls = 0;
        var r = await ToolCheckResult.WithRetryAsync(_ =>
        {
            calls++;
            return Task.FromResult(calls == 1
                ? new ToolCheckResult(ToolState.NetworkError, "زمان اتصال تمام شد", "github: timeout", "timeout", 15000)
                : new ToolCheckResult(ToolState.Connected, "متصل", "ok", null, 3000));
        }, CancellationToken.None, attempts: 2, pause: TimeSpan.Zero);
        Assert.Equal(2, calls);
        Assert.Equal(ToolState.Connected, r.State);
        Assert.Contains("attempt 2/2", r.Detail);
        Assert.Equal(18000, r.DurationMs);

        calls = 0;
        var fatal = await ToolCheckResult.WithRetryAsync(_ => { calls++; return Task.FromResult(new ToolCheckResult(ToolState.NeedsLogin, "401", "", "http:401")); },
            CancellationToken.None, attempts: 2, pause: TimeSpan.Zero);
        Assert.Equal(1, calls);
        Assert.Equal(ToolState.NeedsLogin, fatal.State);
    }

    [Fact]
    public void Kling_Child_Environment_Hands_The_Proxy_To_Node()
    {
        var custom = KlingCli.ChildEnvironment(new ProxySettings { Mode = "custom", Url = "http://127.0.0.1:10809/" });
        Assert.Equal("http://127.0.0.1:10809", custom["HTTPS_PROXY"]);
        Assert.Equal("http://127.0.0.1:10809", custom["HTTP_PROXY"]);
        Assert.Equal("1", custom["NODE_USE_ENV_PROXY"]);
        Assert.Equal("127.0.0.1,localhost", custom["NO_PROXY"]);

        var none = KlingCli.ChildEnvironment(new ProxySettings { Mode = "none" });
        Assert.Null(none["HTTPS_PROXY"]);
        Assert.False(none.ContainsKey("NODE_USE_ENV_PROXY"));

        Assert.True(KlingCli.NodeSupportsEnvProxy(new Version(24, 5, 0)));
        Assert.True(KlingCli.NodeSupportsEnvProxy(new Version(22, 21, 1)));
        Assert.False(KlingCli.NodeSupportsEnvProxy(new Version(22, 12, 0)));
        Assert.False(KlingCli.NodeSupportsEnvProxy(new Version(20, 19, 0)));
        Assert.False(KlingCli.NodeSupportsEnvProxy(null));

        Assert.True(KlingOutput.LooksLikeNetworkFailure("TypeError: fetch failed ... UND_ERR_CONNECT_TIMEOUT"));
        Assert.False(KlingOutput.LooksLikeNetworkFailure("{\"ok\":true}"));
        Assert.Contains("Node", KlingCli.ProxyHint("http://127.0.0.1:10809", new Version(22, 12, 0)));
    }
}
