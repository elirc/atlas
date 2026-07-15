using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Events;
using Atlas.Domain.Approvals;
using Atlas.Domain.Auditing;
using Atlas.Domain.Expenses;

namespace Atlas.Application.Expenses;

public sealed class CancelExpense(
    IExpenseClaimRepository claims,
    IEventSink events,
    IAuditTrail audit,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<ExpenseClaim> ExecuteAsync(Guid claimId, CancellationToken cancellationToken)
    {
        // WHY (v1): cancellation is an HR/admin act on behalf of the worker — same reason
        // submission is (no worker↔user link yet). The entity guards WHICH states can be
        // cancelled (submitted or approved, but not reimbursed); this use case guards WHO may ask.
        context.RequireRole(ApprovalWorkflow<ExpenseApprovalSubject>.ApproverRoles);
        Guid orgId = context.RequireOrg();

        ExpenseClaim claim = await claims.FindInOrgAsync(claimId, orgId, cancellationToken)
            ?? throw new NotFoundException("expense claim", claimId);

        DateTimeOffset now = clock.GetUtcNow();
        claim.Cancel();

        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "expense.cancelled", "expense_claim", claim.Id,
            JsonSerializer.Serialize(new { claim.WorkerId }),
            now));

        await events.PublishAsync(
            new AtlasEvent(
                "expense.cancelled", orgId, "expense_claim", claim.Id,
                JsonSerializer.Serialize(new { claim.WorkerId, CancelledAtUtc = now }),
                now),
            cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);

        return claim;
    }
}
