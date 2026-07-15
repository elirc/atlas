using Atlas.Application.Abstractions;
using Atlas.Infrastructure.Security;

namespace Atlas.Api.Tests;

/// <summary>
/// The REAL hasher's unit tests (the HTTP suite swaps in a fast fake — this file
/// is where the production crypto is actually exercised).
/// </summary>
public class Pbkdf2PasswordHasherTests
{
    private readonly IPasswordHasher _hasher = new Pbkdf2PasswordHasher();

    [Fact]
    public void Hash_then_verify_roundtrips()
    {
        string hash = _hasher.Hash("correct-horse-battery");

        Assert.True(_hasher.Verify("correct-horse-battery", hash));
        Assert.False(_hasher.Verify("wrong-horse-battery", hash));
    }

    [Fact]
    public void Same_password_hashes_differently_every_time()
    {
        // LEARN: per-password random salt. Identical hashes for identical passwords
        // would let an attacker crack one hash and unlock every user who chose the
        // same password — and reveal WHO shares passwords, which is itself a leak.
        Assert.NotEqual(_hasher.Hash("same-password-here"), _hasher.Hash("same-password-here"));
    }

    [Fact]
    public void Stored_format_self_describes_its_parameters()
    {
        string hash = _hasher.Hash("some-password-here");

        // pbkdf2-sha256$<iterations>$<salt>$<key>
        string[] parts = hash.Split('$');
        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2-sha256", parts[0]);
        Assert.True(int.Parse(parts[1]) >= 600_000);
    }

    [Fact]
    public void Garbage_stored_hashes_verify_false_never_throw()
    {
        Assert.False(_hasher.Verify("password", "not-a-hash"));
        Assert.False(_hasher.Verify("password", ""));
        Assert.False(_hasher.Verify("password", "pbkdf2-sha256$abc$??$??"));
    }
}
