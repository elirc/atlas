using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Atlas.Application.Abstractions;

namespace Atlas.Infrastructure.Security;

internal sealed class SessionTokenService : ISessionTokens
{
    public (string Token, string Hash) Mint()
    {
        // 256 bits of CSPRNG entropy — unguessable, and carrying no claims. An
        // opaque token beats a JWT here because sessions are server-side state
        // anyway (we need revocation), so self-describing tokens buy nothing.
        string token = "atls_" + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

        return (token, HashOf(token));
    }

    // WHY: SHA-256, not PBKDF2, for session tokens. Password hashing is slow on
    // purpose because passwords are low-entropy; a 256-bit random token cannot be
    // brute-forced, so the hash only needs to be one-way, not expensive —
    // and login-per-request cannot afford 600k iterations.
    public string HashOf(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
