namespace Atlas.Contracts.V1;

// ── Vendor selection: effective-dated per org + country (S12) ───────────────────────────────────

/// <summary>Body of POST /api/v1/payroll/vendor-assignments — schedule a vendor for a country from a day.</summary>
// NOTE: Vendor is a string (e.g. "AtlasNative", "LedgerPay") on the wire, parsed to the PayrollVendor
// enum at the edge — an unknown name is a clean 422, not a 500. EffectiveFrom is a DateOnly: a vendor
// switch is a calendar decision ("LedgerPay from 1 August"), never an instant (ADR-0002 §6).
public sealed record AssignPayrollVendorRequest(string? Country, string? Vendor, DateOnly EffectiveFrom);

/// <summary>One effective-dated vendor assignment, as served under /api/v1/payroll/vendor-assignments.</summary>
public sealed record VendorAssignmentDto(
    Guid Id,
    string Country,
    string Vendor,
    DateOnly EffectiveFrom,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// GET /api/v1/payroll/vendor-assignments?country=DEU — the switch history (newest effective first)
/// plus the vendor governing TODAY (resolved from the same history the prepare path pins from).
/// </summary>
public sealed record VendorHistoryDto(
    string Country,
    string Current,
    IReadOnlyList<VendorAssignmentDto> Assignments);

// ── Shadow-run comparison: DELTAS ONLY (net pay redacted at the domain boundary — ADR-0014) ──────

/// <summary>
/// One worker's line in a comparison report. Deltas are signed minor units of the deltas' currency;
/// a positive net delta means the shadow vendor pays MORE than the run's vendor. There is DELIBERATELY
/// no absolute-amount field here — the report never carries a second copy of anyone's take-home pay.
/// </summary>
public sealed record VendorComparisonLineDto(
    Guid WorkerId,
    long GrossDeltaMinor,
    long NetDeltaMinor,
    long EmployerCostDeltaMinor,
    string Currency,
    bool Divergent);

/// <summary>
/// GET /api/v1/payroll/runs/{id}/shadow-comparison (and the POST that creates it) — which two vendors
/// were compared, the tolerance that judged them, the summary, and one redacted line per worker.
/// </summary>
// 🔍 Read this DTO as the proof of the redaction win: the fields a finance team needs to decide
// "is LedgerPay safe to flip to?" are all here — how many workers diverged, by how much at worst —
// and NONE of them is a salary. The sensitive absolutes were spent computing deltas inside the
// domain (VendorComparison.DeltaOf) and never crossed into anything serialisable.
public sealed record VendorComparisonReportDto(
    Guid RunId,
    string Country,
    string PrimaryVendor,
    string ShadowVendor,
    int ToleranceMinorUnits,
    int WorkerCount,
    int DivergentCount,
    long MaxAbsNetDeltaMinor,
    bool IsClean,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<VendorComparisonLineDto> Lines);
