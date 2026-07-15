namespace Atlas.Contracts.V1;

// NOTE: MoneyDto is NOT declared here — it already exists in the V1 contracts (introduced with
// quotes in S04, reused by payroll in S07). Money is one wire shape across the whole API; a
// third declaration would be a duplicate-type compile error. This file reuses it. (See NOTES.md.)

/// <summary>Body of POST /api/v1/contractors.</summary>
// WHY every field is nullable: each absence is a VALID request shape the domain rejects with a
// useful 422 (missing name, malformed email, unsupported country), never a deserialization 500.
public sealed record CreateContractorRequest(string? Name, string? Email, string? Country, string? PayoutCurrency);

/// <summary>A contractor, as served by /api/v1/contractors.</summary>
public sealed record ContractorDto(
    Guid Id,
    string Name,
    string Email,
    string Country,
    string PayoutCurrency,
    DateTimeOffset CreatedAtUtc);

/// <summary>One line of a submitted invoice. Currency is per-line so a mixed-currency invoice is
/// EXPRESSIBLE — and therefore rejectable by the domain, rather than impossible to test.</summary>
public sealed record InvoiceLineInput(string? Description, decimal Amount, string? Currency);

/// <summary>Body of POST /api/v1/invoices — submit an invoice for a contractor (HR/admin, v1).</summary>
public sealed record SubmitInvoiceRequest(Guid ContractorId, IReadOnlyList<InvoiceLineInput> Lines);

/// <summary>One priced line of an invoice, on the way out.</summary>
public sealed record InvoiceLineDto(string Description, MoneyDto Amount);

/// <summary>A contractor invoice, as served by /api/v1/invoices.</summary>
public sealed record ContractorInvoiceDto(
    Guid Id,
    Guid ContractorId,
    string Status,
    IReadOnlyList<InvoiceLineDto> Lines,
    MoneyDto Total,
    string? VendorReference,
    string? FailureReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset? HrApprovedAtUtc,
    DateTimeOffset? AdminApprovedAtUtc,
    DateTimeOffset? PaidAtUtc);

/// <summary>
/// A contractor invoice as served by the LIST — /api/v1/invoices. Deliberately lighter than
/// <see cref="ContractorInvoiceDto"/>: no lines and no Total, because the list query is a keyset
/// index that does not load the line graph (loading it would be an N+1 across the page, and
/// folding an un-loaded collection for Total() would throw). The status filter makes this the
/// finance reconciliation view; the vendor reference is what a Paid row is reconciled against.
/// Fetch GET /api/v1/invoices/{id} for the full lines-and-total detail. Mirrors the
/// PayslipSummaryDto vs Payslip detail split from S07.
/// </summary>
public sealed record ContractorInvoiceSummaryDto(
    Guid Id,
    Guid ContractorId,
    string Status,
    string? VendorReference,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset? PaidAtUtc);

/// <summary>
/// GET /api/v1/invoices/{id}/reconciliation — our record of the invoice, plus the provider's
/// current settlement of it (null when the invoice was never handed to a provider). Settlement is
/// a status string (Pending/Settled/Failed) — the provider-neutral word, never a vendor code.
/// </summary>
public sealed record InvoiceReconciliationDto(ContractorInvoiceDto Invoice, string? Settlement);
