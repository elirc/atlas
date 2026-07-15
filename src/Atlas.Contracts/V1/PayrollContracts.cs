namespace Atlas.Contracts.V1;

// NOTE: MoneyDto (the decimal+currency wire twin of Money) is defined once in
// QuoteContracts.cs (S04) and reused here — money's wire shape is the same everywhere.

/// <summary>Body of POST /api/v1/payroll/cycles — open a schedule for a country + cadence.</summary>
public sealed record CreatePayCycleRequest(string? Country, string? Frequency);

/// <summary>A payroll schedule, as served by /api/v1/payroll/cycles.</summary>
public sealed record PayCycleDto(
    Guid Id,
    string Country,
    string Frequency,
    DateTimeOffset CreatedAtUtc);

/// <summary>Body of POST /api/v1/payroll/runs — prepare a run for a cycle's period.</summary>
public sealed record PreparePayrollRunRequest(Guid CycleId, int Year, int Month);

/// <summary>
/// A payroll run in the list/index shape: identity, period, state, timestamps, and the vendor
/// pinned at creation. No money totals and no payslips — those are on the detail view, so the
/// list stays cheap.
/// </summary>
// 📘 SelectedVendor is an ADDITIVE v1 change (S12), not a new version — ADR-0002 §1: "evolution
// is by addition, never a breaking change." An existing v1 consumer that ignores unknown fields
// keeps working untouched; a new one learns which engine computed the run. Adding a field is the
// cheap, in-place kind of evolution; the payslip's line-GROUPING change (which reshuffles an array
// a client may index) is the expensive kind, and THAT is what earned /api/v2 instead (ADR-0015).
// The two sit side by side in this sprint as the worked example of "when to add vs when to version."
public sealed record PayrollRunDto(
    Guid Id,
    Guid PayCycleId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string State,
    string SelectedVendor,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExecutedAtUtc);

/// <summary>One worker's payslip in summary — the amounts, not the line detail.</summary>
public sealed record PayslipSummaryDto(
    Guid Id,
    Guid WorkerId,
    MoneyDto Gross,
    MoneyDto Net,
    MoneyDto EmployerCost);

/// <summary>
/// GET /api/v1/payroll/runs/{id} — the run plus its computed totals and every worker's
/// payslip summary. Totals are null when the run has no payslips (no workers → no currency
/// to total in).
/// </summary>
public sealed record PayrollRunDetailDto(
    PayrollRunDto Run,
    MoneyDto? TotalGross,
    MoneyDto? TotalNet,
    MoneyDto? TotalEmployerCost,
    IReadOnlyList<PayslipSummaryDto> Payslips);

/// <summary>One line of a payslip: what it is, and how much.</summary>
public sealed record PayslipLineDto(string Label, string Kind, MoneyDto Amount);

/// <summary>GET /api/v1/payslips/{id} — a full payslip with every line (FLAT list; v2 groups them).</summary>
public sealed record PayslipDto(
    Guid Id,
    Guid RunId,
    Guid WorkerId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    MoneyDto Gross,
    IReadOnlyList<PayslipLineDto> Lines,
    MoneyDto Net,
    MoneyDto EmployerCost);
