using Atlas.Application.Abstractions;
using Atlas.Domain.Payroll;

namespace Atlas.Infrastructure.Vendors.AtlasNative;

/// <summary>
/// Atlas's own gross-to-net engine, presented as the first <see cref="IPayrollCalculationVendor"/>.
/// A one-line adapter: it delegates straight to the pure <see cref="PayrollCalculator"/> and hands
/// back a finished task. There is NO translation, because there is no dialect — the native engine
/// already speaks Atlas's language; it IS Atlas's language.
/// </summary>
// 📘 THE EXTRACTION'S PROOF. The whole S12 vendor abstraction had to be introduced WITHOUT
// changing a single number the in-house engine produces — otherwise "we added a seam" would
// really mean "we rewrote payroll." So the native path stayed the pure static PayrollCalculator,
// untouched, and this adapter is the thinnest possible skin over it: same input, same output, one
// await. S07's PayrollCalculatorGoldenTests never moved and never went red; that green, unmodified
// golden file is the receipt that the port extraction was behaviour-preserving (ADR-0009's rule:
// a refactor whose characterization tests appear in the diff is not a refactor).
//
// WHY sync work behind an async port: the port is async because a REAL vendor is a network call
// (LedgerPay is), and one uniform signature keeps the prepare loop and the shadow runner from
// caring which vendor answered. The native engine is pure and synchronous, so it computes inline
// and returns Task.FromResult — no fake await, no thread hop, no lie about doing I/O.
internal sealed class AtlasNativeCalculator : IPayrollCalculationVendor
{
    public PayrollVendor Vendor => PayrollVendor.AtlasNative;

    public Task<PayslipCalculation> CalculateAsync(
        PayrollCalculationInput input, CancellationToken cancellationToken) =>
        Task.FromResult(PayrollCalculator.Calculate(input));
}
