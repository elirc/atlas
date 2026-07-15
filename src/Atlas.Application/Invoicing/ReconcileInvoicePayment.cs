using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Payments;
using Atlas.Domain.Access;
using Atlas.Domain.Invoicing;

namespace Atlas.Application.Invoicing;

/// <summary>Our record of an invoice, next to the provider's current settlement of it.</summary>
// WHY surface BOTH and mutate NEITHER: reconciliation compares two books, it does not overwrite
// one with the other. If Atlas says Paid and the provider says Failed, that DISAGREEMENT is the
// output — a flag for a human — not a licence for this read to silently flip a Paid (immutable!)
// invoice. Automated correction, if we ever build it, is a deliberate write path with its own
// audit trail, not a side effect of looking.
public sealed record InvoiceReconciliation(ContractorInvoice Invoice, PaymentSettlement? Settlement);

/// <summary>
/// Reconcile one invoice against the payment provider: read our stored state, then (if the
/// invoice has a vendor reference) ask the provider where its settlement stands. PayGrid settles
/// asynchronously, so a Paid invoice can still be Pending at the vendor for a while — this is how
/// finance sees that gap without waiting for a webhook (webhooks arrive in S10).
/// </summary>
public sealed class ReconcileInvoicePayment(
    IContractorInvoiceRepository invoices,
    IPaymentProvider payments,
    ICurrentContext context)
{
    public async Task<InvoiceReconciliation> ExecuteAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        ContractorInvoice invoice = await invoices.FindInOrgAsync(invoiceId, orgId, cancellationToken)
            ?? throw new NotFoundException("invoice", invoiceId);

        // An invoice we never handed to the provider has nothing to reconcile against — return
        // our side only. (A never-paid or still-in-approval invoice has no vendor reference.)
        if (invoice.VendorReference is null)
        {
            return new InvoiceReconciliation(invoice, Settlement: null);
        }

        PaymentSettlement settlement = await payments.GetSettlementAsync(
            new VendorPaymentReference(invoice.VendorReference), cancellationToken);

        return new InvoiceReconciliation(invoice, settlement);
    }
}
