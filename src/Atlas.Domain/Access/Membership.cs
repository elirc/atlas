namespace Atlas.Domain.Access;

/// <summary>Grants a user a role inside one client org.</summary>
public sealed class Membership
{
    public Guid Id { get; }
    public Guid UserId { get; }
    public Guid OrgId { get; }
    public Role Role { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; }

    private Membership(Guid id, Guid userId, Guid orgId, Role role, DateTimeOffset createdAtUtc)
    {
        Id = id;
        UserId = userId;
        OrgId = orgId;
        Role = role;
        CreatedAtUtc = createdAtUtc;
    }

    public static Membership Grant(Guid userId, Guid orgId, Role role, DateTimeOffset createdAtUtc)
    {
        if (userId == Guid.Empty || orgId == Guid.Empty)
        {
            throw new DomainRuleException("A membership needs both a user and an org.");
        }

        return new Membership(Guid.NewGuid(), userId, orgId, role, createdAtUtc);
    }

    // WHY: EF Core materialization only.
    private Membership()
    {
    }
}
