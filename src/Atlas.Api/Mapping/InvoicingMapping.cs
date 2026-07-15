using Atlas.Application.Invoicing;
using Atlas.Contracts.V1;
using Atlas.Domain;
using Atlas.Domain.Invoicing;

namespace Atlas.Api.Mapping;

// WHY a private ToMoneyDto helper instead of the shared `money.ToDto()` extension: several
// mapping files already declare a `ToDto(this Money)` extension in this namespace, so calling the
// extension here would be ambiguous. A file-local, non-extension helper keeps this mapping
// self-contained and independent of which of those the integrator keeps. (See NOTES.md.)
internal static class InvoicingMapping
{
    public static ContractorDto ToDto(this Contractor contractor) =>
        new(
            contractor.Id,
            contractor.Name,
            contractor.Email.Value,
            contractor.Country.Value,
            contractor.PayoutCurrency.Value,
            contractor.CreatedAtUtc);

    public static ContractorInvoiceDto ToDto(this ContractorInvoice invoice) =>
        new(
            invoice.Id,
            invoice.ContractorId,
            invoice.Status.ToString(),
            [.. invoice.Lines.Select(l => new InvoiceLineDto(l.Description, ToMoneyDto(l.Amount)))],
            ToMoneyDto(invoice.Total()),
            invoice.VendorReference,
            invoice.FailureReason,
            invoice.CreatedAtUtc,
            invoice.SubmittedAtUtc,
            invoice.HrApprovedAtUtc,
            invoice.AdminApprovedAtUtc,
            invoice.PaidAtUtc);

    // The LIST projection: no lines, no Total() — the list repository does not load the line graph,
    // and Total() folds it. This is the lightweight index the endpoint hands back per page.
    public static ContractorInvoiceSummaryDto ToSummaryDto(this ContractorInvoice invoice) =>
        new(
            invoice.Id,
            invoice.ContractorId,
            invoice.Status.ToString(),
            invoice.VendorReference,
            invoice.CreatedAtUtc,
            invoice.SubmittedAtUtc,
            invoice.PaidAtUtc);

    public static InvoiceReconciliationDto ToDto(this InvoiceReconciliation reconciliation) =>
        new(reconciliation.Invoice.ToDto(), reconciliation.Settlement?.Status.ToString());

    private static MoneyDto ToMoneyDto(Money money) => new(money.Amount, money.Currency.Value);
}
