namespace Atlas.Application.Payments;

/// <summary>
/// The outcome of asking a provider to pay one invoice — a CLOSED set of exactly the things the
/// application can DO something about. The anti-corruption layer returns one of these; the pay
/// use case switches over them exhaustively. There is no "other" case, and that is the point.
/// </summary>
// 📘 Why a typed result union and not `catch (Exception)` + string matching:
//   * A closed set is a CHECKLIST the compiler helps you complete — add a member and every
//     exhaustive switch stops compiling until it is handled. `catch (Exception ex) when
//     (ex.Message.Contains("declined"))` is a checklist of one, silently missing the rest.
//   * Each member carries exactly the data the caller needs (a reference to record, a reason to
//     surface) and nothing it must not depend on (no HTTP status, no vendor code shape).
//   * Control flow that matters is not signalled by exceptions. A declined payment is a normal
//     business outcome, not an exceptional one; throwing for it would put policy (retry? fail
//     the invoice?) in a catch block far from the decision.
// The ONE thing that is NOT in this set is "the vendor said something we've never heard of."
// That is not an outcome the application can act on — it is a hole in our map of the vendor —
// so the ACL throws UnmappedVendorStateException instead of inventing a member. (ADR-0010.)
public abstract record PaymentResult
{
    // WHY: a private constructor seals the hierarchy to the nested records below. No other
    // assembly can add a PaymentResult, so an exhaustive switch here is exhaustive forever.
    private PaymentResult()
    {
    }

    /// <summary>The provider accepted the disbursement. Settlement may still be in flight (async).</summary>
    public sealed record Succeeded(VendorPaymentReference Reference, SettlementStatus Settlement) : PaymentResult;

    /// <summary>
    /// The provider reports this payment was already made — a prior attempt reached it. Carries
    /// the reference of the existing payment so the invoice can be reconciled to it.
    /// </summary>
    public sealed record AlreadyPaid(VendorPaymentReference Reference) : PaymentResult;

    /// <summary>The provider declined the disbursement (a business "no"). Reason is provider-neutral prose.</summary>
    public sealed record PaymentRejected(string Reason, string VendorCode) : PaymentResult;

    /// <summary>
    /// The provider could not be reached (rate-limited, timed out, down). A TRANSIENT fault: the
    /// invoice is untouched and the caller may retry later. Distinct from PaymentRejected, which
    /// is the vendor answering "no" rather than not answering at all.
    /// </summary>
    public sealed record VendorUnavailable(string Reason) : PaymentResult;
}
