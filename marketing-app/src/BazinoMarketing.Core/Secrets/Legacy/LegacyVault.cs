using System.Text;
using System.Text.Json;

namespace BazinoMarketing.Core.Secrets.Legacy;

/// <summary>
/// Decoder for the output of Windows PowerShell <c>ConvertFrom-SecureString</c> (no -Key):
/// a hex string of the DPAPI (CurrentUser, no entropy) blob over the UTF-16LE characters of the plaintext.
/// </summary>
public static class PowerShellSecureString
{
    // Every DPAPI blob starts with version 1 followed by the provider GUID {df9d8cd0-1501-11d1-8c7a-00c04fc297eb}.
    private const string DpapiPrefix = "01000000d08c9ddf0115d1118c7a00c04fc297eb";

    public static bool LooksLikeProtectedHex(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.Length < DpapiPrefix.Length || s.Length % 2 != 0) return false;
        if (!s.StartsWith(DpapiPrefix, StringComparison.OrdinalIgnoreCase)) return false;
        return s.All(Uri.IsHexDigit);
    }

    public static byte[] HexToBytes(string hex)
    {
        var s = hex.Trim();
        if (s.Length % 2 != 0) throw new FormatException("Hex string has odd length");
        return Convert.FromHexString(s);
    }

    /// <param name="unprotect">DPAPI CurrentUser unprotect (raw, no entropy). Injected so tests can run on any OS.</param>
    public static string Decode(string hex, Func<byte[], byte[]> unprotect)
    {
        ArgumentNullException.ThrowIfNull(unprotect);
        var plain = unprotect(HexToBytes(hex));
        return Encoding.Unicode.GetString(plain).TrimEnd('\0');
    }
}

public sealed class LegacyConnector
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "api";      // api | mcp
    public string Url { get; set; } = "";
    public string Auth { get; set; } = "none";     // none | bearer | header | oauth
    public string HeaderName { get; set; } = "";
    public string AgentAccess { get; set; } = "none";
    public string Credential { get; set; } = "";
    public bool HasOAuthTokens { get; set; }
}

public sealed class LegacyVaultData
{
    public Dictionary<string, string> Secrets { get; } = new(StringComparer.Ordinal);
    public string CloudflareAccountId { get; set; } = "";
    public bool AutoApproveReads { get; set; }
    public bool AutoApproveWrites { get; set; }
    public List<LegacyConnector> Connectors { get; } = new();
    public bool HasKlingOAuth { get; set; }
    /// <summary>True when the stored JSON carried OEM mojibake that was reversed (see <see cref="OemMojibake"/>).</summary>
    public bool RepairedEncoding { get; set; }

    public string GitHubToken => Secrets.GetValueOrDefault("githubToken", "");
    public string ZernioKey => Secrets.GetValueOrDefault("zernioKey", "");
    public string CloudflareToken => Secrets.GetValueOrDefault("cloudflareToken", "");
    public IEnumerable<string> RelayKeyNames => Secrets.Keys.Where(k => k.StartsWith("relay", StringComparison.Ordinal));
}

/// <summary>
/// Reads the previous app's <c>%APPDATA%\BazinoMarketingBrowser\vault.json</c>:
/// <c>{ "v": 1, "ciphertext": base64( utf8( hex-DPAPI( utf16( json ) ) ) ) }</c>.
/// </summary>
public static class LegacyVaultReader
{
    public static LegacyVaultData Read(string vaultFileJson, Func<byte[], byte[]> unprotect)
    {
        ArgumentNullException.ThrowIfNull(unprotect);
        using var doc = JsonDocument.Parse(vaultFileJson);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Number || v.GetInt32() != 1 ||
            !root.TryGetProperty("ciphertext", out var c) || c.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Unknown legacy vault format (expected v=1 with ciphertext)");

        var wrapped = Convert.FromBase64String(c.GetString() ?? "");
        var hex = Encoding.ASCII.GetString(wrapped).Trim();
        string plain;
        if (PowerShellSecureString.LooksLikeProtectedHex(hex))
        {
            plain = PowerShellSecureString.Decode(hex, unprotect);
        }
        else
        {
            // Defensive fallback: the bytes may already be a raw DPAPI blob.
            plain = Encoding.Unicode.GetString(unprotect(wrapped)).TrimEnd('\0');
        }
        // The old app piped this JSON through PowerShell's OEM console encoding; undo that byte-per-char damage first.
        var data = Parse(OemMojibake.Repair(plain));
        data.RepairedEncoding = OemMojibake.NeedsRepair(plain) && OemMojibake.TryRepair(plain, out _);
        return data;
    }

    public static LegacyVaultData Parse(string plainJson)
    {
        var data = new LegacyVaultData();
        using var doc = JsonDocument.Parse(plainJson);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Legacy vault payload is not an object");

        if (root.TryGetProperty("secrets", out var secrets) && secrets.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in secrets.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.Value.GetString()))
                    data.Secrets[p.Name] = p.Value.GetString()!.Trim();
        }
        if (root.TryGetProperty("cloudflareAccountId", out var acc) && acc.ValueKind == JsonValueKind.String)
            data.CloudflareAccountId = acc.GetString()?.Trim() ?? "";
        if (root.TryGetProperty("autoApproveReads", out var ar) && ar.ValueKind is JsonValueKind.True or JsonValueKind.False)
            data.AutoApproveReads = ar.GetBoolean();
        if (root.TryGetProperty("autoApproveWrites", out var aw) && aw.ValueKind is JsonValueKind.True or JsonValueKind.False)
            data.AutoApproveWrites = aw.GetBoolean();
        if (root.TryGetProperty("klingOAuth", out var ko) && ko.ValueKind == JsonValueKind.Object)
            data.HasKlingOAuth = ko.TryGetProperty("tokens", out var tok) && tok.ValueKind == JsonValueKind.Object;

        if (root.TryGetProperty("connectors", out var connectors) && connectors.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in connectors.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var con = new LegacyConnector
                {
                    Id = Str(item, "id"),
                    Name = Str(item, "name"),
                    Kind = Str(item, "kind", "api"),
                    Url = Str(item, "url"),
                    Auth = Str(item, "auth", "none"),
                    HeaderName = Str(item, "headerName"),
                    AgentAccess = Str(item, "agentAccess", "none"),
                    Credential = Str(item, "credential"),
                    HasOAuthTokens = item.TryGetProperty("oauth", out var oauth) && oauth.ValueKind == JsonValueKind.Object &&
                                     oauth.TryGetProperty("tokens", out var t) && t.ValueKind == JsonValueKind.Object
                };
                if (!string.IsNullOrWhiteSpace(con.Id)) data.Connectors.Add(con);
            }
        }
        return data;
    }

    private static string Str(JsonElement obj, string name, string fallback = "")
    {
        if (obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
            return el.GetString()?.Trim() ?? fallback;
        return fallback;
    }
}

/// <summary>Reads <c>%APPDATA%\BazinoBridge\settings.json</c> = <c>{ "token": "&lt;ConvertFrom-SecureString hex&gt;" }</c>.</summary>
public static class LegacyBridgeSettingsReader
{
    public static string? ReadToken(string json, Func<byte[], byte[]> unprotect)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        if (!doc.RootElement.TryGetProperty("token", out var t) || t.ValueKind != JsonValueKind.String) return null;
        var hex = t.GetString() ?? "";
        if (!PowerShellSecureString.LooksLikeProtectedHex(hex)) return null;
        var token = PowerShellSecureString.Decode(hex, unprotect).Trim();
        return string.IsNullOrEmpty(token) ? null : token;
    }
}
