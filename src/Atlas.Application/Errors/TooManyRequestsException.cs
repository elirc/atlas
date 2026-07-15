namespace Atlas.Application.Errors;

/// <summary>
/// The caller is being rate-limited — the Api maps this to 429 with a <c>Retry-After</c> header.
/// Distinct from 401: the credentials were never even checked, because this identifier is in a
/// cool-down. Carries how long the caller should wait.
/// </summary>
// LEARN: 429 is not an authn/authz outcome, it is a throttling one. Keeping it its own type (and
// its own problem `type` URI) lets a client tell "slow down" apart from "wrong password" and back
// off correctly instead of hammering into a longer lockout.
public sealed class TooManyRequestsException(
    TimeSpan retryAfter, string message = "Too many attempts. Please try again later.")
    : Exception(message)
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}
