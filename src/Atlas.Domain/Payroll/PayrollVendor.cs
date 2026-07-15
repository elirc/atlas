namespace Atlas.Domain.Payroll;

/// <summary>
/// Which calculation vendor produced (or would produce) a payslip. A first-class business
/// fact, not an implementation detail — a run RECORDS the vendor it was computed with, so
/// "who calculated this payroll" survives every future adapter change. Persisted as text
/// (see PayrollConfigurations), like every other state enum in Atlas (ADR-0006).
/// </summary>
// 📘 THE PIVOT MADE VISIBLE. Before S12 there was exactly one way to turn terms into a
// payslip — the pure PayrollCalculator — so "which vendor" was not a question anyone could
// ask. S12 signs a second vendor (LedgerPay), and the moment there are two, the choice must
// be NAMED, STORED, and AUDITABLE. This enum is that name. It lives in the Domain (not
// Infrastructure) on purpose: a payslip's provenance is a business fact a regulator can ask
// about, not a wiring concern that dies at the adapter. The adapters themselves (which HTTP
// call, which dialect) stay in Infrastructure; only the *identity* of the vendor is here.
public enum PayrollVendor
{
    /// <summary>
    /// Atlas's own in-house gross-to-net engine (the pure PayrollCalculator, S07). The
    /// default vendor for every org+country until an effective-dated switch says otherwise,
    /// and the first implementation of <c>IPayrollCalculationVendor</c>.
    /// </summary>
    AtlasNative = 1,

    /// <summary>
    /// LedgerPay — the second, external vendor signed in S12. Reached only through its
    /// anti-corruption layer (LedgerPayCalculator); its cents-and-bundles dialect stops at
    /// that adapter, exactly as PayGrid's did for payments (ADR-0010, ADR-0014).
    /// </summary>
    LedgerPay = 2,
}
