using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Approvals;
using Atlas.Domain.Expenses;
using Atlas.Domain.Leave;

namespace Atlas.Application.Approvals;

/// <summary>
/// The cross-type approval inbox: every submitted leave request AND expense claim in the caller's
/// org, as one merged, newest-submitted-first worklist. This use case is only writable BECAUSE the
/// two aggregates now share the approval lifecycle — it is the extraction earning its keep.
/// </summary>
public sealed class ListPendingApprovals(
    ILeaveRequestRepository leave,
    IExpenseClaimRepository expenses,
    ICurrentContext context)
{
    /// <summary>Cap the merged worklist; the same 1..100 contract every list honours.</summary>
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;

    public async Task<IReadOnlyList<PendingApproval>> ExecuteAsync(
        int? limit, CancellationToken cancellationToken)
    {
        // The approver roles are the SHARED set — identical for both subjects today, so either
        // marker resolves them. Whoever may decide is who may see the queue of things to decide.
        context.RequireRole(ApprovalWorkflow<LeaveApprovalSubject>.ApproverRoles);
        Guid orgId = context.RequireOrg();

        int cap = limit ?? DefaultLimit;
        if (cap < 1 || cap > MaxLimit)
        {
            throw new RequestValidationException($"limit must be between 1 and {MaxLimit}.");
        }

        // WHY over-fetch each feed by `cap` (not cap+1) then merge-and-cap: with two independent
        // sources there is no single cursor to hand back, so this view is a bounded worklist, not
        // a paginated archive (ADR-0009 records the deferral). Fetching `cap` from each guarantees
        // the merged top-`cap` is correct no matter how the two interleave.
        IReadOnlyList<LeaveRequest> pendingLeave =
            await leave.ListPendingForOrgAsync(orgId, cap, cancellationToken);
        IReadOnlyList<ExpenseClaim> pendingExpenses =
            await expenses.ListPendingForOrgAsync(orgId, cap, cancellationToken);

        IEnumerable<PendingApproval> merged = pendingLeave
            .Select(r => new PendingApproval(
                "leave", r.Id, r.WorkerId, r.SubmittedAtUtc!.Value,
                $"{r.WorkingDays:0.##} working days ({r.From:yyyy-MM-dd}–{r.To:yyyy-MM-dd})"))
            .Concat(pendingExpenses.Select(e => new PendingApproval(
                "expense", e.Id, e.WorkerId, e.SubmittedAtUtc!.Value,
                $"{e.Amount} · {e.Category}")));

        return
        [
            .. merged
                .OrderByDescending(p => p.SubmittedAtUtc)
                .ThenByDescending(p => p.Id)
                .Take(cap)
        ];
    }
}
