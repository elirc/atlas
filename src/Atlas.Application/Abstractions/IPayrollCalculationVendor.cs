using Atlas.Domain.Payroll;

namespace Atlas.Application.Abstractions;

/// <summary>
/// The seam between Atlas and whoever actually turns a worker's terms into a payslip. A PORT:
/// the application says "calculate this input" in its OWN vocabulary and never learns which
/// engine answered or in what dialect. Every implementation — our in-house calculator or an
/// external vendor's ACL — returns the same canonical <see cref="PayslipCalculation"/>.
/// </summary>
// 🔗 THE THIRD VENDOR PORT IN ATLAS, and the one the whole architecture was underwriting.
// IFxRates (ADR-0005) was the first, IPaymentProvider (ADR-0010) the second; by now the shape
// is muscle memory: a narrow interface in Application, a translating adapter in Infrastructure,
// a canonical result the domain already understands, and a hard rule that a vendor's types stop
// at its adapter. S12 extracts this port FROM the pure PayrollCalculator so a second engine
// (LedgerPay) can slot in without the prepare use case noticing. The pure calculator is
// untouched — AtlasNativeCalculator simply wraps it — so S07's golden files stay green,
// byte-for-byte, and that untouched-and-green golden file is the proof the extraction preserved
// behaviour (a refactor whose golden tests appear in the diff is not a refactor — ADR-0009).
//
// Contract every implementation holds to:
//   * It returns a canonical PayslipCalculation whose lines carry Atlas's PayslipLineKind — a
//     vendor's coarser or finer granularity is re-expressed in Atlas's terms INSIDE the adapter,
//     never leaked past it.
//   * The net identity holds: Net + Σ(withheld lines) == Gross (the invariant S07 pins).
//   * It THROWS only for what is not a business outcome — an unmapped vendor state (our map of
//     the vendor is stale: a loud 500 that pages someone), or an input the vendor's model cannot
//     price (a clean 422). It never leaks a vendor code, status string, or wire type.
public interface IPayrollCalculationVendor
{
    /// <summary>Which vendor this implementation IS — the key the directory indexes it under.</summary>
    PayrollVendor Vendor { get; }

    /// <summary>
    /// Turn one worker's terms, pack, period, and unpaid-leave days into a canonical payslip
    /// calculation. Async because a real vendor is a network call; the native implementation
    /// completes synchronously (it wraps the pure engine) and just hands back a finished task.
    /// </summary>
    Task<PayslipCalculation> CalculateAsync(PayrollCalculationInput input, CancellationToken cancellationToken);
}

/// <summary>
/// Resolves a <see cref="PayrollVendor"/> to the implementation that speaks for it. The
/// composition root registers every vendor; this directory indexes them by
/// <see cref="IPayrollCalculationVendor.Vendor"/> so a use case can turn the enum a run pinned
/// into the engine that computes it.
/// </summary>
// LEARN: a tiny lookup port instead of scattering keyed-DI or a switch through the use cases.
// The use cases speak PayrollVendor (a domain concept); the mapping from that enum to a concrete
// adapter is Infrastructure's job, behind this seam. A vendor enum with no registered
// implementation is a wiring bug, so Resolve FAILS LOUD (never returns null) — the same
// fail-at-first-touch stance CountryRulePacks takes for a missing pack.
public interface IPayrollVendorDirectory
{
    IPayrollCalculationVendor Resolve(PayrollVendor vendor);
}
