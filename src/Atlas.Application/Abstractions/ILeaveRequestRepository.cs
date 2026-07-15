using Atlas.Application.Pagination;
using Atlas.Domain.Leave;

namespace Atlas.Application.Abstractions;

public interface ILeaveRequestRepository
{
    Task AddAsync(LeaveRequest request, CancellationToken cancellationToken);

    /// <summary>Load by primary key within one org — the tenant-safe by-id read.</summary>
    Task<LeaveRequest?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken cancellationToken);

    /// <summary>A worker's APPROVED requests — the ledger the balance is folded from.</summary>
    // WHY: there is no stored balance. "Spent" is the sum of these rows, and "does this
    // overlap?" is a scan of these rows. The approved requests ARE the ledger (ADR-0007).
    Task<IReadOnlyList<LeaveRequest>> ListApprovedForWorkerAsync(
        Guid workerId, CancellationToken cancellationToken);

    /// <summary>Newest-first org-scoped page; callers over-fetch by one (see ListWorkers).</summary>
    Task<IReadOnlyList<LeaveRequest>> ListForOrgAsync(
        Guid orgId,
        TimelineCursor? after,
        int fetch,
        CancellationToken cancellationToken);
}
