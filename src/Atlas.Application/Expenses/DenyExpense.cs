using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Events;
using Atlas.Domain.Approvals;
using Atlas.Domain.Auditing;
using Atlas.Domain.Expenses;

namespace Atlas.Application.Expenses;

/// <summary>
/// Deny a submitted expense claim, optionally recording WHY. The reason is the one field the
/// extracted <see cref="ApprovalDecision"/> record carries that leave never used — here it earns
/// its keep.
/// </summary>
public sealed class DenyExpense(
    IExpenseClaimRepository claims,
    IEventSink events,
    IAuditTrail audit,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<ExpenseClaim> ExecuteAsync(
        Guid claimId, string? reason, CancellationToken cancellationToken)
    {
        context.RequireRole(ApprovalWorkflow<ExpenseApprovalSubject>.ApproverRoles);
        Guid orgId = context.RequireOrg();

        ExpenseClaim claim = await claims.FindInOrgAsync(claimId, orgId, cancellationToken)
            ?? throw new NotFoundException("expense claim", claimId);

        DateTimeOffset now = clock.GetUtcNow();
        claim.Deny(context.UserId, now, reason);

        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "expense.denied", "expense_claim", claim.Id,
            JsonSerializer.Serialize(new { claim.WorkerId, claim.DecisionReason }),
            now));

        await events.PublishAsync(
            new AtlasEvent(
                "expense.denied", orgId, "expense_claim", claim.Id,
                JsonSerializer.Serialize(new
                {
                    claim.WorkerId,
                    DecidedByUserId = context.UserId,
                    DecidedAtUtc = now,
                    claim.DecisionReason,
                }),
                now),
            cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);

        return claim;
    }
}
