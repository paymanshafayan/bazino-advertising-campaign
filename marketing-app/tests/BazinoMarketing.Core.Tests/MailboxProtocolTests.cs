using System.Text.Json;
using System.Text.Json.Nodes;
using BazinoMarketing.Core.Logging;
using BazinoMarketing.Core.Mailbox;
using BazinoMarketing.Core.Secrets;
using BazinoMarketing.Core.Settings;
using Xunit;

namespace BazinoMarketing.Core.Tests;

public class MailboxProtocolTests
{
    [Fact]
    public void IdentityFile_Roundtrip_And_Fingerprint_Check()
    {
        using var id = MailboxIdentity.Create();
        var file = IdentityFile.From(id, "test app");
        var parsed = IdentityFile.Parse(file.ToJson());
        Assert.Equal(id.Fingerprint, parsed.Fingerprint);
        Assert.Equal("test app", parsed.Label);
        Assert.Equal(id.PublicKeys, parsed.ToPeerKeys());

        var tampered = JsonNode.Parse(file.ToJson())!.AsObject();
        tampered["fingerprint"] = "0000:0000:0000:0000";
        var ex = Assert.Throws<MailboxSecurityException>(() => IdentityFile.Parse(tampered.ToJsonString()));
        Assert.Equal("identity", ex.Code);
    }

    [Fact]
    public void State_Sign_And_Verify_Detects_Tampering()
    {
        using var app = MailboxIdentity.Create();
        var state = new MailboxState { Listening = true, Status = "active", Session = "abc", Agent = "1111:2222:3333:4444", ApprovedUntil = DateTimeOffset.UtcNow.AddHours(8), App = "0.2.0" };
        state.Sign(app);
        var parsed = MailboxState.TryParse(state.ToJson());
        Assert.NotNull(parsed);
        Assert.True(parsed!.Verify(app.PublicKeys));

        parsed.Listening = false;
        Assert.False(parsed.Verify(app.PublicKeys));

        using var other = MailboxIdentity.Create();
        state.Listening = true;
        Assert.False(state.Verify(other.PublicKeys));
    }

    [Fact]
    public void CommandResponse_Serializes_Result_And_Error()
    {
        var ok = CommandResponse.Success("ping", "id1", new { pong = true, n = 3 }, 12, "0.2.0");
        var parsed = CommandResponse.FromJson(ok.ToJson());
        Assert.True(parsed.Ok);
        Assert.Equal("id1", parsed.InReplyTo);
        Assert.True(parsed.Result!["pong"]!.GetValue<bool>());
        Assert.Equal(3, parsed.Result["n"]!.GetValue<int>());

        var fail = CommandResponse.FromJson(CommandResponse.Failure("x", "id2", "denied", "no", 1, "0.2.0").ToJson());
        Assert.False(fail.Ok);
        Assert.Equal("denied", fail.Error!.Code);
        Assert.Null(fail.Result);
    }

    [Fact]
    public void JsonMerge_Merges_Nested_Objects_And_Reports_Changes()
    {
        var target = JsonNode.Parse("""{"github":{"pollSeconds":8,"proxy":{"mode":"system","url":""}},"kling":{"region":"global"}}""")!.AsObject();
        var changes = JsonNode.Parse("""{"github":{"proxy":{"mode":"custom","url":"http://127.0.0.1:10809"}},"kling":{"region":"global"}}""")!.AsObject();
        var changed = JsonMerge.Apply(target, changes);
        Assert.Equal(2, changed.Count);
        Assert.Contains(changed, c => c.StartsWith("github.proxy.mode:", StringComparison.Ordinal));
        Assert.Equal("custom", target["github"]!["proxy"]!["mode"]!.GetValue<string>());
        Assert.Equal(8, target["github"]!["pollSeconds"]!.GetValue<int>());
    }

