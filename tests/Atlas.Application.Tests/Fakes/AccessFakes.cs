using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Access;

namespace Atlas.Application.Tests.Fakes;

public sealed class InMemoryUserRepository : IUserRepository
{
    public List<User> Rows { get; } = [];

    public Task AddAsync(User user, CancellationToken ct)
    {
        Rows.Add(user);
        return Task.CompletedTask;
    }

    public Task<User?> FindAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Rows.FirstOrDefault(u => u.Id == id));

    public Task<User?> FindByEmailAsync(EmailAddress email, CancellationToken ct) =>
        Task.FromResult(Rows.FirstOrDefault(u => u.Email == email));
}

public sealed class InMemoryMembershipRepository : IMembershipRepository
{
    public List<Membership> Rows { get; } = [];

    public Task AddAsync(Membership membership, CancellationToken ct)
    {
        Rows.Add(membership);
        return Task.CompletedTask;
    }

    public Task<Membership?> FindForUserAsync(Guid userId, CancellationToken ct) =>
        Task.FromResult(Rows.FirstOrDefault(m => m.UserId == userId));
}

public sealed class InMemorySessionRepository : ISessionRepository
{
    public List<Session> Rows { get; } = [];

    public Task AddAsync(Session session, CancellationToken ct)
    {
        Rows.Add(session);
        return Task.CompletedTask;
    }

    public Task<Session?> FindByTokenHashAsync(string tokenHash, CancellationToken ct) =>
        Task.FromResult(Rows.FirstOrDefault(s => s.TokenHash == tokenHash));
}

/// <summary>Reversible "hash" — fine for tests, where we assert flows, not crypto.</summary>
public sealed class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string password) => $"hashed:{password}";

    public bool Verify(string password, string storedHash) => storedHash == $"hashed:{password}";
}

public sealed class SequentialSessionTokens : ISessionTokens
{
    private int _minted;

    public (string Token, string Hash) Mint()
    {
        _minted++;
        return ($"token-{_minted}", $"tokenhash-{_minted}");
    }

    public string HashOf(string token) => token.Replace("token-", "tokenhash-");
}

public sealed class StubContext : ICurrentContext
{
    public bool IsAuthenticated { get; init; }
    public Guid UserId { get; init; }
    public bool IsAtlasOps { get; init; }
    public Guid? OrgId { get; init; }
    public Role? Role { get; init; }

    public static StubContext Anonymous() => new();

    public static StubContext OrgUser(Guid orgId, Role role, Guid? userId = null) => new()
    {
        IsAuthenticated = true,
        UserId = userId ?? Guid.NewGuid(),
        OrgId = orgId,
        Role = role,
    };

    public static StubContext Ops() => new()
    {
        IsAuthenticated = true,
        UserId = Guid.NewGuid(),
        IsAtlasOps = true,
    };

    public void RequireUser()
    {
        if (!IsAuthenticated)
        {
            throw new UnauthenticatedException();
        }
    }

    public Guid RequireOrg()
    {
        RequireUser();
        return OrgId ?? throw new ForbiddenException("An org context is required.");
    }

    public void RequireRole(params Role[] anyOf)
    {
        RequireUser();
        if (!IsAtlasOps && (Role is not { } role || !anyOf.Contains(role)))
        {
            throw new ForbiddenException();
        }
    }

    public void RequireAtlasOps()
    {
        RequireUser();
        if (!IsAtlasOps)
        {
            throw new ForbiddenException("Platform staff only.");
        }
    }
}
