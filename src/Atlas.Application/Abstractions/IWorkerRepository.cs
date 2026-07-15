using Atlas.Application.Pagination;
using Atlas.Domain.Countries;
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

    // S07 addition: the payroll population.
    /// <summary>
    /// Every worker of one org in one country who was employed on or before
    /// <paramref name="asOf"/> — i.e. "active" for a period ending that day. NOT paginated:
    /// a payroll run processes the whole population in one pass, so it wants the whole set,
    /// not a page of it.
    /// </summary>
    // WHY hired-on-or-before-period-end defines "active" in v1: it is the simplest rule that
    // matches the data we have (Worker.HiredOn). Terminations and leavers are an S05
    // engagement-state concern; when engagement states are integrated, "active" tightens to
    // "engagement is Active on the period" — refinement noted in NOTES/ADR-0008, not built.
    Task<IReadOnlyList<Worker>> ListActiveInCountryAsync(
        Guid orgId,
        CountryCode country,
        DateOnly asOf,
        CancellationToken cancellationToken);
}

public interface IEmploymentRecordRepository
{
    Task AddAsync(EmploymentRecord record, CancellationToken cancellationToken);

    Task<IReadOnlyList<EmploymentRecord>> ListForWorkerAsync(
        Guid workerId,
        CancellationToken cancellationToken);
}
