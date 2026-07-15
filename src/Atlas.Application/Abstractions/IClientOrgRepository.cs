using Atlas.Application.Pagination;
using Atlas.Domain.Orgs;

namespace Atlas.Application.Abstractions;

public interface IClientOrgRepository
{
    Task AddAsync(ClientOrg org, CancellationToken cancellationToken);

    Task<ClientOrg?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Newest-first page. <paramref name="after"/> is an exclusive lower bound in
    /// (CreatedAtUtc DESC, Id DESC) order; callers over-fetch by one to detect a next page.
    /// </summary>
    Task<IReadOnlyList<ClientOrg>> ListAfterAsync(
        TimelineCursor? after,
        int fetch,
        CancellationToken cancellationToken);
}
