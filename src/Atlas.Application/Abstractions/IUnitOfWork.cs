namespace Atlas.Application.Abstractions;

/// <summary>
/// Commits everything a use case changed, atomically.
/// </summary>
// WHY: repositories collect changes; the use case decides when they become real.
// Keeping commit explicit (instead of hiding SaveChanges inside every repository
// method) is what makes "several writes, one transaction" the default instead of
// an advanced trick. This seam becomes load-bearing in Sprint 10 (the outbox).
public interface IUnitOfWork
{
    Task CommitAsync(CancellationToken cancellationToken);
}
