using BazinoMarketing.Core.Logging;
using BazinoMarketing.Core.Secrets;
using BazinoMarketing.Core.Settings;
using BazinoMarketing.Core.Tools;
using Xunit;

namespace BazinoMarketing.Core.Tests;

public class RedactorAndLogTests
{
    [Theory]
    [InlineData("token ghp_FAKEABCDEFGHIJKLMNOPQRSTUV used", "ghp_")]
    [InlineData("pat github_pat_FAKE11AAAAAAA0bbbbbbbbbbbbb", "github_pat_")]
    [InlineData("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload.sig", "eyJ")]
    [InlineData("api_key=sk-live-1234567890", "sk-live")]
    [InlineData("https://x.test/cb?code=abc123def&state=1", "abc123def")]
    [InlineData("https://user:pa55word@host/path", "pa55word")]
    [InlineData("Cookie: session=verysecretcookievalue", "verysecretcookievalue")]
    public void Redactor_Masks_Known_Shapes(string input, string mustNotRemain)
    {
        var output = Redactor.Empty.Redact(input);
        Assert.DoesNotContain(mustNotRemain, output);
        Assert.Contains(Redactor.Mask, output);
    }

    [Fact]
    public void Redactor_Masks_Stored_Secret_Values_Longest_First()
    {
        var store = new InMemorySecretStore();
        store.Set("a", "shortsecret");
        store.Set("b", "shortsecret-extended-version");
        var redactor = new Redactor(store.AllValues);

        var output = redactor.Redact("x shortsecret-extended-version y shortsecret z");
        Assert.Equal("x *** y *** z", output);
    }

    [Fact]
    public void Redactor_Keeps_Ordinary_Text()
    {
        var text = "اتصال به GitHub برقرار شد. repo=paymanshafayan/bazino-gamenet-portal branch=arena/01a0e1c3";
        Assert.Equal(text, Redactor.Empty.Redact(text));
    }

    [Fact]
    public void LogStore_Writes_Redacted_Events_And_Reads_Them_Back_Newest_First()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bazino-log-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new InMemorySecretStore();
            store.Set(SecretKeys.GitHubToken, "ghp_FAKEzzzzzzzzzzzzzzzzzzzzzz");
            var log = new JsonlLogStore(dir, new Redactor(store.AllValues));

            log.Append(LogLevel.Info, "github", "tool.test", "checking with ghp_FAKEzzzzzzzzzzzzzzzzzzzzzz");
            log.Append(LogLevel.Error, "kling", "tool.test", "failed", "Authorization: Bearer abcdefghijklmnop", errorCode: "needs_login");

            var raw = File.ReadAllText(Directory.GetFiles(dir, "*.jsonl").Single());
            Assert.DoesNotContain("ghp_zzzz", raw);
            Assert.DoesNotContain("abcdefghijklmnop", raw);

            var all = log.Read();
            Assert.Equal(2, all.Count);
            Assert.Equal("kling", all[0].Tool);

            var failures = log.Read(new LogQuery { OnlyFailures = true });
            Assert.Single(failures);
            Assert.Equal("needs_login", failures[0].ErrorCode);

            var github = log.Read(new LogQuery { Tool = "github" });
            Assert.Single(github);
            Assert.Contains("***", github[0].Message);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SettingsStore_Roundtrips_And_Normalizes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bazino-settings-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(dir);
            Assert.False(store.Exists);
            var s = store.Load();
            Assert.Equal("paymanshafayan/bazino-gamenet-portal", s.GitHub.Repository);
            Assert.Equal("global", s.Kling.Region);
            Assert.Equal("none", s.Flux.Provider);

            s.GitHub.PollSeconds = 1; // below minimum
            s.Kling.Region = "weird";
            s.Media.OutputFolder = @"D:\Bazino Media";
            s.Media.FfmpegPath = @"D:\Tools\ffmpeg.exe";
            s.CustomCards.Add(new CustomCard { Name = "My CRM!" });
            store.Save(s);

            var loaded = store.Load();
            Assert.Equal(3, loaded.GitHub.PollSeconds);
            Assert.Equal("global", loaded.Kling.Region);
            Assert.Equal("my-crm", loaded.CustomCards[0].Id);
            Assert.Equal(@"D:\Bazino Media", loaded.Media.OutputFolder);
            Assert.Equal(@"D:\Tools\ffmpeg.exe", loaded.Media.FfmpegPath);
            Assert.DoesNotContain("token", File.ReadAllText(store.FilePath), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void KlingOutput_Parses_Version_And_Json_Reply()
    {
        Assert.Equal(new Version(0, 2, 3), KlingOutput.ParseVersion("@klingai/cli-global 0.2.3\n"));
        Assert.Null(KlingOutput.ParseVersion("no version here"));

        var reply = KlingOutput.ParseReply("warning: something\n{\"ok\":true,\"status\":200,\"body\":{\"data\":{\"username\":\"payman\",\"models\":[{\"name\":\"kling-v2\"},\"kling-v1\"],\"account\":{\"availableRemainCredits\":1234}}}}");
        Assert.NotNull(reply);
        Assert.True(reply!.Ok);
        Assert.Equal(200, reply.Status);
        Assert.Equal("payman", reply.FindString("username"));
        Assert.Equal("1234", reply.FindString("availableRemainCredits"));
        Assert.Equal(new[] { "kling-v2", "kling-v1" }, reply.FindStringArray("models"));

        var failed = KlingOutput.ParseReply("{\"ok\":false,\"status\":401,\"body\":{\"message\":\"please run kling login\"}}");
        Assert.NotNull(failed);
        Assert.False(failed!.Ok);
        Assert.Equal(401, failed.Status);
        Assert.True(KlingOutput.LooksLikeNotLoggedIn(failed.Raw));
    }

    [Fact]
    public void ToolCheckResult_Maps_Http_Status_To_States()
    {
        Assert.Equal(ToolState.NeedsLogin, ToolCheckResult.FromStatus(System.Net.HttpStatusCode.Unauthorized, "x").State);
        Assert.Equal(ToolState.NetworkError, ToolCheckResult.FromStatus(System.Net.HttpStatusCode.BadGateway, "x").State);
        Assert.Equal(ToolState.Error, ToolCheckResult.FromStatus(System.Net.HttpStatusCode.NotFound, "x").State);
        Assert.True(GitHubClient.IsValidRepository("owner/repo"));
        Assert.False(GitHubClient.IsValidRepository("owner/repo/extra"));
    }

    [Fact]
    public void DpapiSecretStore_Roundtrip_On_Windows_Only()
    {
        if (!OperatingSystem.IsWindows()) return;
        var dir = Path.Combine(Path.GetTempPath(), "bazino-dpapi-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DpapiSecretStore(dir);
            Assert.True(store.IsAvailable);
            store.Set("github.token", "ghp_test_value_1234567890");
            var reloaded = new DpapiSecretStore(dir);
            Assert.True(reloaded.TryGet("github.token", out var v));
            Assert.Equal("ghp_test_value_1234567890", v);
            Assert.DoesNotContain("ghp_test_value", File.ReadAllText(reloaded.FilePath));
            reloaded.Remove("github.token");
            Assert.False(new DpapiSecretStore(dir).Has("github.token"));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
