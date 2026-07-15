namespace Atlas.Api.Errors;

/// <summary>Stable problem `type` URIs — part of the API contract (ADR-0002).</summary>
// LEARN: RFC 7807's `type` is the machine-readable half of an error. Clients switch
// on these URIs, not on message text — which means messages can improve freely while
// integrations keep working. Stability here is a compatibility promise.
public static class AtlasProblems
{
    private const string Base = "https://atlas.example/problems/";

    public const string NotFound = Base + "not-found";
    public const string InvalidRequest = Base + "invalid-request";
    public const string DomainRuleViolated = Base + "domain-rule-violated";
    public const string Unauthenticated = Base + "unauthenticated";
    public const string Forbidden = Base + "forbidden";

    // S09: a downstream payment vendor is momentarily unreachable (503, retryable).
    public const string VendorUnavailable = Base + "vendor-unavailable";

    // S10: an Idempotency-Key was reused with a different request body (422, client bug).
    public const string IdempotencyConflict = Base + "idempotency-conflict";

    // S13: the caller is rate-limited (429, retryable after a cool-down — see Retry-After).
    public const string TooManyRequests = Base + "too-many-requests";
}
