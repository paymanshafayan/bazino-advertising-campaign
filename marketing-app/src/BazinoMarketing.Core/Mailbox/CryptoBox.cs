using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BazinoMarketing.Core.Mailbox;

public sealed class MailboxSecurityException : Exception
{
    public string Code { get; }
    public MailboxSecurityException(string code, string message) : base(message) { Code = code; }
}

/// <summary>
/// Long-lived identity of one mailbox party (the app or the agent): an ECDH P-256 key for encryption and an
/// ECDSA P-256 key for signatures. Private keys are exported as PKCS#8 and kept in the secret store.
/// </summary>
public sealed class MailboxIdentity : IDisposable
{
    public ECDiffieHellman Exchange { get; }
    public ECDsa Signing { get; }

    private MailboxIdentity(ECDiffieHellman exchange, ECDsa signing)
    {
        Exchange = exchange;
        Signing = signing;
    }

    public static MailboxIdentity Create() =>
        new(ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256), ECDsa.Create(ECCurve.NamedCurves.nistP256));

    public string ExchangePublicKey => Convert.ToBase64String(Exchange.ExportSubjectPublicKeyInfo());
    public string SigningPublicKey => Convert.ToBase64String(Signing.ExportSubjectPublicKeyInfo());
    public PeerKeys PublicKeys => new(ExchangePublicKey, SigningPublicKey);

    /// <summary>Short fingerprint the owner can compare out-of-band (SHA-256 of both public keys, first 8 bytes).</summary>
    public string Fingerprint => PeerKeys.FingerprintOf(ExchangePublicKey, SigningPublicKey);

    public string ExportPrivate()
    {
        var payload = new PrivatePayload
        {
            Exchange = Convert.ToBase64String(Exchange.ExportPkcs8PrivateKey()),
            Signing = Convert.ToBase64String(Signing.ExportPkcs8PrivateKey())
        };
        return JsonSerializer.Serialize(payload);
    }

    public static MailboxIdentity ImportPrivate(string json)
    {
        var payload = JsonSerializer.Deserialize<PrivatePayload>(json) ?? throw new InvalidDataException("identity json empty");
        var ecdh = ECDiffieHellman.Create();
        ecdh.ImportPkcs8PrivateKey(Convert.FromBase64String(payload.Exchange), out _);
        var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(payload.Signing), out _);
        return new MailboxIdentity(ecdh, ecdsa);
    }

    public void Dispose()
    {
        Exchange.Dispose();
        Signing.Dispose();
    }

    private sealed class PrivatePayload
    {
        [JsonPropertyName("x")] public string Exchange { get; set; } = "";
        [JsonPropertyName("s")] public string Signing { get; set; } = "";
    }
}

/// <summary>Public half of a peer identity (SPKI, base64).</summary>
public sealed record PeerKeys(string ExchangePublicKey, string SigningPublicKey)
{
    public string Fingerprint => FingerprintOf(ExchangePublicKey, SigningPublicKey);

    public static string FingerprintOf(string exchange, string signing)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(exchange + "|" + signing));
        var hex = Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
        return string.Join(":", Enumerable.Range(0, 4).Select(i => hex.Substring(i * 4, 4)));
    }
}

public sealed class EncryptedPart
{
    [JsonPropertyName("salt")] public string Salt { get; set; } = "";
    [JsonPropertyName("nonce")] public string Nonce { get; set; } = "";
    [JsonPropertyName("data")] public string Data { get; set; } = "";
}