    [Fact]
    public void Commands_Risk_Table_Is_Informational_Only()
    {
        // Risk labels are kept for the log; nothing is gated on them (owner's decision: no confirmations).
        Assert.Equal(Risk.Read, Commands.RequiredRisk(Commands.Ping));
        Assert.Equal(Risk.Write, Commands.RequiredRisk(Commands.SettingsSet));
        Assert.Equal(Risk.Write, Commands.RequiredRisk(Commands.AppUpdate));
        Assert.Equal(Risk.Write, Commands.RequiredRisk(Commands.ZernioCommentAutomationCreate));
        Assert.Equal(Risk.Os, Commands.RequiredRisk(Commands.OsRun));
        Assert.True(Commands.RiskRank(Risk.Os) > Commands.RiskRank(Risk.Write));
        Assert.Equal("app.update", Commands.AppUpdate);
        Assert.Equal("app.restart", Commands.AppRestart);
    }

    [Fact]
    public async Task DirectCommentAutomation_IsDisabledSoOnlyApprovedQueueCanCreateDmFlows()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "bz-mb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var (executor, _, _, _) = MakeExecutor(tmp);
            var error = await Assert.ThrowsAsync<CommandException>(() => executor.ExecuteAsync(
                Ctx(Commands.ZernioCommentAutomationCreate, """{"kind":"affiliate","mediaId":"17840000000000000"}"""),
                CancellationToken.None));
            Assert.Equal("approval_required", error.Code);
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public void Sha256Sums_Parser_Accepts_Ci_Format()
    {
        var text = "\uFEFFd2a8f0c4e6b1a3c5d7e9f1a3b5c7d9e1f3a5b7c9d1e3f5a7b9c1d3e5f7a9b1c3  BazinoMarketing.exe\r\n" +
                   "0000000000000000000000000000000000000000000000000000000000000000 *SHA256SUMS-other.txt\n";
        Assert.True(Update.AppUpdater.TryParseSums(text, "BazinoMarketing.exe", out var hash));
        Assert.Equal("d2a8f0c4e6b1a3c5d7e9f1a3b5c7d9e1f3a5b7c9d1e3f5a7b9c1d3e5f7a9b1c3", hash);
        Assert.True(Update.AppUpdater.TryParseSums(text, "sha256sums-other.txt", out _));
        Assert.False(Update.AppUpdater.TryParseSums(text, "missing.exe", out _));
        Assert.False(Update.AppUpdater.TryParseSums("garbage\nzz  BazinoMarketing.exe", "BazinoMarketing.exe", out _));
    }

    private static (CommandExecutor executor, List<string> restarts, AppSettings settings, InMemorySecretStore secrets) MakeExecutor(string tmp)
    {
        var settings = new AppSettings();
        var secrets = new InMemorySecretStore();
        secrets.Set(SecretKeys.GitHubToken, "ghp_abcdefghijklmnopqrstuvwxyz0123456789");
        secrets.Set(SecretKeys.MailboxIdentity, "{\"x\":\"private\",\"s\":\"private\"}");
        var redactor = new Redactor(secrets.AllValues);
        var log = new JsonlLogStore(Path.Combine(tmp, "logs"), redactor);
        var restarts = new List<string>();
        AppSettings? applied = null;
        var executor = new CommandExecutor(new CommandExecutorOptions
        {
            GetSettings = () => applied ?? settings,
            Secrets = secrets,
            Log = log,
            Redactor = redactor,
            AppVersion = "0.3.0-test",
            DataFolder = tmp,
            DownloadFolder = Path.Combine(tmp, "downloads"),
            ApplySettings = s => { applied = s; return Task.FromResult<string?>(null); },
            Restart = delay => { restarts.Add($"restart:{(int)delay.TotalSeconds}"); return Task.FromResult<string?>(null); },
            RestartWith = (file, delay) => { restarts.Add($"update:{file}:{(int)delay.TotalSeconds}"); return Task.FromResult<string?>(null); }
        });
        return (executor, restarts, settings, secrets);
    }

    private static CommandContext Ctx(string cmd, string argsJson = "{}", string risk = "read") =>
        new(cmd, JsonDocument.Parse(argsJson).RootElement, risk, "test", Guid.NewGuid().ToString("N"), "s1", "aaaa:bbbb:cccc:dddd");

    [Fact]
    public async Task Executor_Ping_And_SecretStatus_Never_Leak_Values()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "bz-mb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var (executor, _, _, _) = MakeExecutor(tmp);
            var pong = JsonSerializer.Serialize(await executor.ExecuteAsync(Ctx(Commands.Ping), CancellationToken.None));
            Assert.Contains("\"pong\":true", pong);
            Assert.Contains("0.3.0-test", pong);

