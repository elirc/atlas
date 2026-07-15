using Atlas.Application.Pagination;
using Atlas.Domain.Expenses;

namespace Atlas.Application.Abstractions;

public interface IExpenseClaimRepository
{
    Task AddAsync(ExpenseClaim claim, CancellationToken cancellationToken);

    /// <summary>Load by primary key within one org — the tenant-safe by-id read.</summary>
    Task<ExpenseClaim?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken cancellationToken);

    /// <summary>Newest-first org-scoped page; callers over-fetch by one (see ListClientOrgs).</summary>
    Task<IReadOnlyList<ExpenseClaim>> ListForOrgAsync(
        Guid orgId,
        TimelineCursor? after,
        int fetch,
        CancellationToken cancellationToken);

    /// <summary>Submitted (awaiting-decision) claims for an org — the approval-inbox feed.</summary>
    // WHY newest-submitted-first, capped: the inbox is a worklist, not a paginated archive. It
    // shows what an approver must act on, most recent first; the cap keeps it a bounded read even
    // for an org with a backlog. (Cross-type cursor pagination is a deliberate non-goal — ADR-0009.)
    Task<IReadOnlyList<ExpenseClaim>> ListPendingForOrgAsync(
        Guid orgId,
        int fetch,
        CancellationToken cancellationToken);
}
