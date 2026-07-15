namespace Atlas.Infrastructure.Vendors.PayGrid;

// ─────────────────────────────────────────────────────────────────────────────────────────────
// PayGrid's wire types. Everything in this file speaks PayGrid's dialect — integer cents, flat
// currency strings, stringly-typed statuses. NONE of it may escape the adapter
// (PayGridPaymentProvider). If a PayGrid type shows up in Application or Domain, the ACL has
// sprung a leak. (ADR-0010.)
// ─────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Body of PayGrid's "create charge" call. Note the dialect: the amount is an INTEGER number of
/// MINOR UNITS (cents), not a decimal; the currency is a bare 3-letter string; the reference is a
/// free-text memo PayGrid prints on its statement (it is NOT a key PayGrid deduplicates on).
/// </summary>
public sealed record PayGridChargeRequest(long AmountInCents, string CurrencyCode, string Reference);

/// <summary>
/// A charge as PayGrid reports it. <see cref="Status"/> is one of PayGrid's dialect strings —
/// see <see cref="PayGridClient"/>'s documentation for the (open-ended, versioned-without-notice)
/// vocabulary. The amount echoes back in cents.
/// </summary>
public sealed record PayGridCharge(string ChargeId, string Status, long AmountInCents, string CurrencyCode);

/// <summary>
/// PayGrid's transport-level failure — the HTTP layer said no before the charge layer got a look.
/// Thrown for 429 (rate limit) and 5xx. Distinct from a charge whose <c>Status</c> is a rejection:
/// this is "we never got an answer", not "the answer was no".
/// </summary>
public sealed class PayGridTransportException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>
/// Deterministic knobs for the in-process PayGrid mock. A real PayGrid rate-limits
/// UNPREDICTABLY; the mock turns that into a knob (specific call ordinals raise a 429) so tests
/// are deterministic — the "injected toggle" a mock uses instead of a live coin flip.
/// </summary>
public sealed record PayGridSettings
{
    /// <summary>The status a freshly-created charge is stamped with (default: accepted, settling).</summary>
    public string CreatedStatus { get; init; } = "OK_PENDING_2";

    /// <summary>The status a settlement query reports (default: settled).</summary>
    public string SettlementStatus { get; init; } = "SETTLED";

    /// <summary>1-based ordinals of CreateCharge calls that raise a 429 instead of charging.</summary>
    public IReadOnlySet<int> RateLimitedChargeCalls { get; init; } = new HashSet<int>();

    /// <summary>1-based ordinals of GetCharge calls that raise a 429 instead of reporting.</summary>
    public IReadOnlySet<int> RateLimitedStatusCalls { get; init; } = new HashSet<int>();

    /// <summary>App default: never rate-limit (the happy path stays deterministic).</summary>
    public static PayGridSettings Default { get; } = new();
}
