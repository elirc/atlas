using Atlas.Application.Abstractions;

namespace Atlas.Infrastructure.Persistence;

internal sealed class EfUnitOfWork(AtlasDbContext db) : IUnitOfWork
{
    // LEARN: the port promised "everything the use case changed, atomically" —
    // EF's change tracker + SaveChanges IS that promise for a single DbContext.
    // When the outbox arrives (Sprint 10), this one call is where events and state
    // will join the same transaction.
    public Task CommitAsync(CancellationToken cancellationToken) =>
        db.SaveChangesAsync(cancellationToken);
}
