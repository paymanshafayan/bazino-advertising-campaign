namespace BazinoMarketing.Core.Settings;

/// <summary>
/// Settings for the in-app browser bridge. The app talks to the owner's own Chrome through the
/// Chrome DevTools Protocol on a local debugging port — no separate program, no cloud relay.
/// </summary>
public sealed class BrowserSettings
{
    /// <summary>Local debugging port Chrome is started with. 9222 is Chrome's default; 9334 is Bazino's.</summary>
    public int Port { get; set; } = 9334;

    /// <summary>Chrome profile folder. Empty = %LOCALAPPDATA%\BazinoMarketing\chrome-profile.</summary>
    public string UserDataDir { get; set; } = "";

    /// <summary>Explicit path to chrome.exe. Empty = the usual install locations.</summary>
    public string ChromePath { get; set; } = "";

    /// <summary>Open Chrome with the debugging port automatically when the app starts. Off by default.</summary>
    public bool AutoLaunch { get; set; }

    /// <summary>Largest number of characters a single page read may return.</summary>
    public int MaxReadChars { get; set; } = 20000;
}
