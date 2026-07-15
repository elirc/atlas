namespace Atlas.Domain.Payroll;

/// <summary>
/// One worker's vendor-vs-vendor difference, expressed ONLY as deltas in minor units —
/// never as either vendor's absolute figures. This record is where net-pay redaction is
/// enforced by construction: you cannot serialize a salary you were never handed.
/// </summary>
public readonly record struct VendorDelta(
    Guid WorkerId,
    long GrossDeltaMinor,
    long NetDeltaMinor,
    long EmployerCostDeltaMinor,
    string Currency);

/// <summary>
/// Pure comparison of a stored (primary) payslip against a freshly-computed shadow
/// calculation for the same worker. The one function in Atlas that ever holds both vendors'
/// absolute net-pay at once — and it hands back deltas, so nothing downstream can.
/// </summary>
// 🔍 REDACTION IS A BOUNDARY, AND THIS IS THE BOUNDARY (the Reviewer's second S12 win,
// ADR-0014). Finance needs to know WHERE two vendors disagree and BY HOW MUCH; it does not
// need a second, duplicate copy of everyone's take-home pay sitting in a comparison table
// waiting to leak. So the absolute Gross/Net/EmployerCost of both vendors are consumed HERE,
// converted to signed minor-unit deltas, and discarded. A delta of -69,395 minor units
// ("LedgerPay pays €693.95 less than we do for this worker") is exactly the actionable
// signal, and it reveals nothing about how much the worker earns. The comparison report and
// its DTO carry only VendorDeltas; there is no field on either that could hold a net figure,
// which is what makes "we do not store duplicate sensitive results" a structural guarantee
// rather than a promise in a code review.
public static class VendorComparison
{
    private const decimal MinorUnitsPerUnit = 100m;

    /// <summary>
    /// Delta of <paramref name="shadow"/> minus <paramref name="primary"/> for one worker.
    /// Positive means the shadow vendor reports MORE; negative, less.
    /// </summary>
    public static VendorDelta DeltaOf(Payslip primary, PayslipCalculation shadow)
    {
        // A cross-currency comparison is meaningless and is a programmer error (the shadow
        // vendor was asked to price the same worker in the same country) — refuse it loudly
        // rather than subtract euros from pesos, mirroring Money's own stance.
        if (primary.Gross.Currency != shadow.Gross.Currency)
        {
            throw new DomainRuleException(
                $"Cannot compare a {primary.Gross.Currency} payslip against a {shadow.Gross.Currency} calculation.");
        }

        return new VendorDelta(
            primary.WorkerId,
            ToMinor(shadow.Gross) - ToMinor(primary.Gross),
            ToMinor(shadow.Net) - ToMinor(primary.Net),
            ToMinor(shadow.EmployerCost) - ToMinor(primary.EmployerCost),
            primary.Gross.Currency.Value);
    }

    // GOTCHA: minor units, not decimals. Deltas are the ONLY numbers the comparison persists,
    // and keeping them as whole-cent longs (INTEGER on SQLite) sidesteps the decimal-can't-be-
    // SUM'd trap the payroll totals had to fold around — a summary count of divergent workers
    // is a plain integer aggregate here, safely. Scale is fixed at 2 (Money.Scale); banker's
    // rounding matches Money's own boundary conversion so a delta of zero really is zero.
    private static long ToMinor(Money money) =>
        (long)decimal.Round(money.Amount * MinorUnitsPerUnit, 0, MidpointRounding.ToEven);
}