/// <summary>
/// Wire format of one mailbox message. Every field outside <c>enc</c> is authenticated by the signature and
/// used as AES-GCM associated data, so nothing can be altered in transit without detection.
/// </summary>
public sealed class Envelope
{
    [JsonPropertyName("v")] public int V { get; set; } = 1;
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("session")] public string Session { get; set; } = "";
    [JsonPropertyName("seq")] public long Seq { get; set; }
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("risk")] public string Risk { get; set; } = "read";
    [JsonPropertyName("issuedAt")] public DateTimeOffset IssuedAt { get; set; }
    [JsonPropertyName("expiresAt")] public DateTimeOffset ExpiresAt { get; set; }
    [JsonPropertyName("from")] public string From { get; set; } = "";
    [JsonPropertyName("enc")] public EncryptedPart Enc { get; set; } = new();
    [JsonPropertyName("sig")] public string Sig { get; set; } = "";

    public string ToJson() => JsonSerializer.Serialize(this, CryptoBox.JsonOptions);
    public static Envelope FromJson(string json) =>
        JsonSerializer.Deserialize<Envelope>(json, CryptoBox.JsonOptions) ?? throw new InvalidDataException("envelope json empty");

    /// <summary>Canonical byte string covered by the signature and used as AEAD associated data.</summary>
    public byte[] SignedBytes()
    {
        var s = string.Join("\n", new[]
        {
            "bazino-mailbox-v1", V.ToString(), Id, Session, Seq.ToString(), Kind, Risk,
            IssuedAt.ToUniversalTime().ToString("O"), ExpiresAt.ToUniversalTime().ToString("O"), From,
            Enc.Salt, Enc.Nonce, Enc.Data
        });
        return Encoding.UTF8.GetBytes(s);
    }
}

public static class CryptoBox
{
    public const string Info = "bazino-mailbox-v1";
    public const int MaxPlaintextBytes = 64 * 1024;
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(2);
    private static readonly byte[] InfoBytes = Encoding.ASCII.GetBytes(Info);

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static Envelope Seal(MailboxIdentity sender, PeerKeys recipient, string plaintext, string kind, string risk,
        string session, long seq, TimeSpan? ttl = null, DateTimeOffset? now = null, string? id = null)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(recipient);
        var plain = Encoding.UTF8.GetBytes(plaintext ?? "");
        if (plain.Length > MaxPlaintextBytes)
            throw new MailboxSecurityException("too_large", $"message exceeds {MaxPlaintextBytes} bytes");

        var issued = now ?? DateTimeOffset.UtcNow;
        var env = new Envelope
        {
            Id = id ?? Guid.NewGuid().ToString("N"),
            Session = session,
            Seq = seq,
            Kind = kind,
            Risk = risk,
            IssuedAt = issued,
            ExpiresAt = issued + (ttl ?? DefaultTtl),
            From = sender.Fingerprint
        };

        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        env.Enc.Salt = Convert.ToBase64String(salt);
        env.Enc.Nonce = Convert.ToBase64String(nonce);

