using Atlas.Contracts.V1;

namespace Atlas.Contracts.V2;

// ─────────────────────────────────────────────────────────────────────────────────────────────
// API v2 of the payslip resource (S12). The SHAPE change that motivated versioning: v1 serves a
// FLAT list of lines, each tagged with a Kind string; v2 GROUPS the lines by kind into named
// sections and drops the now-redundant per-line Kind. This is a breaking change to the payslip
// body — a client that indexed `lines[3]` would break — so it earns a new VERSION, not an in-place
// edit (ADR-0002 §1, ADR-0015). v1 keeps working, served from the SAME domain data by a flattening
// adapter (not a fork); see PayslipV2Mapping and the byte-for-byte contract test.
//
// NOTE: MoneyDto is reused from Atlas.Contracts.V1 — money's wire shape does not change across API
// versions (a decimal amount + a currency string is a stable primitive). Only the payslip's line
// STRUCTURE evolved, so only the payslip types are re-issued under V2.
// ─────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>One line of a v2 payslip: what it is and how much. No Kind — the GROUP it sits in carries that.</summary>
public sealed record PayslipLineV2Dto(string Label, MoneyDto Amount);

/// <summary>
/// A payslip's lines, grouped by kind. This grouping is the whole reason v2 exists: it is stable
/// across vendors (a LedgerPay payslip has one bundled EmployeeDeductions line where a native one has
/// four, but both present the SAME three groups), and it removes the parallel-array coupling of a
/// flat Kind-tagged list.
/// </summary>
public sealed record PayslipLineGroupsDto(
    IReadOnlyList<PayslipLineV2Dto> EmployeeDeductions,
    IReadOnlyList<PayslipLineV2Dto> UnpaidLeave,
    IReadOnlyList<PayslipLineV2Dto> EmployerBurden);

/// <summary>
/// GET /api/v2/payslips/{id} — a full payslip with lines grouped by kind. Gross/Net/EmployerCost
/// are unchanged from v1; ONLY the line structure evolved (a flat Kind-tagged list → named groups).
/// </summary>
// LEARN: v2 does NOT add a Vendor field, though the pivot might tempt it. Which vendor computed a
// run lives on the RUN (PayrollRunDto.SelectedVendor), not duplicated onto every payslip — adding it
// here would force a provenance column onto the Payslip entity and ripple through its factory,
// mapping, migration, and golden tests, for a fact one hop away. Keeping v2 to the grouping change
// alone is the locality-preserving choice, and it keeps the versioning example clean: v2 exists for
// the ONE breaking reshape, nothing more.
public sealed record PayslipV2Dto(
    Guid Id,
    Guid RunId,
    Guid WorkerId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    MoneyDto Gross,
    PayslipLineGroupsDto Lines,
    MoneyDto Net,
    MoneyDto EmployerCost);
