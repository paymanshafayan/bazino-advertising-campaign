using System.Text;
using System.Text.Json;
using BazinoMarketing.Core.Secrets;
using BazinoMarketing.Core.Secrets.Legacy;
using BazinoMarketing.Core.Settings;
using Xunit;

namespace BazinoMarketing.Core.Tests;

public class LegacyImportTests : IDisposable
{
    private const string DpapiPrefixHex = "01000000d08c9ddf0115d1118c7a00c04fc297eb";
    private readonly string _appData = Path.Combine(Path.GetTempPath(), "bazino-legacy-tests-" + Guid.NewGuid().ToString("N"));

    public LegacyImportTests()
    {
        Directory.CreateDirectory(_appData);
    }

    public void Dispose()
    {
        try { Directory.Delete(_appData, recursive: true); } catch { }
    }

    /// <summary>Fake DPAPI: the "blob" is the fixed DPAPI header followed by the plaintext bytes XOR 0x5A.</summary>
    private static byte[] FakeProtect(byte[] plain)
    {
        var header = Convert.FromHexString(DpapiPrefixHex);
        var body = plain.Select(b => (byte)(b ^ 0x5A)).ToArray();
        return header.Concat(body).ToArray();
    }

    private static byte[] FakeUnprotect(byte[] blob)
    {
        var header = Convert.FromHexString(DpapiPrefixHex);
        if (blob.Length < header.Length || !blob.Take(header.Length).SequenceEqual(header))
            throw new System.Security.Cryptography.CryptographicException("bad blob");
        return blob.Skip(header.Length).Select(b => (byte)(b ^ 0x5A)).ToArray();
    }

    /// <summary>Mirrors PowerShell ConvertFrom-SecureString: hex of DPAPI(UTF-16LE text).</summary>
    private static string ToSecureStringHex(string text) =>
        Convert.ToHexString(FakeProtect(Encoding.Unicode.GetBytes(text))).ToLowerInvariant();

    private static string BuildVaultFile(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var hex = ToSecureStringHex(json);
        var ciphertext = Convert.ToBase64String(Encoding.ASCII.GetBytes(hex));
        return JsonSerializer.Serialize(new { v = 1, ciphertext });
    }

    [Fact]
    public void PowerShellSecureString_Decodes_Utf16_Plaintext()
    {
        var hex = ToSecureStringHex("ghp_exampleTOKEN1234567890abcdefghij");
        Assert.True(PowerShellSecureString.LooksLikeProtectedHex(hex));
        Assert.Equal("ghp_exampleTOKEN1234567890abcdefghij", PowerShellSecureString.Decode(hex, FakeUnprotect));
        Assert.False(PowerShellSecureString.LooksLikeProtectedHex("not-hex"));
        Assert.False(PowerShellSecureString.LooksLikeProtectedHex("abcd"));
    }

    [Fact]
    public void VaultReader_Parses_Secrets_Connectors_And_Flags()
    {
        var file = BuildVaultFile(new
        {
            secrets = new { githubToken = "ghp_FAKEaaaaaaaaaaaaaaaaaaaaaa", zernioKey = "zk_live_123456789", cloudflareToken = "cf_tok_987654321", relayPublicKey = "rp1" },
            cloudflareAccountId = "acc-42",
            autoApproveReads = true,
            autoApproveWrites = false,
            klingOAuth = new { tokens = new { access_token = "old" } },
            connectors = new object[]
            {
                new { id = "crm", name = "CRM سازمانی", kind = "api", url = "https://crm.example.com/api", auth = "bearer", agentAccess = "read", credential = "crm-secret-token" },
                new { id = "notes", name = "Notes MCP", kind = "mcp", url = "https://mcp.example.com/sse", auth = "oauth", oauth = new { tokens = new { access_token = "x" } } }
            }
        });

        var data = LegacyVaultReader.Read(file, FakeUnprotect);

        Assert.Equal("ghp_FAKEaaaaaaaaaaaaaaaaaaaaaa", data.GitHubToken);
        Assert.Equal("zk_live_123456789", data.ZernioKey);
        Assert.Equal("cf_tok_987654321", data.CloudflareToken);
        Assert.Equal("acc-42", data.CloudflareAccountId);
        Assert.True(data.AutoApproveReads);
        Assert.False(data.AutoApproveWrites);
        Assert.True(data.HasKlingOAuth);
        Assert.Single(data.RelayKeyNames);
        Assert.Equal(2, data.Connectors.Count);
        Assert.Equal("crm-secret-token", data.Connectors[0].Credential);
        Assert.True(data.Connectors[1].HasOAuthTokens);
    }

