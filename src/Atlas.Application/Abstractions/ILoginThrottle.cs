namespace Atlas.Application.Abstractions;

/// <summary>
/// Brute-force defence for the login endpoint: counts failed attempts per identifier and locks the
/// identifier out with backoff once a threshold is crossed. A PORT — the use case knows only this
/// shape; whether the counters live in memory, Redis, or the database is Infrastructure's problem.
/// </summary>
// LEARN: this is deliberately keyed on an OPAQUE identifier (the submitted email, normalized),
// not on "the user", because a user who does not exist must be throttled exactly like one who
// does — otherwise the lockout itself becomes a user-enumeration oracle (ADR-0003's whole point).
// 🔗 ADR-0003 recorded auth hardening — rate limiting and lockout — as S13 backlog. This is it.
public interface ILoginThrottle
{
    /// <summary>
    /// Throws <c>TooManyRequestsException</c> (→ 429, with Retry-After) when this identifier is
    /// currently locked out. Called BEFORE any credential check, so a locked account can't be probed.
    /// </summary>
    Task EnsureNotLockedAsync(string identifier, CancellationToken cancellationToken);

    /// <summary>Record one failed attempt for this identifier (may cross the lockout threshold).</summary>
    Task RecordFailureAsync(string identifier, CancellationToken cancellationToken);

    /// <summary>Clear the counter after a successful login — a clean login is a clean slate.</summary>
    Task ResetAsync(string identifier, CancellationToken cancellationToken);
}
