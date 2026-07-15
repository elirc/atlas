using Atlas.Application.Abstractions;
using Atlas.Domain.Payroll;

namespace Atlas.Application.Payroll;

/// <summary>
/// One org's pay cycles, newest first. Org-scoped: the org id comes from context, never
/// the query string. Not paginated — an org has one cycle per country, a handful at most.
/// </summary>
public sealed class ListPayCycles(IPayCycleRepository cycles, ICurrentContext context)
{
    public async Task<IReadOnlyList<PayCycle>> ExecuteAsync(CancellationToken cancellationToken)
    {
        context.RequireUser();
        Guid orgId = context.RequireOrg();

        return await cycles.ListForOrgAsync(orgId, cancellationToken);
    }
}
