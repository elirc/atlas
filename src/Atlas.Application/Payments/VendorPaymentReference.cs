namespace Atlas.Application.Payments;

/// <summary>
/// The opaque handle a payment provider gives back for a disbursement — Atlas's word for "the
/// thing we quote when we ask the vendor about this payment later." A value object so it cannot
/// be confused with any other string that happens to float past.
/// </summary>
// LEARN: this is ubiquitous-language defence at the type level. Inside the anti-corruption
// layer the value is a PayGrid charge id; the moment it crosses OUT of the adapter it is a
// VendorPaymentReference — a name the domain owns. The application never learns it came from a
// field PayGrid calls "chargeRef", and S12 can swap in a vendor that calls it "txn_id" without
// a single edit outside the adapter. (ADR-0010.)
public readonly record struct VendorPaymentReference
{
    public string Value { get; }

    public VendorPaymentReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A vendor payment reference cannot be blank.", nameof(value));
        }

        Value = value.Trim();
    }

    public override string ToString() => Value;
}
