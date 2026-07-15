namespace Atlas.Application.Payroll;

/// <summary>
/// Thrown by a payroll-calculation ACL when a vendor returns a line CODE it has never been taught
/// to translate. NOT a business outcome — a hole in our model of the vendor. Like its payments-side
/// twin (<c>UnmappedVendorStateException</c>) it deliberately escapes as an unhandled 500, so it
/// pages someone rather than being smoothed into a guessed mapping.
/// </summary>
// ⚠️ THE UNKNOWN-STATE POLICY, APPLIED TO A CALCULATION VENDOR (ADR-0010's doctrine, ADR-0014's
// second application). LedgerPay's line vocabulary is open and versioned-without-notice; one day a
// response will carry a code the adapter's table has never seen. The tempting default arm — fold it
// into "some other deduction" — is a lie the size of a paycheck. The honest move is to STOP: an
// unmapped line means the dialect table is stale, and the fix is a human reading LedgerPay's
// changelog and adding a row, not code that shrugs. Because this is OUR bug (our map is wrong), it
// is intentionally absent from AtlasExceptionHandler's table and becomes a loud 500.
//
// 🔍 We did NOT unify this with the payments-side UnmappedVendorStateException. They are four lines
// each and rhyme, but the MESSAGE is the value ("payment provider" vs "calculation vendor"), and a
// shared base class to save four lines would couple two unrelated ACLs at exactly the seam they
// exist to keep separate. Two small, specific exceptions beat one general one here — the YAGNI
// ledger from ADR-0009, re-applied. Unify only if a third vendor ACL wants the same shape.
public sealed class UnmappedVendorLineException(string vendorCode, string context)
    : Exception($"The calculation vendor returned an unmapped line code '{vendorCode}' ({context}); refusing to guess.")
{
    /// <summary>The raw vendor line code that was not recognised.</summary>
    public string VendorCode { get; } = vendorCode;
}
