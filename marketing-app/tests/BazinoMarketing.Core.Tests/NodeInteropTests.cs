using System.Diagnostics;
using System.Text;
using BazinoMarketing.Core.Mailbox;
using Xunit;
using Xunit.Abstractions;

namespace BazinoMarketing.Core.Tests;

/// <summary>
/// The agent talks to the app with <c>marketing-app/agent/mailbox.cjs</c> (Node, no dependencies). These tests prove both
/// implementations agree byte-for-byte: Node seals → C# opens, C# seals → Node opens, C# signs state → Node verifies.
/// They pass vacuously when <c>node</c> is not installed (both CI runners have it).
/// </summary>
public class NodeInteropTests
{
    private readonly ITestOutputHelper _out;
    public NodeInteropTests(ITestOutputHelper output) => _out = output;

    private static string? FindScript()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "agent", "mailbox.cjs");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string? FindNode()
    {
        var names = OperatingSystem.IsWindows() ? new[] { "node.exe" } : new[] { "node" };
        foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var n in names)
            {
                var p = Path.Combine(d.Trim('"'), n);
                if (File.Exists(p)) return p;
            }
        return null;
    }

    private static (int Code, string Out, string Err) Run(string node, string script, string workDir, string stdin, params string[] args)
    {
        var psi = new ProcessStartInfo(node)
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = workDir, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false)
        };
        psi.ArgumentList.Add(script);
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["BAZINO_AGENT_HOME"] = Path.Combine(workDir, "home");
        using var p = Process.Start(psi)!;
        p.StandardInput.Write(stdin);
        p.StandardInput.Close();
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(60_000);
        return (p.ExitCode, stdout, stderr);
    }

    [Fact]
    public void Node_And_CSharp_Agree_On_Envelopes_And_State()
    {
        var node = FindNode();
        var script = FindScript();
        if (node is null || script is null)
        {
            _out.WriteLine($"skipped: node={node ?? "-"} script={script ?? "-"}");
            return;
        }

        var tmp = Path.Combine(Path.GetTempPath(), "bz-node-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            using var app = MailboxIdentity.Create();
            var appIdentityPath = Path.Combine(tmp, "app-identity.json");
            File.WriteAllText(appIdentityPath, IdentityFile.From(app, "app under test").ToJson());

            var agentPrivate = Path.Combine(tmp, "agent-private.json");
            var keygen = Run(node, script, tmp, "", "keygen", "--out", agentPrivate);
            Assert.True(keygen.Code == 0, "keygen: " + keygen.Err);
            var agentIdentityPath = Path.Combine(tmp, "agent-identity.json");
            var identity = Run(node, script, tmp, "", "identity", "--identity", agentPrivate, "--label", "agent under test");
            Assert.True(identity.Code == 0, "identity: " + identity.Err);
            File.WriteAllText(agentIdentityPath, identity.Out);
            var agent = IdentityFile.Parse(identity.Out);
            Assert.Equal(keygen.Out.Trim(), agent.Fingerprint);

            // 1) Node seals a command → C# opens it.
            const string command = "{\"cmd\":\"ping\",\"args\":{},\"note\":\"سلام از نود\"}";
            var sealed1 = Run(node, script, tmp, command, "seal", "--to", appIdentityPath, "--identity", agentPrivate, "--session", "s-node", "--seq", "1", "--risk", "read");
            Assert.True(sealed1.Code == 0, "seal: " + sealed1.Err);
            var env = Envelope.FromJson(sealed1.Out);
            Assert.Equal(agent.Fingerprint, env.From);
            var guard = new ReplayGuard();
            var opened = CryptoBox.Open(app, agent.ToPeerKeys(), env, guard: guard);
            Assert.Equal(command, opened);
            Assert.Throws<MailboxSecurityException>(() => CryptoBox.Open(app, agent.ToPeerKeys(), env, guard: guard)); // replay

            // 2) C# seals a reply (timestamp with trailing zeros → STJ trims the fraction) → Node opens it.
            var now = new DateTimeOffset(DateTimeOffset.UtcNow.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, TimeSpan.Zero).AddMilliseconds(120);
            const string reply = "{\"ok\":true,\"cmd\":\"ping\",\"result\":{\"pong\":true,\"text\":\"پاسخ از سی‌شارپ\"}}";
            var replyEnv = CryptoBox.Seal(app, agent.ToPeerKeys(), reply, "reply", "read", "s-node", 1, TimeSpan.FromMinutes(10), now);
            var replyJson = replyEnv.ToJson();
            Assert.Contains(".12+00:00\"", replyJson); // the fraction really is trimmed in JSON (the Node side must pad it back)
            var opened2 = Run(node, script, tmp, replyJson, "open", "--from", appIdentityPath, "--identity", agentPrivate);
            Assert.True(opened2.Code == 0, "open: " + opened2.Err);
            Assert.Equal(reply, opened2.Out);

            // 3) Tampering is caught on the Node side too.
            var tampered = replyJson.Replace("\"risk\":\"read\"", "\"risk\":\"spend\"");
            var opened3 = Run(node, script, tmp, tampered, "open", "--from", appIdentityPath, "--identity", agentPrivate);
            Assert.NotEqual(0, opened3.Code);

            // 4) Signed heartbeat verified by Node.
            var state = new MailboxState { Listening = true, Status = "active", Session = "abc", Agent = agent.Fingerprint, ApprovedUntil = now.AddHours(8), App = "0.2.0", UpdatedAt = now };
            state.Sign(app);
            var verified = Run(node, script, tmp, state.ToJson(), "verifystate", "--from", appIdentityPath);
            Assert.True(verified.Code == 0, "verifystate: " + verified.Err);
            Assert.Contains("\"verified\":true", verified.Out);
            state.Listening = false;
            var verified2 = Run(node, script, tmp, state.ToJson(), "verifystate", "--from", appIdentityPath);
            Assert.Contains("\"verified\":false", verified2.Out);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
        }
    }
}
