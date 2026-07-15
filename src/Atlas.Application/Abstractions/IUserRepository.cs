using Atlas.Domain.Access;

namespace Atlas.Application.Abstractions;

public interface IUserRepository
{
    Task AddAsync(User user, CancellationToken cancellationToken);
    Task<User?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<User?> FindByEmailAsync(EmailAddress email, CancellationToken cancellationToken);
}

public interface IMembershipRepository
{
    Task AddAsync(Membership membership, CancellationToken cancellationToken);

    /// <summary>The user's single membership (one org per user for now — ADR-0003).</summary>
    Task<Membership?> FindForUserAsync(Guid userId, CancellationToken cancellationToken);
}

public interface ISessionRepository
{
    Task AddAsync(Session session, CancellationToken cancellationToken);
    Task<Session?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);
}
