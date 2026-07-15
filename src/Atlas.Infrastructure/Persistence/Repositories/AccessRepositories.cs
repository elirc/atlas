using Atlas.Application.Abstractions;
using Atlas.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository(AtlasDbContext db) : IUserRepository
{
    public async Task AddAsync(User user, CancellationToken ct) =>
        await db.Set<User>().AddAsync(user, ct);

    public async Task<User?> FindAsync(Guid id, CancellationToken ct) =>
        await db.Set<User>().FindAsync([id], ct);

    public Task<User?> FindByEmailAsync(EmailAddress email, CancellationToken ct) =>
        db.Set<User>().SingleOrDefaultAsync(u => u.Email == email, ct);
}

internal sealed class MembershipRepository(AtlasDbContext db) : IMembershipRepository
{
    public async Task AddAsync(Membership membership, CancellationToken ct) =>
        await db.Set<Membership>().AddAsync(membership, ct);

    public Task<Membership?> FindForUserAsync(Guid userId, CancellationToken ct) =>
        db.Set<Membership>().SingleOrDefaultAsync(m => m.UserId == userId, ct);
}

internal sealed class SessionRepository(AtlasDbContext db) : ISessionRepository
{
    public async Task AddAsync(Session session, CancellationToken ct) =>
        await db.Set<Session>().AddAsync(session, ct);

    public Task<Session?> FindByTokenHashAsync(string tokenHash, CancellationToken ct) =>
        db.Set<Session>().SingleOrDefaultAsync(s => s.TokenHash == tokenHash, ct);
}