    [Fact]
    public void Importer_Moves_Everything_Into_Settings_And_SecretStore_Without_Leaking_Values()
    {
        Directory.CreateDirectory(Path.Combine(_appData, "BazinoMarketingBrowser"));
        File.WriteAllText(Path.Combine(_appData, "BazinoMarketingBrowser", "vault.json"), BuildVaultFile(new
        {
            secrets = new { githubToken = "ghp_FAKEbbbbbbbbbbbbbbbbbbbbbb", zernioKey = "zk_live_abcdefgh", cloudflareToken = "cf_tok_secretvalue" },
            cloudflareAccountId = "1234567890abcdef1234567890abcdef",
            connectors = new object[]
            {
                new { id = "crm", name = "CRM", kind = "api", url = "https://crm.example.com", auth = "header", headerName = "X-Api-Key", credential = "crm-key-value-1" }
            }
        }));
        Directory.CreateDirectory(Path.Combine(_appData, "BazinoBridge"));
        File.WriteAllText(Path.Combine(_appData, "BazinoBridge", "settings.json"),
            JsonSerializer.Serialize(new { token = ToSecureStringHex("ghp_FAKEcccccccccccccccccccccc") }));

        var settings = new AppSettings();
        var secrets = new InMemorySecretStore();
        var importer = new LegacyImporter(FakeUnprotect, _appData);

        var report = importer.Import(settings, secrets);

        Assert.True(report.AnyImported);
        Assert.False(report.AnyFailed);
        Assert.Equal("ghp_FAKEbbbbbbbbbbbbbbbbbbbbbb", secrets.GetOrEmpty(SecretKeys.GitHubToken)); // vault wins over bridge
        Assert.Equal("zk_live_abcdefgh", secrets.GetOrEmpty(SecretKeys.ZernioApiKey));
        Assert.Equal("cf_tok_secretvalue", secrets.GetOrEmpty(SecretKeys.FluxApiKey));
        Assert.Equal("1234567890abcdef1234567890abcdef", settings.Flux.AccountId);
        Assert.Equal("cloudflare", settings.Flux.Provider);
        var card = Assert.Single(settings.CustomCards);
        Assert.Equal("crm", card.Id);
        Assert.Equal("header", card.AuthType);
        Assert.Equal("X-Api-Key", card.HeaderName);
        Assert.Equal("crm-key-value-1", secrets.GetOrEmpty(SecretKeys.ForCustomCredential("crm")));
        Assert.True(settings.LegacyImport.Attempted);

        var summary = report.ToSummary();
        Assert.DoesNotContain("ghp_", summary);
        Assert.DoesNotContain("zk_live", summary);
        Assert.DoesNotContain("cf_tok", summary);
        Assert.DoesNotContain("crm-key-value", summary);
        Assert.Contains("توکن GitHub", summary);
        Assert.Contains("از قبل موجود بود", summary); // bridge token skipped because vault already provided one
    }

    [Fact]
    public void Importer_Is_NonDestructive_Unless_Overwrite_Requested()
    {
        Directory.CreateDirectory(Path.Combine(_appData, "BazinoMarketingBrowser"));
        File.WriteAllText(Path.Combine(_appData, "BazinoMarketingBrowser", "vault.json"),
            BuildVaultFile(new { secrets = new { githubToken = "ghp_FAKE_from_legacy_vault_0000" } }));

        var settings = new AppSettings();
        var secrets = new InMemorySecretStore();
        secrets.Set(SecretKeys.GitHubToken, "ghp_FAKE_current_value_1111111");
        var importer = new LegacyImporter(FakeUnprotect, _appData);

        var first = importer.Import(settings, secrets);
        Assert.Equal("ghp_FAKE_current_value_1111111", secrets.GetOrEmpty(SecretKeys.GitHubToken));
        Assert.Contains(first.Items, i => i.Label == "توکن GitHub" && i.Status == ImportStatus.AlreadyPresent);

        importer.Import(settings, secrets, overwriteExisting: true);
        Assert.Equal("ghp_FAKE_from_legacy_vault_0000", secrets.GetOrEmpty(SecretKeys.GitHubToken));
    }

    [Fact]
    public void Importer_Falls_Back_To_Bridge_Token_When_Vault_Missing()
    {
        Directory.CreateDirectory(Path.Combine(_appData, "BazinoBridge"));
        File.WriteAllText(Path.Combine(_appData, "BazinoBridge", "settings.json"),
            JsonSerializer.Serialize(new { token = ToSecureStringHex("ghp_FAKE_bridge_token_22222222") }));

        var settings = new AppSettings();
        var secrets = new InMemorySecretStore();
        var report = new LegacyImporter(FakeUnprotect, _appData).Import(settings, secrets);

        Assert.Equal("ghp_FAKE_bridge_token_22222222", secrets.GetOrEmpty(SecretKeys.GitHubToken));
        Assert.Contains(report.Items, i => i.Status == ImportStatus.NotFound); // vault.json
    }

    [Fact]
    public void Importer_Reports_Failure_On_Undecryptable_Vault_Without_Throwing()
    {
        Directory.CreateDirectory(Path.Combine(_appData, "BazinoMarketingBrowser"));
        var badHex = DpapiPrefixHex + "ff00ff00"; // valid-looking blob, but FakeUnprotect only accepts our XOR layout... make it corrupt:
        var vault = JsonSerializer.Serialize(new { v = 1, ciphertext = Convert.ToBase64String(Encoding.ASCII.GetBytes(badHex)) });
        File.WriteAllText(Path.Combine(_appData, "BazinoMarketingBrowser", "vault.json"), vault);

        var settings = new AppSettings();
        var secrets = new InMemorySecretStore();
        var report = new LegacyImporter(b => throw new System.Security.Cryptography.CryptographicException("nope"), _appData).Import(settings, secrets);

        Assert.True(report.AnyFailed);
        Assert.Empty(secrets.Keys);
        Assert.True(settings.LegacyImport.Attempted);
    }
}
