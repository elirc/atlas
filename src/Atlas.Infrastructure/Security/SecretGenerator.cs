using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Atlas.Application.Abstractions;

namespace Atlas.Infrastructure.Security;

internal sealed class SecretGenerator : ISecretGenerator
{
    // 256 bits of CSPRNG entropy, url-safe, human-prefixed. Unguessable and carrying no claims.
    public string NewSecret(string prefix) =>
        prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    // SHA-256, not PBKDF2: a 256-bit random secret cannot be brute-forced, so the hash only needs to
    // be one-way, not slow — the same reasoning as session tokens (SessionTokenService).
    public string HashOf(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
