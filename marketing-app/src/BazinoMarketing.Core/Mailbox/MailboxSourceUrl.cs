namespace BazinoMarketing.Core.Mailbox;

/// <summary>
/// Owner-provided branch address (2026-10-04): the owner pastes the public URL of a GitHub branch — for example
/// <c>https://github.com/owner/repo/tree/arena%2Fmy-branch</c> — and the app registers that repository + branch as a
/// command-mailbox source. Repository and branch names are free; nothing about them is assumed here.
/// </summary>
public static class MailboxSourceUrl
{
    /// <summary>Parses a branch URL (or a plain <c>owner/repo</c> shorthand) into repository + branch.</summary>
    public static bool TryParse(string? input, out string repository, out string branch, out string error)
    {
        repository = "";
        branch = "";
        error = "";
        var text = (input ?? "").Trim();
        if (text.Length == 0)
        {
            error = "آدرس خالی است.";
            return false;
        }

        // Drop a query string / fragment and any trailing slashes: they are not part of repo or branch names.
        var cut = text.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0) text = text[..cut];
        text = text.Trim().TrimEnd('/');
        if (text.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) text = text[..^4];

        // Strip scheme and host: github.com/owner/repo/tree/branch → owner/repo/tree/branch
        var scheme = text.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) text = text[(scheme + 3)..];
        var slash = text.IndexOf('/');
        if (slash <= 0)
        {
            error = "آدرس کامل نیست؛ نمونه: https://github.com/<مخزن>/<ریپو>/tree/<برنچ>";
            return false;
        }
        var host = text[..slash];
        var rest = text[(slash + 1)..];
        if (host.Contains("github.com", StringComparison.OrdinalIgnoreCase) || host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            text = rest;
        else if (host.Contains('.', StringComparison.Ordinal))
        {
            error = "فقط آدرس‌های گیت‌هاب پشتیبانی می‌شود.";
            return false;
        }

        var parts = text.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            error = "نام مخزن و ریپو از آدرس پیدا نشد.";
            return false;
        }

        repository = parts[0] + "/" + parts[1];
        if (!Tools.GitHubClient.IsValidRepository(repository))
        {
            error = "نام مخزن/ریپو معتبر نیست.";
            repository = "";
            return false;
        }

        // https://github.com/owner/repo            → default branch
        // https://github.com/owner/repo/tree/dev   → branch "dev"
        // …/tree/dev/sub/dir                       → branch "dev/sub/dir" (Git allows slashes in branch names)
        if (parts.Length >= 4 && parts[2].Equals("tree", StringComparison.OrdinalIgnoreCase))
        {
            var segments = parts.Skip(3).Select(Uri.UnescapeDataString).Where(s => s.Length > 0).ToArray();
            branch = string.Join("/", segments);
        }
        else
        {
            branch = "main";
        }

        branch = branch.Trim().Trim('/');
        if (branch.Length == 0)
        {
            error = "نام برنچ از آدرس پیدا نشد.";
            repository = "";
            return false;
        }
        return true;
    }

    /// <summary>A stable identity for one source (repository + branch + mailbox folder) — used by the ledgers and the UI.</summary>
    public static string KeyOf(string repository, string branch, string mailboxPath) =>
        $"{repository.Trim().ToLowerInvariant()}@{branch.Trim()}#{(mailboxPath ?? "").Trim().Trim('/')}";
}
