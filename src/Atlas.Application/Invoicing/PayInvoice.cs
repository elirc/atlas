using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Events;
using Atlas.Application.Payments;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Invoicing;

namespace Atlas.Application.Invoicing;

/// <summary>
/// The money-mover: hand an approved invoice to the payment provider and record what came back.
/// The provider is reached through <see cref="IPaymentProvider"/> — the anti-corruption layer —
/// so this use case speaks only Atlas's payment vocabulary and never a vendor's. The provider's
/// answer is a CLOSED <see cref="PaymentResult"/>; the switch below handles every member of it,
/// and the compiler guarantees there is no fifth case to forget.
/// </summary>
public sealed class PayInvoice(
    IContractorInvoiceRepository invoices,
    IContractorRepository contractors,
    IPaymentProvider payments,
    IAuditTrail audit,
    IEventSink events,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<ContractorInvoice> ExecuteAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        // WHY OrgAdmin only: paying is the sharpest authority in this feature.
        context.RequireRole(Role.OrgAdmin);
        Guid orgId = context.RequireOrg();

        ContractorInvoice invoice = await invoices.FindInOrgAsync(invoiceId, orgId, cancellationToken)
            ?? throw new NotFoundException("invoice", invoiceId);

        Contractor contractor = await contractors.FindInOrgAsync(invoice.ContractorId, orgId, cancellationToken)
            ?? throw new NotFoundException("contractor", invoice.ContractorId);

        // Only an approved (or previously-failed, i.e. retrying) invoice is payable. This mirrors
        // the domain's own transition guard; checking here lets us return a clear 422 before we
        // ever call the provider, rather than letting MarkPaid throw after the money conversation.
        if (invoice.Status is not (InvoiceStatus.Approved or InvoiceStatus.Failed))
        {
            throw new DomainRuleException(
                $"An invoice in {invoice.Status} cannot be paid; it must be approved first.");
        }

        DateTimeOffset now = clock.GetUtcNow();

        var instruction = new PaymentInstruction(invoice.Id, invoice.Total(), contractor.Name);
        PaymentResult result = await payments.PayAsync(instruction, cancellationToken);

        // LEARN: the whole value of the typed result is right here. Each branch is a decision the
        // business owns — record a payment, record a failure, surface a retryable outage — and
        // the vendor's dialect (which status string meant which) was already spent inside the
        // adapter. This use case cannot accidentally depend on a PayGrid code because it never
        // sees one.
        (string action, string detail) = result switch
        {
            PaymentResult.Succeeded s => MarkPaid(invoice, s.Reference, now),
            PaymentResult.AlreadyPaid a => MarkPaid(invoice, a.Reference, now),
            PaymentResult.PaymentRejected r => MarkFailed(invoice, r, now),

            // Transient: the invoice is untouched (still payable) and the caller is told to retry.
            // We do NOT write a failure — "we couldn't reach the vendor" is not "the payment
            // failed", and recording it as failure would lie to reconciliation.
            PaymentResult.VendorUnavailable u => throw new PaymentProviderUnavailableException(u.Reason),

            _ => throw new InvalidOperationException("Unhandled payment result."),
        };

        audit.Append(AuditEntry.Record(
            orgId, context.UserId, action, "contractor_invoice", invoice.Id, detail, now));

        await events.PublishAsync(
            new AtlasEvent(action, orgId, "contractor_invoice", invoice.Id, detail, now),
            cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);

        return invoice;
    }

    private static (string Action, string Detail) MarkPaid(
        ContractorInvoice invoice, VendorPaymentReference reference, DateTimeOffset now)
    {
        invoice.MarkPaid(reference.Value, now);
        return ("invoice.paid", JsonSerializer.Serialize(new
        {
            VendorReference = reference.Value,
            PaidAtUtc = now,
        }));
    }

    private static (string Action, string Detail) MarkFailed(
        ContractorInvoice invoice, PaymentResult.PaymentRejected rejection, DateTimeOffset now)
    {
        invoice.MarkPaymentFailed(rejection.Reason, now);
        return ("invoice.payment_failed", JsonSerializer.Serialize(new
        {
            rejection.Reason,
            AttemptedAtUtc = now,
        }));
    }
}
