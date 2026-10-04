using BazinoMarketing.Core.Browser;
using BazinoMarketing.Core.Settings;
using Xunit;

namespace BazinoMarketing.Core.Tests;

/// <summary>
/// The browser bridge's rules, tested without Chrome: any ordinary web address opens, sensitive browser storage stays blocked,
/// page text is bounded, and Chrome's own JSON is read safely.
/// </summary>
public class BrowserGuardTests
{
    [Theory]
    [InlineData("https://www.instagram.com/bazinopro/", true)]
    [InlineData("https://instagram.com", true)]
    [InlineData("https://m.instagram.com/reels/x", true)]
    [InlineData("https://bazino.pro/page", true)]
    [InlineData("https://facebook.com/bazinopro", true)]
    [InlineData("https://evil-instagram.com.attacker.net/", true)]
    [InlineData("https://notinstagram.com/", true)]
    [InlineData("https://tiktok.com/@x", true)]
    [InlineData("https://example.org/anything?q=1", true)]
    [InlineData("http://127.0.0.1:9334/status", true)]
    [InlineData("ftp://example.org/file", false)]
    [InlineData("file:///C:/Windows/System32/config", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,hi", false)]
    [InlineData("about:blank", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void CheckUrl_Allows_All_Http_And_Https_Hosts(string? url, bool allowed)
    {
        var refusal = BrowserGuard.CheckUrl(url);
        if (allowed) Assert.Null(refusal);
        else Assert.NotNull(refusal);
    }

    [Theory]
    [InlineData("document.title", true)]
    [InlineData("document.querySelectorAll('a').length", true)]
    [InlineData("document.cookie", false)]
    [InlineData("window.localStorage.getItem('x')", false)]
    [InlineData("sessionStorage.clear()", false)]
    [InlineData("document.querySelector(\"input[type=password]\").value", false)]
    [InlineData("document.querySelector(\"input[type='password']\").value", false)]
    [InlineData("indexedDB.open('x')", false)]
    [InlineData("navigator.credentials.get()", false)]
    [InlineData("   ", false)]
    public void CheckExpression_Blocks_Stored_Credentials(string expression, bool allowed)
    {
        var refusal = BrowserGuard.CheckExpression(expression);
        if (allowed) Assert.Null(refusal);
        else Assert.NotNull(refusal);
    }

    [Fact]
    public void CheckExpression_Refuses_An_Overlong_Expression() =>
        Assert.NotNull(BrowserGuard.CheckExpression(new string('a', 12001)));

    [Theory]
    [InlineData(1000, 1000, false)]
    [InlineData(500, 200, true)]
    [InlineData(200, 200, false)]
    public void Trim_Cuts_Long_Pages_And_Says_So(int length, int max, bool truncated)
    {
        var (text, total, wasTruncated) = BrowserGuard.Trim(new string('x', length), max);
        Assert.Equal(length, total);
        Assert.Equal(truncated, wasTruncated);
        Assert.True(text.Length <= Math.Max(max, 200));
    }

    [Fact]
    public void Trim_Clamps_A_Ridiculous_Max() =>
        Assert.Equal(200, BrowserGuard.Trim(new string('y', 500), 1).Text.Length);

    [Fact]
    public void Trim_Handles_Empty_Pages() =>
        Assert.Equal((0, false), (BrowserGuard.Trim("", 100).Total, BrowserGuard.Trim("", 100).Truncated));

    [Fact]
    public void ParseTargets_Reads_Chromes_List_And_Ignores_Non_Pages()
    {
        var json = """
        [
          { "id": "A1", "title": "Instagram", "url": "https://www.instagram.com/", "type": "page",
            "webSocketDebuggerUrl": "ws://127.0.0.1:9334/devtools/page/A1" },
          { "id": "S1", "title": "worker", "url": "https://x/", "type": "service_worker" },
          { "id": "E1", "title": "ext", "url": "chrome-extension://x", "type": "background_page" },
          { "id": "P2", "title": "No socket", "url": "https://bazino.pro/", "type": "page" }
        ]
        """;
        var targets = BrowserGuard.ParseTargets(json);
        Assert.Equal(2, targets.Count);
        Assert.Equal("A1", targets[0].Id);
        Assert.Equal("Instagram", targets[0].Title);
        Assert.Equal("ws://127.0.0.1:9334/devtools/page/A1", targets[0].WebSocketDebuggerUrl);
        Assert.Equal("P2", targets[1].Id);
        Assert.Null(targets[1].WebSocketDebuggerUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"id\":\"A1\"}")]
    [InlineData("[{\"type\":\"page\"}]")]
    public void ParseTargets_Never_Throws_On_Unexpected_Shapes(string json) =>
        Assert.Empty(BrowserGuard.ParseTargets(json));

    [Fact]
    public void ParseBrowserWebSocketUrl_Reads_The_Version_Reply()
    {
        var url = BrowserGuard.ParseBrowserWebSocketUrl("""
            { "Browser": "Chrome/140.0.0.0", "webSocketDebuggerUrl": "ws://127.0.0.1:9334/devtools/browser/abc" }
            """);
        Assert.Equal("ws://127.0.0.1:9334/devtools/browser/abc", url);
        Assert.Null(BrowserGuard.ParseBrowserWebSocketUrl("garbage"));
    }
}

public class BrowserSettingsTests
{
    [Fact]
    public void Defaults_Are_Sane()
    {
        var s = new BrowserSettings();
        Assert.InRange(s.Port, 1024, 65535);
        Assert.False(s.AutoLaunch);
        Assert.InRange(s.MaxReadChars, 200, 200000);
        // An empty profile folder means "the app's own folder under %LOCALAPPDATA%".
        Assert.Equal("", s.UserDataDir);
        Assert.Equal("", s.ChromePath);
    }

    [Fact]
    public void Settings_Round_Trip_Through_Json()
    {
        var s = new BrowserSettings { Port = 9444, ChromePath = @"C:\chrome.exe" };
        var copy = JsonUtil.Clone(s);
        Assert.Equal(9444, copy.Port);
        Assert.Equal(@"C:\chrome.exe", copy.ChromePath);
    }

    [Fact]
    public void Legacy_Host_List_Is_Ignored()
    {
        var copy = System.Text.Json.JsonSerializer.Deserialize<BrowserSettings>(
            "{\"port\":9444,\"allowedHosts\":[\"instagram.com\"]}", JsonUtil.Options);
        Assert.NotNull(copy);
        Assert.Equal(9444, copy.Port);
    }
}
