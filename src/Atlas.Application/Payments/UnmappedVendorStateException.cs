namespace Atlas.Application.Payments;

/// <summary>
/// Thrown by the anti-corruption layer when a provider returns a state it has never been taught
/// to translate. NOT a business outcome — a hole in our model of the vendor. It deliberately
/// escapes as an unhandled 500 so it pages someone, rather than being smoothed into a guess.
/// </summary>
// ⚠️ This is the "unknown-state policy" the ACL lives by: fail loud, never guess. A real vendor
// WILL one day return a status your enum forgot — a new "OK_HOLD_1", a "REVIEW_PENDING". The
// tempting move is a default branch that treats the unknown as "probably fine" (or "probably
// failed"). Both are lies the size of a payment. The honest move is to STOP: an unmapped state
// means the adapter's dialect table is stale, and the fix is a human reading the vendor's
// changelog and adding a row — not code that shrugs. Because this is a bug (our map is wrong),
// not a request problem, it is intentionally NOT in AtlasExceptionHandler's translation table:
// it becomes a 500, the loudest thing the API can say. (ADR-0010; docs/concepts/error-taxonomies.md.)
public sealed class UnmappedVendorStateException(string vendorState, string context)
    : Exception($"The payment provider returned an unmapped state '{vendorState}' ({context}); refusing to guess.")
{
    /// <summary>The raw vendor state string that was not recognised.</summary>
    public string VendorState { get; } = vendorState;
}
