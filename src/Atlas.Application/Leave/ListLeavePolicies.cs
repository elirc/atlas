using Atlas.Application.Abstractions;
using Atlas.Domain.Leave;

namespace Atlas.Application.Leave;

public sealed class ListLeavePolicies(ILeavePolicyRepository policies, ICurrentContext context)
{
    public async Task<IReadOnlyList<LeavePolicy>> ExecuteAsync(CancellationToken cancellationToken)
    {
        // WHY: policies are org configuration and drive the balances employees can see —
        // any org member may read them. No cursor: the set is bounded by supported
        // countries (a handful), so a plain list is honest, not a scaling trap.
        Guid orgId = context.RequireOrg();
        return await policies.ListForOrgAsync(orgId, cancellationToken);
    }
}
