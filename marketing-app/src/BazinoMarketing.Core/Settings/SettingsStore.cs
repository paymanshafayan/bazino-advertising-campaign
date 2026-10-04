using System.Text.Json;

namespace BazinoMarketing.Core.Settings;

/// <summary>
/// Loads/saves <c>settings.json</c> atomically. The file never contains secrets, so exporting it is safe.
/// </summary>
public sealed class SettingsStore
{
    public string RootPath { get; }
    public string FilePath => Path.Combine(RootPath, "settings.json");

    public SettingsStore(string rootPath)
    {
        RootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
    }

    public bool Exists => File.Exists(FilePath);

    public AppSettings Load()
    {
        if (!Exists) return new AppSettings();
        var json = File.ReadAllText(FilePath);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonUtil.Options) ?? new AppSettings();
        Normalize(settings);
        return settings;
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Normalize(settings);
        Directory.CreateDirectory(RootPath);
        var tmp = FilePath + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonUtil.Options));
        File.Move(tmp, FilePath, overwrite: true);
    }

    /// <summary>Settings are secret-free by construction; this is the same JSON the file holds.</summary>
    public string Export(AppSettings settings) => JsonSerializer.Serialize(settings, JsonUtil.Options);

    public static AppSettings Import(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonUtil.Options)
                       ?? throw new InvalidDataException("Settings JSON is empty");
        Normalize(settings);
        return settings;
    }

    private static void Normalize(AppSettings s)
    {
        s.GitHub ??= new GitHubSettings();
        s.Kling ??= new KlingSettings();
        s.Zernio ??= new ZernioSettings();
        s.Flux ??= new FluxSettings();
        s.Groq ??= new GroqSettings();
        s.Groq.Proxy ??= new ProxySettings();
        s.Groq.Permissions ??= new ToolPermissions();
        if (string.IsNullOrWhiteSpace(s.Groq.BaseUrl)) s.Groq.BaseUrl = "https://api.groq.com/openai/v1";
        if (string.IsNullOrWhiteSpace(s.Groq.Model)) s.Groq.Model = "whisper-large-v3";
        s.CustomCards ??= new List<CustomCard>();
        s.CustomCards.RemoveAll(c => string.Equals(c.Id, "groq", StringComparison.OrdinalIgnoreCase));
        s.Ui ??= new UiSettings();
        s.Browser ??= new BrowserSettings();
        s.Media ??= new MediaSettings();
        s.LegacyImport ??= new LegacyImportState();
        if (s.GitHub.PollSeconds < 3) s.GitHub.PollSeconds = 3;
        if (s.GitHub.PollSeconds > 300) s.GitHub.PollSeconds = 300;
        if (s.Kling.Region != "cn") s.Kling.Region = "global";
        foreach (var card in s.CustomCards)
        {
            card.Variables ??= new List<CustomVariable>();
            card.Proxy ??= new ProxySettings();
            card.Permissions ??= new ToolPermissions();
            if (string.IsNullOrWhiteSpace(card.Id)) card.Id = CustomCardIds.FromName(card.Name);
        }
    }
}

public static class CustomCardIds
{
    public static string FromName(string? name)
    {
        var slug = new string((name ?? "").ToLowerInvariant()
            .Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        if (slug.Length == 0 || !char.IsAsciiLetter(slug[0])) slug = "svc-" + Guid.NewGuid().ToString("N")[..8];
        return slug.Length > 40 ? slug[..40] : slug;
    }
}
