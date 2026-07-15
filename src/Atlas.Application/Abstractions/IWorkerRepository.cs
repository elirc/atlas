using Atlas.Application.Pagination;
using Atlas.Domain.Workforce;

namespace Atlas.Application.Abstractions;

public interface IWorkerRepository
{
    Task AddAsync(Worker worker, CancellationToken cancellationToken);

    /// <summary>Load by primary key.</summary>
    Task<Worker?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Load by primary key within one org — the tenant-safe by-id read.</summary>
    Task<Worker?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken cancellationToken);

    /// <summary>Newest-first org-scoped page; callers over-fetch by one (see ListClientOrgs).</summary>
    Task<IReadOnlyList<Worker>> ListForOrgAsync(
        Guid orgId,
        TimelineCursor? after,
        int fetch,
        CancellationToken cancellationToken);
}

public interface IEmploymentRecordRepository
{
    Task AddAsync(EmploymentRecord record, CancellationToken cancellationToken);

    Task<IReadOnlyList<EmploymentRecord>> ListForWorkerAsync(
        Guid workerId,
        CancellationToken cancellationToken);
}
