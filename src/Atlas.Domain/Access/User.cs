namespace Atlas.Domain.Access;

public sealed class User
{
    public const int MaxDisplayNameLength = 120;

    public Guid Id { get; }
    public EmailAddress Email { get; }
    public string DisplayName { get; private set; }

    // WHY: the Domain stores an opaque verifier string and never learns what a
    // "PBKDF2" is — hashing strategy is an Infrastructure decision behind the
    // IPasswordHasher port. If the format changes, no Domain code notices.
    public string PasswordHash { get; private set; }

    /// <summary>Platform-side operator (Atlas staff). Orthogonal to org roles.</summary>
    public bool IsAtlasOps { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    private User(
        Guid id,
        EmailAddress email,
        string displayName,
        string passwordHash,
        bool isAtlasOps,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        Email = email;
        DisplayName = displayName;
        PasswordHash = passwordHash;
        IsAtlasOps = isAtlasOps;
        CreatedAtUtc = createdAtUtc;
    }

    public static User Create(
        EmailAddress email,
        string? displayName,
        string passwordHash,
        DateTimeOffset createdAtUtc,
        bool isAtlasOps = false)
    {
        string name = (displayName ?? string.Empty).Trim();

        if (name.Length is 0 or > MaxDisplayNameLength)
        {
            throw new DomainRuleException(
                $"Display name is required (max {MaxDisplayNameLength} characters).");
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainRuleException("A password verifier is required.");
        }

        return new User(Guid.NewGuid(), email, name, passwordHash, isAtlasOps, createdAtUtc);
    }

    // WHY: EF Core materialization only.
    private User()
    {
        DisplayName = null!;
        PasswordHash = null!;
    }
}
