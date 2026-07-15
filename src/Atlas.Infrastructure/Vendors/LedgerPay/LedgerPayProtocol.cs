namespace Atlas.Infrastructure.Vendors.LedgerPay;

// ─────────────────────────────────────────────────────────────────────────────────────────────
// LedgerPay's wire types. Everything in this file speaks LedgerPay's dialect — integer minor
// units, half-AWAY-from-zero rounding, coarse bundled line codes, an open code vocabulary. NONE
// of it may escape the adapter (LedgerPayCalculator). If a LedgerPay type shows up in Application
// or Domain, the ACL has sprung a leak — the exact failure PayGrid's ACL was built to prevent
// (ADR-0010), applied a second time to a calculation vendor (ADR-0014).
// ─────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Body of LedgerPay's "calculate period" call. Note the dialect: the annual gross is an INTEGER
/// number of MINOR units (LedgerPay, like most vendors, has no decimal on the wire), the currency
/// and country are bare ISO strings, and the period is described by an ordinal + a divisor rather
/// than dates. The adapter fills every field from Atlas types, once, at the boundary.
/// </summary>
public sealed record LedgerPayCalcRequest(
    string EmployeeRef,
    string IsoCountry,
    string IsoCurrency,
    long AnnualGrossMinor,
    int PeriodOrdinal,
    int PeriodsInYear,
    int UnpaidDays);

/// <summary>
/// One line as LedgerPay reports it: an open-vocabulary CODE and an amount in minor units.
/// LedgerPay bundles where Atlas itemises — its whole employee-statutory withholding is one
/// <c>EE_STATUTORY</c> line, its whole employer on-cost one <c>ER_ONCOST</c> line — which is the
/// "different line granularity" the stakeholder warned about. The adapter re-expresses these in
/// Atlas's per-kind vocabulary; a code it has never been taught is a hole in our model of LedgerPay
/// and FAILS LOUD (never a guessed mapping).
/// </summary>
public sealed record LedgerPayLine(string Code, long AmountMinor);

/// <summary>
/// LedgerPay's calculation response: the coarse lines, in its own minor units and currency. There
/// is a <c>NET</c> line, but the adapter DERIVES net from gross minus the withheld lines rather
/// than trusting it — a vendor's self-reported total is a claim to verify, not a fact to store.
/// </summary>
public sealed record LedgerPayCalcResult(string EmployeeRef, string IsoCurrency, IReadOnlyList<LedgerPayLine> Lines);

/// <summary>
/// LedgerPay's business-level failure, carrying a stringly-typed reason CODE from an open,
/// versioned-without-notice vocabulary (<c>E_COUNTRY_UNSUPPORTED</c>, <c>E_BAD_REQUEST</c>, …).
/// Distinct from a transport error: this is "LedgerPay answered, and the answer was no."
/// </summary>
public sealed class LedgerPayException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Deterministic knobs for the in-process LedgerPay mock — its per-country rate model, its rounding
/// dialect, and which countries it refuses. A real LedgerPay hides these behind an API; the mock
/// turns them into knobs so the adapter's tests can pin the dialect (and the DIVERGENCES from
/// Atlas) instead of flaking on a live black box.
/// </summary>
// 📘 WHY THE MOCK HAS ITS OWN RATE TABLE (and why it is deliberately cruder than ours): a real
// third-party payroll vendor has its OWN model of each country's statutory rules — and that model
// does not agree with yours in every corner. LedgerPay's model here is a FLAT aggregate rate that
// IGNORES the wage ceilings Atlas honours. For a worker below every cap the two engines agree to
// the cent; for a high earner above a cap LedgerPay over-withholds, and the two DIVERGE. That gap
// is not a bug in the mock — it is the entire reason shadow mode exists: to surface exactly where a
// vendor's model and yours disagree, in cents, before a worker's pay depends on the difference.
public sealed record LedgerPaySettings
{
    /// <summary>
    /// Per ISO country: (aggregate employee-statutory rate on gross, aggregate employer on-cost
    /// rate on gross). Flat, cap-free — LedgerPay's simplified model. Countries absent here are
    /// UNSUPPORTED and raise <see cref="LedgerPayException"/> with <c>E_COUNTRY_UNSUPPORTED</c>.
    /// </summary>
    public IReadOnlyDictionary<string, (decimal EmployeeRate, decimal EmployerRate)> CountryRates { get; init; } =
        new Dictionary<string, (decimal, decimal)>(StringComparer.Ordinal)
        {
            // DEU: 9.3 + 7.3 + 1.3 + 1.7 = 19.6% employee; employer mirrors = 19.6%. Cap-free, so a
            // €60k worker (below every ceiling) matches Atlas to the cent, and a €120k worker does not.
            ["DEU"] = (0.196m, 0.196m),
            // PHL: 4.5 + 2.5 + 2.0 = 9.0% employee; 9.5 + 2.5 + 2.0 = 14.0% employer. Cap-free, so a
            // capped earner diverges from Atlas (which honours the SSS/Pag-IBIG ceilings).
            ["PHL"] = (0.09m, 0.14m),
        };

    /// <summary>LedgerPay's working-days-per-year for the unpaid-leave daily rate (matches Atlas's 260).</summary>
    public decimal WorkingDaysPerYear { get; init; } = 260m;

    /// <summary>
    /// Test knob: when set, the mock appends a line with this (deliberately unknown) code, so the
    /// adapter's fail-loud path — an unmapped LedgerPay line code — can be driven deterministically.
    /// A real LedgerPay would slip a new code into a response without a changelog; this reproduces it.
    /// </summary>
    public string? UnknownLineCode { get; init; }

    /// <summary>App default: the two countries LedgerPay was signed for, half-away-from-zero rounding.</summary>
    public static LedgerPaySettings Default { get; } = new();
}
