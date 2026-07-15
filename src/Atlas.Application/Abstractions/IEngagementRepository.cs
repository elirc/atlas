using Atlas.Application.Pagination;
using Atlas.Domain.Engagements;

namespace Atlas.Application.Abstractions;

public interface IEngagementRepository
{
    Task AddAsync(Engagement engagement, CancellationToken cancellationToken);

    /// <summary>
    /// Load one engagement within an org — the tenant-safe by-id read. Returns a
    /// TRACKED aggregate (with its checklist) so a transition use case can mutate it
    /// and have <see cref="IUnitOfWork.CommitAsync"/> persist the change.
    /// </summary>
    Task<Engagement?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken cancellationToken);

    /// <summary>Newest-first org-scoped page; callers over-fetch by one (see ListWorkers).</summary>
    Task<IReadOnlyList<Engagement>> ListForOrgAsync(
        Guid orgId,
        TimelineCursor? after,
        int fetch,
        CancellationToken cancellationToken);
}
