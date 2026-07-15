using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Access;
using Atlas.Domain.Expenses;

namespace Atlas.Application.Expenses;

public sealed class GetExpense(IExpenseClaimRepository claims, ICurrentContext context)
{
    public async Task<ExpenseClaim> ExecuteAsync(Guid claimId, CancellationToken cancellationToken)
    {
        // WHY HR/admin: expense claims name workers and amounts — same reading audience as leave
        // requests and the audit log. A cross-org id is a 404 (the claim does not exist for you),
        // never a 403 that leaks its existence.
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        return await claims.FindInOrgAsync(claimId, orgId, cancellationToken)
            ?? throw new NotFoundException("expense claim", claimId);
    }
}
