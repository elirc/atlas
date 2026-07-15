using Atlas.Application.Abstractions;
using Atlas.Domain.Access;
using Atlas.Domain.Invoicing;

namespace Atlas.Application.Contractors;

/// <summary>Every contractor in the caller's org, newest first. HR/admin only.</summary>
public sealed class ListContractors(IContractorRepository contractors, ICurrentContext context)
{
    public async Task<IReadOnlyList<Contractor>> ExecuteAsync(CancellationToken cancellationToken)
    {
        // WHY a simple list, not a cursor page: contractors are reference-scale (a handful per
        // org), like pay cycles and leave policies. The timeline-cursor discipline is reserved
        // for the unbounded lists (invoices, audit); over-paginating a small set is ceremony.
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        return await contractors.ListForOrgAsync(orgId, cancellationToken);
    }
}
