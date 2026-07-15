namespace Atlas.Application.Payments;

/// <summary>
/// Where a disbursement is in the provider's own settlement pipeline, in Atlas's words. A
/// CLOSED set: the anti-corruption layer maps the vendor's many status strings onto exactly
/// these three, and maps anything it does not recognise to a loud exception rather than a
/// fourth, guessed value.
/// </summary>
// 📘 Three states is a DECISION, not a lack of imagination. The vendor emits a zoo of codes
// ("OK_PENDING_2", "SETTLED", "REJECTED_04", …); the application can only ACT on three
// distinctions: the money is on its way (Pending), it has landed (Settled), or it will not
// (Failed). Collapsing the vendor's vocabulary to what the caller can do about it is the whole
// job of the ACL — see docs/concepts/error-taxonomies.md.
public enum SettlementStatus
{
    /// <summary>Accepted by the provider and still moving — not yet final either way.</summary>
    Pending = 1,

    /// <summary>Terminal success: the funds reached the contractor.</summary>
    Settled = 2,

    /// <summary>Terminal failure: the provider will not complete this disbursement.</summary>
    Failed = 3,
}

/// <summary>A disbursement's current settlement, as reported by the provider and translated by the ACL.</summary>
public sealed record PaymentSettlement(VendorPaymentReference Reference, SettlementStatus Status);
