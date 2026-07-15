namespace Atlas.Application.Abstractions;

/// <summary>
/// Mints high-entropy secrets (webhook signing secrets, partner API keys) and derives their at-rest
/// hashes. Randomness and hashing are infrastructure concerns kept behind a port so use cases stay
/// deterministic under test — a fake generator hands back a known value.
/// </summary>
public interface ISecretGenerator
{
    /// <summary>A new opaque secret, prefixed for humans (e.g. <c>whsec_…</c>, <c>atk_…</c>).</summary>
    string NewSecret(string prefix);

    /// <summary>The at-rest hash of a secret. SHA-256 is enough for 256-bit random tokens.</summary>
    string HashOf(string secret);
}
