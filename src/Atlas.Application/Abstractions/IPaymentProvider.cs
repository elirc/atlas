using Atlas.Application.Payments;

namespace Atlas.Application.Abstractions;

/// <summary>
/// The seam between Atlas and whoever actually moves money. A PORT: the application says "pay
/// this instruction" and "what happened to this reference", in its OWN vocabulary, and never
/// learns which vendor answered or in what dialect. The adapter behind it (the anti-corruption
/// layer) is the ONLY place that speaks the vendor's language.
/// </summary>
// 🔗 This is the SECOND vendor port in Atlas — IFxRates (ADR-0005, S04) was the first. Notice
// the pattern maturing: a narrow interface in Application, a translating adapter in
// Infrastructure, typed results the domain can act on, and a hard rule that vendor types stop
// at the adapter. When S12 swaps a payment vendor, the invoice use cases must not notice — the
// blast radius of a vendor change is exactly this interface's implementations, and no further.
//
// Contract both methods hold to:
//   * They return TYPED RESULTS for outcomes the caller can act on (accepted / declined /
//     already-paid / unreachable). They THROW only for the two things that are not outcomes:
//     UnmappedVendorStateException (our map of the vendor is stale — a 500, page someone) and,
//     via the use case, a transient-unavailable surfaced as a 503.
//   * They never leak a vendor type, code, or status string across this boundary.
public interface IPaymentProvider
{
    /// <summary>
    /// Ask the provider to disburse one invoice. Returns a closed <see cref="PaymentResult"/>.
    /// Throws <see cref="UnmappedVendorStateException"/> if the provider answers with a state the
    /// adapter cannot translate.
    /// </summary>
    Task<PaymentResult> PayAsync(PaymentInstruction instruction, CancellationToken cancellationToken);

    /// <summary>
    /// Ask the provider where a prior disbursement stands (async settlement reconciliation).
    /// Returns the settled/pending/failed fact. Throws <see cref="UnmappedVendorStateException"/>
    /// on a state the adapter cannot translate. A read has no closed decision set to fold an
    /// "unavailable" into, so a transient fault surfaces here as
    /// <see cref="Errors.PaymentProviderUnavailableException"/> (retryable) rather than a result
    /// member — the deliberate action-vs-read asymmetry noted in ADR-0010.
    /// </summary>
    Task<PaymentSettlement> GetSettlementAsync(VendorPaymentReference reference, CancellationToken cancellationToken);
}