        using var peer = ImportExchangeKey(recipient.ExchangePublicKey);
        var key = DeriveKey(sender.Exchange, peer, salt);
        try
        {
            var cipher = new byte[plain.Length];
            var tag = new byte[16];
            // Associated data = header with an empty data field; the ciphertext itself is covered by the GCM tag.
            var aad = env.SignedBytes();
            using (var aes = new AesGcm(key, 16))
                aes.Encrypt(nonce, plain, cipher, tag, aad);
            var data = new byte[cipher.Length + tag.Length];
            Buffer.BlockCopy(cipher, 0, data, 0, cipher.Length);
            Buffer.BlockCopy(tag, 0, data, cipher.Length, tag.Length);
            env.Enc.Data = Convert.ToBase64String(data);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        env.Sig = Convert.ToBase64String(sender.Signing.SignData(env.SignedBytes(), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        return env;
    }

    public static string Open(MailboxIdentity recipient, PeerKeys sender, Envelope env, DateTimeOffset? now = null, ReplayGuard? guard = null)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(env);
        if (env.V != 1) throw new MailboxSecurityException("version", $"unsupported envelope version {env.V}");
        if (string.IsNullOrWhiteSpace(env.Id) || string.IsNullOrWhiteSpace(env.Session))
            throw new MailboxSecurityException("malformed", "missing id/session");
        if (!string.Equals(env.From, sender.Fingerprint, StringComparison.Ordinal))
            throw new MailboxSecurityException("sender", "envelope is not from the paired agent key");

        byte[] sig, data, salt, nonce;
        try
        {
            sig = Convert.FromBase64String(env.Sig);
            data = Convert.FromBase64String(env.Enc.Data);
            salt = Convert.FromBase64String(env.Enc.Salt);
            nonce = Convert.FromBase64String(env.Enc.Nonce);
        }
        catch (FormatException)
        {
            throw new MailboxSecurityException("malformed", "envelope fields are not valid base64");
        }
        if (data.Length < 16 || data.Length > MaxPlaintextBytes + 16 || nonce.Length != 12 || salt.Length < 8)
            throw new MailboxSecurityException("malformed", "envelope sizes out of range");

        // 1) Signature over every header field (and the ciphertext).
        using (var verifier = ImportSigningKey(sender.SigningPublicKey))
        {
            if (!verifier.VerifyData(env.SignedBytes(), sig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw new MailboxSecurityException("signature", "signature verification failed");
        }

        // 2) Freshness.
        var t = now ?? DateTimeOffset.UtcNow;
        if (env.ExpiresAt < t) throw new MailboxSecurityException("expired", "message expired");
        if (env.IssuedAt > t + MaxClockSkew) throw new MailboxSecurityException("future", "message issued in the future");
        if (env.ExpiresAt - env.IssuedAt > TimeSpan.FromHours(1)) throw new MailboxSecurityException("ttl", "ttl too long");

        // 3) Replay.
        guard?.Check(env);

        // 4) Decrypt with the AAD built from the same header, data field cleared.
        var aadEnv = new Envelope
        {
            V = env.V, Id = env.Id, Session = env.Session, Seq = env.Seq, Kind = env.Kind, Risk = env.Risk,
            IssuedAt = env.IssuedAt, ExpiresAt = env.ExpiresAt, From = env.From,
            Enc = new EncryptedPart { Salt = env.Enc.Salt, Nonce = env.Enc.Nonce, Data = "" }
        };
        using var peer = ImportExchangeKey(sender.ExchangePublicKey);
        var key = DeriveKey(recipient.Exchange, peer, salt);
        try
        {
            var cipher = data.AsSpan(0, data.Length - 16);
            var tag = data.AsSpan(data.Length - 16, 16);
            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, cipher, tag, plain, aadEnv.SignedBytes());
            guard?.Commit(env);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            throw new MailboxSecurityException("decrypt", "authenticated decryption failed");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] DeriveKey(ECDiffieHellman mine, ECDiffieHellman peer, byte[] salt)
    {
        var shared = mine.DeriveRawSecretAgreement(peer.PublicKey);
        try
        {
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, salt, InfoBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(shared);
        }
    }

    public static ECDiffieHellman ImportExchangeKey(string spkiBase64)
    {
        var ecdh = ECDiffieHellman.Create();
        try { ecdh.ImportSubjectPublicKeyInfo(Convert.FromBase64String(spkiBase64), out _); }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            ecdh.Dispose();
            throw new MailboxSecurityException("peer_key", "invalid peer exchange key");
        }
        return ecdh;
    }

    public static ECDsa ImportSigningKey(string spkiBase64)
    {
        var ecdsa = ECDsa.Create();
        try { ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(spkiBase64), out _); }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            ecdsa.Dispose();
            throw new MailboxSecurityException("peer_key", "invalid peer signing key");
        }
        return ecdsa;
    }
}

/// <summary>Rejects a message id seen before and non-increasing sequence numbers within a session.</summary>
public sealed class ReplayGuard
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _seen = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _lastSeq = new(StringComparer.Ordinal);
    private readonly TimeSpan _retention;

    public ReplayGuard(TimeSpan? retention = null)
    {
        _retention = retention ?? TimeSpan.FromHours(2);
    }

    public void Check(Envelope env)
    {
        lock (_gate)
        {
            Prune(DateTimeOffset.UtcNow);
            if (_seen.ContainsKey(env.Id)) throw new MailboxSecurityException("replay", "message id already processed");
            if (_lastSeq.TryGetValue(env.Session, out var last) && env.Seq <= last)
                throw new MailboxSecurityException("replay", $"sequence {env.Seq} not after {last}");
        }
    }

    public void Commit(Envelope env)
    {
        lock (_gate)
        {
            _seen[env.Id] = env.ExpiresAt;
            _lastSeq[env.Session] = env.Seq;
        }
    }

    private void Prune(DateTimeOffset now)
    {
        if (_seen.Count < 1024) return;
        foreach (var k in _seen.Where(kv => kv.Value + _retention < now).Select(kv => kv.Key).ToList()) _seen.Remove(k);
    }
}
