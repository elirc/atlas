using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Access;
using Atlas.Domain.Invoicing;

namespace Atlas.Application.Invoicing;

/// <summary>One invoice (with its lines) in the caller's org, or a 404 — including for a foreign one.</summary>
public sealed class GetInvoice(IContractorInvoiceRepository invoices, ICurrentContext context)
{
    public async Task<ContractorInvoice> ExecuteAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        // WHY the org filter is in the query, not a post-hoc check: an invoice in another org
        // reads as 404, never 403 — existence across tenants is need-to-know (multi-tenancy).
        return await invoices.FindInOrgAsync(invoiceId, orgId, cancellationToken)
            ?? throw new NotFoundException("invoice", invoiceId);
    }
}
