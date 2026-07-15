using Atlas.Application.Pagination;
using Atlas.Domain.Quotes;

namespace Atlas.Application.Abstractions;

public interface IQuoteRepository
{
    Task AddAsync(Quote quote, CancellationToken cancellationToken);

    /// <summary>Load one quote within one org — the tenant-safe by-id read (see
    /// IWorkerRepository.FindInOrgAsync; cross-org reads come back null → 404).</summary>
    Task<Quote?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken cancellationToken);

    /// <summary>Newest-first org-scoped page; callers over-fetch by one (see ListClientOrgs).</summary>
    Task<IReadOnlyList<Quote>> ListForOrgAsync(
        Guid orgId,
        TimelineCursor? after,
        int fetch,
        CancellationToken cancellationToken);
}