            var status = JsonSerializer.Serialize(await executor.ExecuteAsync(Ctx(Commands.SecretStatus), CancellationToken.None));
            Assert.Contains("github.token", status);
            Assert.DoesNotContain("ghp_", status);
            Assert.DoesNotContain("mailbox.identity", status);
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public async Task Executor_Runs_Everything_Without_Asking_But_Still_Validates_Arguments()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "bz-mb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var (executor, restarts, _, _) = MakeExecutor(tmp);
            // Risk labels no longer gate anything: an os.run labelled "read" is not rejected for its label (it fails later only if the file is missing).
            var notFound = await Assert.ThrowsAsync<CommandException>(() => executor.ExecuteAsync(Ctx(Commands.OsRun, """{"file":"definitely-not-a-real-binary-xyz"}""", "read"), CancellationToken.None));
            Assert.Equal("not_found", notFound.Code);
            var unknown = await Assert.ThrowsAsync<CommandException>(() => executor.ExecuteAsync(Ctx("format.disk"), CancellationToken.None));
            Assert.Equal("unknown_command", unknown.Code);
            var noArgs = await Assert.ThrowsAsync<CommandException>(() => executor.ExecuteAsync(Ctx(Commands.KlingCli, """{"args":[]}"""), CancellationToken.None));
            Assert.Equal("bad_args", noArgs.Code);
            var multiline = await Assert.ThrowsAsync<CommandException>(() => executor.ExecuteAsync(Ctx(Commands.KlingCli, """{"args":["who_am_i\nx"]}"""), CancellationToken.None));
            Assert.Equal("bad_args", multiline.Code);

            // app.restart goes straight to the host hook.
            var restart = JsonSerializer.Serialize(await executor.ExecuteAsync(Ctx(Commands.AppRestart, """{"delaySec":7}""", "os"), CancellationToken.None));
            Assert.Contains("\"restarting\":true", restart);
            Assert.Equal(new[] { "restart:7" }, restarts);
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public async Task Executor_SettingsSet_Applies_Immediately()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "bz-mb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var (executor, _, _, _) = MakeExecutor(tmp);
            var changes = """{"changes":{"kling":{"proxy":{"mode":"custom","url":"http://127.0.0.1:10809"}}}}""";
            var result = JsonSerializer.Serialize(await executor.ExecuteAsync(Ctx(Commands.SettingsSet, changes, "write"), CancellationToken.None));
            Assert.Contains("\"applied\":true", result);
            Assert.Contains("kling.proxy.mode", result);
            var after = JsonSerializer.Serialize(await executor.ExecuteAsync(Ctx(Commands.SettingsGet), CancellationToken.None));
            Assert.Contains("http://127.0.0.1:10809", after);

            // Same change again → nothing to do.
            var again = JsonSerializer.Serialize(await executor.ExecuteAsync(Ctx(Commands.SettingsSet, changes, "write"), CancellationToken.None));
            Assert.Contains("no_change", again);

            // Schema violations are still refused.
            var bad = await Assert.ThrowsAsync<CommandException>(() => executor.ExecuteAsync(Ctx(Commands.SettingsSet, """{"changes":{"gitHub":{"repository":"not-a-repo"}}}""", "write"), CancellationToken.None));
            Assert.Equal("bad_args", bad.Code);
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public void ResolveExecutable_Rejects_Relative_Paths_With_Separators()
    {
        Assert.Null(CommandExecutor.ResolveExecutable("../evil"));
        Assert.Null(CommandExecutor.ResolveExecutable("sub\\evil.exe"));
        Assert.Null(CommandExecutor.ResolveExecutable("definitely-not-a-real-binary-xyz"));
    }

    [Fact]
    public void Tail_Keeps_The_End()
    {
        var s = new string('a', 100) + "END";
        var t = CommandExecutor.Tail(s, 10);
        Assert.EndsWith("END", t);
        Assert.Contains("chars cut", t);
        Assert.Equal("short", CommandExecutor.Tail("short", 10));
    }
}
