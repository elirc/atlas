using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Events;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Invoicing;

namespace Atlas.Application.Invoicing;

/// <summary>
/// One requested invoice line, in the words the API speaks it: a description, an amount, and the
/// currency the client claims it is in. The use case attaches the currency to a Money; the
/// aggregate then checks every line agrees with the contractor's payout currency.
/// </summary>
public sealed record NewInvoiceLine(string? Description, decimal Amount, string? Currency);

/// <summary>
/// Record a contractor's invoice. v1: submitted BY HR/admin ON BEHALF of the contractor — the
/// same deferral as leave (S06) and time (S06): contractors are not Atlas users yet, so there is
/// no contractor→user link to authorise self-service against, and inventing one "sort of" now
/// would bake in an identity mapping we have not designed. HR-on-behalf is the honest v1.
/// </summary>
public sealed class SubmitInvoice(
    IContractorRepository contractors,
    IContractorInvoiceRepository invoices,
    IAuditTrail audit,
    IEventSink events,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<ContractorInvoice> ExecuteAsync(
        Guid contractorId,
        IReadOnlyList<NewInvoiceLine> lines,
        CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        Contractor contractor = await contractors.FindInOrgAsync(contractorId, orgId, cancellationToken)
            ?? throw new NotFoundException("contractor", contractorId);

        if (lines is null || lines.Count == 0)
        {
            // WHY a 400 here (not the aggregate's 422): "you sent zero lines" is a malformed
            // request, caught before we build any domain object. The aggregate ALSO guards
            // emptiness — belt and braces — but the boundary rejects the shape first.
            throw new RequestValidationException("An invoice needs at least one line.");
        }

        // Attach each line's currency to a Money. The aggregate is the one that decides whether
        // those currencies are ALLOWED (all must equal the contractor's payout currency); here we
        // only turn primitives into value objects, failing as a 422 on a malformed currency code.
        var built = lines
            .Select(l => InvoiceLine.For(l.Description, Money.Of(l.Amount, CurrencyCode.From(l.Currency))))
            .ToList();

        DateTimeOffset now = clock.GetUtcNow();

        // LEARN: the persisted row is Submitted, but it travels through Draft first — the
        // Draft → Submit transition is REAL and exercised here, it just happens inside one use
        // case for v1. When contractor self-service lands, Draft becomes a persisted, editable
        // state the contractor owns before an HR manager ever sees it.
        var invoice = ContractorInvoice.Open(
            orgId, contractor.Id, contractor.PayoutCurrency, built, context.UserId, now);
        invoice.Submit(now);

        string details = JsonSerializer.Serialize(new
        {
            ContractorId = contractor.Id,
            LineCount = built.Count,
            Total = new { invoice.Total().Amount, Currency = invoice.Currency.Value },
        });

        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "invoice.submitted", "contractor_invoice", invoice.Id, details, now));

        await invoices.AddAsync(invoice, cancellationToken);

        // Announce, THEN commit — the publish-before-commit ordering the S10 outbox will make
        // load-bearing. Today the sink is a log line; the ORDER is the lesson in waiting.
        await events.PublishAsync(
            new AtlasEvent("invoice.submitted", orgId, "contractor_invoice", invoice.Id, details, now),
            cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);

        return invoice;
    }
}
