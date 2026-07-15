using System.Security.Cryptography;
using Atlas.Application.Abstractions;

namespace Atlas.Infrastructure.Security;

/// <summary>
/// PBKDF2-SHA256 password hashing. Hand-rolled deliberately (ADR-0003): the point
/// is that a mid-level engineer can explain every byte of this file.
/// </summary>
internal sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    // WHY: ~600k iterations is the 2026 OWASP guidance neighborhood for
    // PBKDF2-SHA256. The count is EMBEDDED in each hash, so raising it later
    // upgrades new hashes without invalidating old ones.
    private const int Iterations = 600_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);

        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public bool Verify(string password, string storedHash)
    {
        string[] parts = storedHash.Split('$');

        // LEARN: the stored format self-describes its parameters. Verification uses
        // the STORED iteration count, not the current constant — that is what makes
        // parameter upgrades a deploy, not a migration.
        if (parts is not [_, var iterText, var saltText, var keyText]
            || !int.TryParse(iterText, out int iterations))
        {
            return false;
        }

        byte[] salt = Convert.FromBase64String(saltText);
        byte[] expected = Convert.FromBase64String(keyText);
        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        // WHY: fixed-time comparison. `SequenceEqual` short-circuits on the first
        // differing byte, and that timing difference is measurable across a network.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
