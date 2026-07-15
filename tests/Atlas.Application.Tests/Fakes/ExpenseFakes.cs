using Atlas.Application.Abstractions;
using Atlas.Application.Pagination;
using Atlas.Domain.Approvals;
using Atlas.Domain.Expenses;

namespace Atlas.Application.Tests.Fakes;

// NOTE: the audit / event / unit-of-work / clock / context / worker fakes are REUSED from the
// existing Fakes (RecordingAuditTrail, RecordingEventSink, CountingUnitOfWork, FixedClock,
// StubContext, InMemoryWorkerRepository). S08 adds only this expense-claim repository.
public sealed class InMemoryExpenseClaimRepository : IExpenseClaimRepository
{
    public List<ExpenseClaim> Rows { get; } = [];

    public Task AddAsync(ExpenseClaim claim, CancellationToken ct)
    {
        Rows.Add(claim);
        return Task.CompletedTask;
    }

    public Task<ExpenseClaim?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken ct) =>
        Task.FromResult(Rows.FirstOrDefault(c => c.Id == id && c.OrgId == orgId));

    public Task<IReadOnlyList<ExpenseClaim>> ListForOrgAsync(
        Guid orgId, TimelineCursor? after, int fetch, CancellationToken ct)
    {
        IEnumerable<ExpenseClaim> ordered = Rows
            .Where(c => c.OrgId == orgId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ThenByDescending(c => c.Id);

        if (after is { } cursor)
        {
            ordered = ordered.Where(c =>
                c.CreatedAtUtc < cursor.CreatedAtUtc
                || (c.CreatedAtUtc == cursor.CreatedAtUtc && c.Id.CompareTo(cursor.Id) < 0));
        }

        IReadOnlyList<ExpenseClaim> page = [.. ordered.Take(fetch)];
        return Task.FromResult(page);
    }

    public Task<IReadOnlyList<ExpenseClaim>> ListPendingForOrgAsync(
        Guid orgId, int fetch, CancellationToken ct)
    {
        IReadOnlyList<ExpenseClaim> pending =
        [
            .. Rows
                .Where(c => c.OrgId == orgId && c.Approval == ApprovalState.Submitted)
                .OrderByDescending(c => c.SubmittedAtUtc)
                .ThenByDescending(c => c.Id)
                .Take(fetch)
        ];
        return Task.FromResult(pending);
    }
}
