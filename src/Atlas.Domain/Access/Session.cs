namespace Atlas.Domain.Access;

/// <summary>
/// A login session. The bearer token itself never touches the database —
/// only its hash is stored, so a leaked table cannot impersonate anyone.
/// </summary>
public sealed class Session
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    public Guid Id { get; }
    public Guid UserId { get; }
    public string TokenHash { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    private Session(
        Guid id,
        Guid userId,
        string tokenHash,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        Id = id;
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public static Session Start(Guid userId, string tokenHash, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new DomainRuleException("A session needs a token hash.");
        }

        return new Session(Guid.NewGuid(), userId, tokenHash, nowUtc, nowUtc + Lifetime);
    }

    // LEARN: the session knows its OWN liveness rules; callers ask, they don't
    // re-derive. When "active" grows a third condition (org suspended, password
    // rotated), there is exactly one method to change.
    public bool IsActive(DateTimeOffset nowUtc) =>
        RevokedAtUtc is null && nowUtc < ExpiresAtUtc;

    public void Revoke(DateTimeOffset nowUtc)
    {
        // WHY: idempotent on purpose — logging out twice is a no-op, not an error.
        // Clients retry; retries must be safe (the S10 idempotency theme, early).
        RevokedAtUtc ??= nowUtc;
    }

    // WHY: EF Core materialization only.
    private Session()
    {
        TokenHash = null!;
    }
}
