using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Events;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Invoicing;

namespace Atlas.Application.Invoicing;

/// <summary>
/// The SECOND approval: an org admin authorises the money. Legal only from HrApproved, so this
/// step can never run before HR has signed off — the two-step order is a property of the
/// transition table, not of anyone remembering to check.
/// </summary>
public sealed class AdminApproveInvoice(
    IContractorInvoiceRepository invoices,
    IAuditTrail audit,
    IEventSink events,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<ContractorInvoice> ExecuteAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        // WHY OrgAdmin only: this is the sign-off that makes the invoice payable. Authorising a
        // disbursement is an admin act; HR gets it to Approved's doorstep, an admin opens the door.
        context.RequireRole(Role.OrgAdmin);
        Guid orgId = context.RequireOrg();

        ContractorInvoice invoice = await invoices.FindInOrgAsync(invoiceId, orgId, cancellationToken)
            ?? throw new NotFoundException("invoice", invoiceId);

        DateTimeOffset now = clock.GetUtcNow();
        invoice.AdminApprove(context.UserId, now);

        string details = JsonSerializer.Serialize(new { ApproverUserId = context.UserId });

        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "invoice.admin_approved", "contractor_invoice", invoice.Id, details, now));

        await events.PublishAsync(
            new AtlasEvent("invoice.approved", orgId, "contractor_invoice", invoice.Id, details, now),
            cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);

        return invoice;
    }
}
