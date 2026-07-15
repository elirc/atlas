namespace Atlas.Application.Errors;

/// <summary>
/// The payment provider could not be reached in time (rate-limited, timed out, down). A
/// TRANSIENT fault, not the client's fault and not a business "no" — the Api maps it to 503 so
/// the caller knows to retry rather than to fix its request.
/// </summary>
// WHY a distinct exception and not a DomainRuleException(422): 422 says "your request was
// well-formed but the business rules reject it" — retrying it unchanged is pointless. This says
// the opposite: the request is fine, the downstream is momentarily not, and retrying is exactly
// the right move. Conflating the two teaches clients to give up on transient faults and to hammer
// on permanent ones. (The idempotency needed to retry a money-mover SAFELY is the S10 theme.)
public sealed class PaymentProviderUnavailableException(string reason)
    : Exception($"The payment provider is unavailable: {reason}")
{
    public string Reason { get; } = reason;
}
