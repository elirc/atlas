using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Events;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Invoicing;

namespace Atlas.Application.Invoicing;

/// <summary>
/// The FIRST of two approvals: an HR manager checks a submitted invoice's amounts. The order is
/// enforced by the state machine, not by this use case — HrApprove is only legal from Submitted,
/// so "HR-approve an already-admin-approved invoice" is a clean 422 from the domain.
/// </summary>
public sealed class HrApproveInvoice(
    IContractorInvoiceRepository invoices,
    IAuditTrail audit,
    IEventSink events,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<ContractorInvoice> ExecuteAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        // WHY OrgAdmin is allowed on the HR step: an admin outranks HR everywhere else in Atlas,
        // so barring them here would be a surprise. True segregation of duties (the second
        // approver MUST differ from the first) is recorded as future work — we already capture
        // both approver ids, so the guard is a later refinement, not a new idea (see NOTES).
        context.RequireRole(Role.HrManager, Role.OrgAdmin);
        Guid orgId = context.RequireOrg();

        ContractorInvoice invoice = await invoices.FindInOrgAsync(invoiceId, orgId, cancellationToken)
            ?? throw new NotFoundException("invoice", invoiceId);

        DateTimeOffset now = clock.GetUtcNow();
        invoice.HrApprove(context.UserId, now);

        string details = JsonSerializer.Serialize(new { ApproverUserId = context.UserId });

        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "invoice.hr_approved", "contractor_invoice", invoice.Id, details, now));

        await events.PublishAsync(
            new AtlasEvent("invoice.hr_approved", orgId, "contractor_invoice", invoice.Id, details, now),
            cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);

        return invoice;
    }
}
