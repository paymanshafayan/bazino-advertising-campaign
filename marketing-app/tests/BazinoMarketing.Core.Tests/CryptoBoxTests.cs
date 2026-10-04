using BazinoMarketing.Core.Mailbox;
using Xunit;

namespace BazinoMarketing.Core.Tests;

public class CryptoBoxTests
{
    [Fact]
    public void Roundtrip_Seal_Then_Open_Returns_Plaintext()
    {
        using var app = MailboxIdentity.Create();
        using var agent = MailboxIdentity.Create();
        const string message = "{\"op\":\"diag\",\"text\":\"سلام — hello\"}";

        var env = CryptoBox.Seal(agent, app.PublicKeys, message, "diag", "read", "s1", 1);
        var json = env.ToJson();
        var parsed = Envelope.FromJson(json);

        var opened = CryptoBox.Open(app, agent.PublicKeys, parsed);
        Assert.Equal(message, opened);
        Assert.Equal(1, parsed.V);
        Assert.Equal("diag", parsed.Kind);
        Assert.Equal(agent.Fingerprint, parsed.From);
    }

    [Fact]
    public void Tampered_Header_Fails_Signature()
    {
        using var app = MailboxIdentity.Create();
        using var agent = MailboxIdentity.Create();
        var env = CryptoBox.Seal(agent, app.PublicKeys, "{}", "http", "read", "s1", 1);
        env.Risk = "spend"; // privilege escalation attempt

        var ex = Assert.Throws<MailboxSecurityException>(() => CryptoBox.Open(app, agent.PublicKeys, env));
        Assert.Equal("signature", ex.Code);
    }

    [Fact]
    public void Tampered_Ciphertext_Fails()
    {
        using var app = MailboxIdentity.Create();
        using var agent = MailboxIdentity.Create();
        var env = CryptoBox.Seal(agent, app.PublicKeys, "{\"x\":1}", "http", "read", "s1", 1);
        var bytes = Convert.FromBase64String(env.Enc.Data);
        bytes[0] ^= 0x01;
        env.Enc.Data = Convert.ToBase64String(bytes);

        var ex = Assert.Throws<MailboxSecurityException>(() => CryptoBox.Open(app, agent.PublicKeys, env));
        Assert.Equal("signature", ex.Code); // data is covered by the signature first
    }

    [Fact]
    public void Wrong_Sender_Is_Rejected()
    {
        using var app = MailboxIdentity.Create();
        using var agent = MailboxIdentity.Create();
        using var impostor = MailboxIdentity.Create();
        var env = CryptoBox.Seal(impostor, app.PublicKeys, "{}", "os", "write", "s1", 1);

        var ex = Assert.Throws<MailboxSecurityException>(() => CryptoBox.Open(app, agent.PublicKeys, env));
        Assert.Equal("sender", ex.Code);
    }

    [Fact]
    public void Expired_Message_Is_Rejected()
    {
        using var app = MailboxIdentity.Create();
        using var agent = MailboxIdentity.Create();
        var issued = DateTimeOffset.UtcNow.AddMinutes(-30);
        var env = CryptoBox.Seal(agent, app.PublicKeys, "{}", "diag", "read", "s1", 1, TimeSpan.FromMinutes(10), issued);

        var ex = Assert.Throws<MailboxSecurityException>(() => CryptoBox.Open(app, agent.PublicKeys, env));
        Assert.Equal("expired", ex.Code);
    }

    [Fact]
    public void Replay_Is_Rejected_By_Id_And_Sequence()
    {
        using var app = MailboxIdentity.Create();
        using var agent = MailboxIdentity.Create();
        var guard = new ReplayGuard();
        var first = CryptoBox.Seal(agent, app.PublicKeys, "{}", "diag", "read", "s1", 5);
        CryptoBox.Open(app, agent.PublicKeys, first, guard: guard);

        var replay = Assert.Throws<MailboxSecurityException>(() => CryptoBox.Open(app, agent.PublicKeys, first, guard: guard));
        Assert.Equal("replay", replay.Code);

        var older = CryptoBox.Seal(agent, app.PublicKeys, "{}", "diag", "read", "s1", 4);
        var seq = Assert.Throws<MailboxSecurityException>(() => CryptoBox.Open(app, agent.PublicKeys, older, guard: guard));
        Assert.Equal("replay", seq.Code);

        var next = CryptoBox.Seal(agent, app.PublicKeys, "{}", "diag", "read", "s1", 6);
        Assert.Equal("{}", CryptoBox.Open(app, agent.PublicKeys, next, guard: guard));
    }

    [Fact]
    public void Identity_Private_Export_Import_Roundtrip()
    {
        using var original = MailboxIdentity.Create();
        var exported = original.ExportPrivate();
        using var restored = MailboxIdentity.ImportPrivate(exported);

        Assert.Equal(original.ExchangePublicKey, restored.ExchangePublicKey);
        Assert.Equal(original.SigningPublicKey, restored.SigningPublicKey);
        Assert.Equal(original.Fingerprint, restored.Fingerprint);
        Assert.Matches("^[0-9a-f]{4}(:[0-9a-f]{4}){3}$", restored.Fingerprint);
    }

    [Fact]
    public void Oversized_Message_Is_Refused()
    {
        using var app = MailboxIdentity.Create();
        using var agent = MailboxIdentity.Create();
        var big = new string('a', CryptoBox.MaxPlaintextBytes + 1);
        var ex = Assert.Throws<MailboxSecurityException>(() => CryptoBox.Seal(agent, app.PublicKeys, big, "http", "read", "s1", 1));
        Assert.Equal("too_large", ex.Code);
    }
}
