using Atlas.Application.Pagination;
using Atlas.Domain.Leave;

namespace Atlas.Application.Abstractions;

// INTEGRATOR NOTE (S08): this interface accumulates on S06's. The ONLY S08 addition is
// ListPendingForOrgAsync (the leave feed for the cross-type approval inbox). Everything else is
// verbatim S06 — keep it. See meta/NOTES.md.
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

    /// <summary>Submitted (awaiting-decision) requests for an org — the approval-inbox feed (S08).</summary>
    // 🔗 The leave half of the unified approval inbox. Its expense twin is the identically-shaped
    // IExpenseClaimRepository.ListPendingForOrgAsync — two feeds, one merged worklist.
    Task<IReadOnlyList<LeaveRequest>> ListPendingForOrgAsync(
        Guid orgId,
        int fetch,
        CancellationToken cancellationToken);
}
