using Atlas.Application.Approvals;
using Atlas.Contracts.V1;
using Atlas.Domain.Expenses;

namespace Atlas.Api.Mapping;

// WHY a third mapping file (beside DtoMapping and PayrollMapping): expenses + the approval inbox
// are a feature's worth of translations, kept together so the surface reads on its own. All three
// classes share the Atlas.Api.Mapping namespace, so `using Atlas.Api.Mapping;` brings every
// .ToDto() into scope — including Money.ToDto() from PayrollMapping, reused here for the amount.
internal static class ExpenseMapping
{
    public static ExpenseClaimDto ToDto(this ExpenseClaim claim) =>
        new(claim.Id,
            claim.WorkerId,
            claim.Amount.ToDto(),
            claim.Category.ToString(),
            claim.Status.ToString(),
            claim.ReceiptUrl,
            claim.ReceiptFilename,
            claim.Note,
            claim.RequestedByUserId,
            claim.DecidedByUserId,
            claim.DecidedAtUtc,
            claim.DecisionReason,
            claim.ReimbursedAtUtc,
            claim.CreatedAtUtc);

    public static ApprovalInboxItemDto ToDto(this PendingApproval item) =>
        new(item.Kind, item.Id, item.WorkerId, item.SubmittedAtUtc, item.Summary);
}
