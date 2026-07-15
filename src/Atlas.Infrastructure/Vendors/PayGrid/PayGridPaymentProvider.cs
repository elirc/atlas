using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Payments;
using Atlas.Domain;

namespace Atlas.Infrastructure.Vendors.PayGrid;

/// <summary>
/// The anti-corruption layer for payments: the ONE place that speaks PayGrid. It translates
/// Atlas's <see cref="PaymentInstruction"/> into PayGrid's cents-and-strings dialect, and
/// PayGrid's stringly-typed answers back into the closed <see cref="PaymentResult"/> /
/// <see cref="PaymentSettlement"/> vocabulary the application reasons in. Above this class, the
/// word "PayGrid" never appears; below it, the words "invoice" and "domain" never do.
/// </summary>
// 📘 This adapter is the ubiquitous-language border post. Everything a vendor swap (S12) touches
// lives here: the cents conversion, the status table, the transport-error handling. The invoice
// use cases import none of it. That is the trade the ACL buys — one translating file, so that
// "which vendor and what its codes mean" is a fact known in exactly one place instead of smeared
// across every use case that moves money. (ADR-0010; the debate in the PR defends the price.)
// NOTE: public (not internal) so the adversarial adapter tests can drive it directly as an
// executable spec of PayGrid's dialect — the debate's agreed price of skipping the ACL's ceremony.
// Publicness does not breach the architecture rule: that rule forbids the Api *production*
// assembly from touching Infrastructure types outside Composition; test assemblies are exempt.
public sealed class PayGridPaymentProvider(PayGridClient payGrid) : IPaymentProvider
{
    public Task<PaymentResult> PayAsync(PaymentInstruction instruction, CancellationToken cancellationToken)
    {
        var request = new PayGridChargeRequest(
            ToCents(instruction.Amount),
            instruction.Amount.Currency.Value,
            instruction.Memo);

        try
        {
            PayGridCharge charge = payGrid.CreateCharge(request);
            return Task.FromResult(TranslateChargeStatus(charge));
        }
        catch (PayGridTransportException ex)
        {
            // A transport failure (429/5xx) is "we never got an answer" — a TYPED, transient
            // result the caller can act on, not an exception thrown across the boundary. The
            // vendor's status code becomes provider-neutral prose here and goes no further.
            return Task.FromResult<PaymentResult>(new PaymentResult.VendorUnavailable(Describe(ex)));
        }
    }

    public Task<PaymentSettlement> GetSettlementAsync(
        VendorPaymentReference reference, CancellationToken cancellationToken)
    {
        try
        {
            PayGridCharge charge = payGrid.GetCharge(reference.Value);
            return Task.FromResult(new PaymentSettlement(reference, TranslateSettlementStatus(charge.Status)));
        }
        catch (PayGridTransportException ex)
        {
            // A read has no closed result set to fold "unavailable" into (see IPaymentProvider),
            // so a transient fault surfaces as the retryable application exception.
            throw new PaymentProviderUnavailableException(Describe(ex));
        }
    }

    // ── PayGrid dialect → Atlas vocabulary ─────────────────────────────────────────────────────

    /// <summary>Translate a create-charge status into the closed <see cref="PaymentResult"/> set.</summary>
    // 🔍 This method IS the dialect table, and the adapter tests read it as an executable spec.
    // Two rules encoded here that a naive translation gets wrong:
    //   * Match the STATUS FAMILY by prefix, not the exact string — "OK_PENDING_2" and the yet-
    //     unseen "OK_PENDING_9" are the same acceptance; equality matching would fail-loud on a
    //     status that only differs by its batch counter.
    //   * The default arm THROWS. A status we have never mapped is a hole in our model of PayGrid,
    //     not a business outcome — so we refuse to guess and let it become a loud 500.
    private static PaymentResult TranslateChargeStatus(PayGridCharge charge)
    {
        string status = charge.Status.Trim().ToUpperInvariant();
        var reference = new VendorPaymentReference(charge.ChargeId);

        return status switch
        {
            _ when status.StartsWith("SETTLED", StringComparison.Ordinal) =>
                new PaymentResult.Succeeded(reference, SettlementStatus.Settled),

            _ when status.StartsWith("OK_PENDING", StringComparison.Ordinal) =>
                new PaymentResult.Succeeded(reference, SettlementStatus.Pending),

            // Check the specific duplicate case BEFORE the general REJECTED_ family.
            _ when status == "REJECTED_DUPLICATE" =>
                new PaymentResult.AlreadyPaid(reference),

            _ when status.StartsWith("REJECTED", StringComparison.Ordinal) =>
                new PaymentResult.PaymentRejected(DescribeRejection(status), charge.Status),

            _ => throw new UnmappedVendorStateException(charge.Status, "charge create"),
        };
    }

    /// <summary>Translate a settlement-query status into the closed <see cref="SettlementStatus"/> set.</summary>
    private static SettlementStatus TranslateSettlementStatus(string vendorStatus)
    {
        string status = vendorStatus.Trim().ToUpperInvariant();

        return status switch
        {
            _ when status.StartsWith("SETTLED", StringComparison.Ordinal) => SettlementStatus.Settled,
            _ when status.StartsWith("OK_PENDING", StringComparison.Ordinal) => SettlementStatus.Pending,
            _ when status.StartsWith("PENDING", StringComparison.Ordinal) => SettlementStatus.Pending,
            _ when status.StartsWith("REJECTED", StringComparison.Ordinal) => SettlementStatus.Failed,
            _ when status.StartsWith("FAILED", StringComparison.Ordinal) => SettlementStatus.Failed,
            _ => throw new UnmappedVendorStateException(vendorStatus, "settlement query"),
        };
    }

    // ── Value translation ──────────────────────────────────────────────────────────────────────

    // GOTCHA: PayGrid speaks integer cents; Money speaks a decimal amount + a currency. The scale
    // is Money.Scale (2 for every currency Atlas models today), so one minor unit is 1/100. Round
    // banker's-style to whole cents at the boundary — the vendor cannot represent a fractional
    // cent, and rounding is a boundary decision, made once, here.
    private static long ToCents(Money money) =>
        (long)decimal.Round(money.Amount * 100m, 0, MidpointRounding.ToEven);

    private static string Describe(PayGridTransportException ex) => ex.StatusCode switch
    {
        429 => "The payment provider is rate-limiting requests.",
        >= 500 => "The payment provider returned a server error.",
        _ => "The payment provider could not be reached.",
    };

    // Reason codes become human prose; the raw code still rides along in PaymentRejected.VendorCode
    // for logs, but the DOMAIN only ever records this neutral sentence.
    private static string DescribeRejection(string status) => status switch
    {
        "REJECTED_04" => "Declined by the payment provider (insufficient provider float).",
        "REJECTED_07" => "Declined by the payment provider (beneficiary blocked).",
        _ => "Declined by the payment provider.",
    };
}
