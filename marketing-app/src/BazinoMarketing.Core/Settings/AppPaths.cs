namespace BazinoMarketing.Core.Settings;

/// <summary>All on-disk locations of the app. Nothing is written anywhere else.</summary>
public sealed class AppPaths
{
    public string Root { get; }
    public string Logs => Path.Combine(Root, "logs");
    public string Mailbox => Path.Combine(Root, "mailbox");
    public string Downloads => Path.Combine(Root, "downloads");
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string SecretsFile => Path.Combine(Root, "secrets.json");

    public AppPaths(string root)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
    }

    public static AppPaths Default()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local)) local = Path.GetTempPath();
        return new AppPaths(Path.Combine(local, "BazinoMarketing"));
    }

    public static AppPaths Temporary(string tag)
    {
        return new AppPaths(Path.Combine(Path.GetTempPath(), "BazinoMarketing-" + tag));
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Logs);
    }
}
