using System.Globalization;
using System.Text;

namespace BazinoMarketing.Core.Secrets.Legacy;

/// <summary>
/// Repairs text that the previous app stored through Windows PowerShell 5.1.
/// The old Node app piped UTF-8 JSON into <c>powershell.exe</c>, whose <c>[Console]::In</c> decoded it with the console's
/// OEM code page (437 on English Windows). Every non-ASCII character therefore became one OEM character per UTF-8 byte
/// (e.g. Persian "ت" = D8 AA → "╪¬"); on the way back PowerShell re-encoded with the same code page, so the old app
/// never noticed. The DPAPI blob keeps the mojibake, so we reverse it: chars → OEM bytes → UTF-8.
/// Pure ASCII text is returned unchanged, and a repair is only accepted when the bytes form strictly valid UTF-8.
/// </summary>
public static class OemMojibake
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static OemMojibake()
    {
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { }
    }

    /// <summary>Code pages to try, most likely first: US OEM, the machine's own OEM page, then common western/Arabic pages.</summary>
    public static IEnumerable<int> CandidateCodePages()
    {
        yield return 437;
        int oem = 0;
        try { oem = CultureInfo.CurrentCulture.TextInfo.OEMCodePage; } catch { }
        if (oem > 0 && oem != 437 && oem != 65001) yield return oem;
        yield return 850;
        yield return 1252;
        yield return 720;
        yield return 1256;
    }

    public static bool NeedsRepair(string? text) => !string.IsNullOrEmpty(text) && text.Any(c => c > 0x7F);

    public static string Repair(string text) => TryRepair(text, out var fixedText) ? fixedText : text;

    public static bool TryRepair(string text, out string repaired)
    {
        repaired = text;
        if (!NeedsRepair(text)) return false;
        foreach (var cp in CandidateCodePages().Distinct())
        {
            Encoding enc;
            try { enc = Encoding.GetEncoding(cp, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback); }
            catch { continue; }
            try
            {
                var bytes = enc.GetBytes(text);
                var candidate = StrictUtf8.GetString(bytes);
                // A genuine repair must remove mojibake, not merely re-interpret pure single-byte text.
                if (candidate == text || !bytes.Any(b => b > 0x7F)) continue;
                repaired = candidate;
                return true;
            }
            catch (EncoderFallbackException) { }
            catch (DecoderFallbackException) { }
            catch (ArgumentException) { }
        }
        return false;
    }
}
