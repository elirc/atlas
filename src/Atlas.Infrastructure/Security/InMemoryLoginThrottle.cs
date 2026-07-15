using System.Collections.Concurrent;
using Atlas.Application.Abstractions;
using Atlas.Application.Auth;
using Atlas.Application.Errors;

namespace Atlas.Infrastructure.Security;

/// <summary>
/// In-memory adapter for <see cref="ILoginThrottle"/>: a concurrent map of identifier → attempt
/// state, with the lock decision delegated to the pure <see cref="LoginThrottlePolicy"/>. A
/// singleton, because the counters ARE process state — the same instance must see every request.
/// </summary>
// ⚠️ Recorded debt (ADR-0016): this is per-process. On a multi-node deployment each instance keeps
// its own counters, so the effective threshold multiplies by the node count and a load balancer
// can spread a brute-force run thin. Production moves this behind a shared store (Redis INCR + TTL,
// or the database) — the PORT does not change, only this adapter. Named, not hidden.
internal sealed class InMemoryLoginThrottle(TimeProvider clock) : ILoginThrottle
{
    // Ordinal comparison: the use case has already normalized the key (trim + lower), so the store
    // must NOT re-fold case — that would be a second, invisible normalization policy.
    private readonly ConcurrentDictionary<string, LoginAttempts> _attempts = new(StringComparer.Ordinal);

    public Task EnsureNotLockedAsync(string identifier, CancellationToken cancellationToken)
    {
        if (_attempts.TryGetValue(identifier, out LoginAttempts? state))
        {
            TimeSpan retryAfter = LoginThrottlePolicy.RetryAfter(state, clock.GetUtcNow());
            if (retryAfter > TimeSpan.Zero)
            {
                throw new TooManyRequestsException(retryAfter);
            }
        }

        return Task.CompletedTask;
    }

    public Task RecordFailureAsync(string identifier, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        _attempts.AddOrUpdate(
            identifier,
            _ => LoginThrottlePolicy.RegisterFailure(LoginAttempts.None, now),
            (_, existing) => LoginThrottlePolicy.RegisterFailure(existing, now));
        return Task.CompletedTask;
    }

    public Task ResetAsync(string identifier, CancellationToken cancellationToken)
    {
        _attempts.TryRemove(identifier, out _);
        return Task.CompletedTask;
    }
}
