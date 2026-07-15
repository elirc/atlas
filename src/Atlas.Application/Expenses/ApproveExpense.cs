using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Events;
using Atlas.Domain.Approvals;
using Atlas.Domain.Auditing;
using Atlas.Domain.Expenses;

namespace Atlas.Application.Expenses;

/// <summary>
/// Approve a submitted expense claim. Unlike leave's approve there is no balance or overlap math —
/// the only rule is the entity's own guard (must be Submitted), which lives in the shared workflow.
/// </summary>
public sealed class ApproveExpense(
    IExpenseClaimRepository claims,
    IEventSink events,
    IAuditTrail audit,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<ExpenseClaim> ExecuteAsync(Guid claimId, CancellationToken cancellationToken)
    {
        // 🔗 The approver roles come from the SHARED component, not a local list — the same source
        // leave would use. Today it resolves to OrgAdmin/HrManager for every subject (the YAGNI
        // ledger explains why it is not yet a function of the subject).
        context.RequireRole(ApprovalWorkflow<ExpenseApprovalSubject>.ApproverRoles);
        Guid orgId = context.RequireOrg();

        ExpenseClaim claim = await claims.FindInOrgAsync(claimId, orgId, cancellationToken)
            ?? throw new NotFoundException("expense claim", claimId);

        DateTimeOffset now = clock.GetUtcNow();
        claim.Approve(context.UserId, now);

        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "expense.approved", "expense_claim", claim.Id,
            JsonSerializer.Serialize(new
            {
                claim.WorkerId,
                Amount = claim.Amount.Amount,
                Currency = claim.Amount.Currency.Value,
            }),
            now));

        await events.PublishAsync(
            new AtlasEvent(
                "expense.approved", orgId, "expense_claim", claim.Id,
                JsonSerializer.Serialize(new
                {
                    claim.WorkerId,
                    Amount = claim.Amount.Amount,
                    Currency = claim.Amount.Currency.Value,
                    DecidedByUserId = context.UserId,
                    DecidedAtUtc = now,
                }),
                now),
            cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);

        return claim;
    }
}
