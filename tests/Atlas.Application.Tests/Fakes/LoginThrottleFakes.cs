using Atlas.Application.Abstractions;
using Atlas.Application.Errors;

namespace Atlas.Application.Tests.Fakes;

/// <summary>A throttle that never locks — for use-case tests where lockout is not the subject.</summary>
public sealed class NoLoginThrottle : ILoginThrottle
{
    public Task EnsureNotLockedAsync(string identifier, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task RecordFailureAsync(string identifier, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ResetAsync(string identifier, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Records how <see cref="Login"/> drives the throttle, and can be scripted to report an
/// identifier as locked — so a test can assert ordering (gate BEFORE verify) and normalization
/// without pulling in the real in-memory adapter or a clock.
/// </summary>
public sealed class RecordingLoginThrottle : ILoginThrottle
{
    public HashSet<string> Locked { get; } = new(StringComparer.Ordinal);
    public List<string> Checks { get; } = [];
    public List<string> Failures { get; } = [];
    public List<string> Resets { get; } = [];
    public TimeSpan LockRetryAfter { get; init; } = TimeSpan.FromSeconds(30);

    public Task EnsureNotLockedAsync(string identifier, CancellationToken cancellationToken)
    {
        Checks.Add(identifier);
        return Locked.Contains(identifier)
            ? throw new TooManyRequestsException(LockRetryAfter)
            : Task.CompletedTask;
    }

    public Task RecordFailureAsync(string identifier, CancellationToken cancellationToken)
    {
        Failures.Add(identifier);
        return Task.CompletedTask;
    }

    public Task ResetAsync(string identifier, CancellationToken cancellationToken)
    {
        Resets.Add(identifier);
        return Task.CompletedTask;
    }
}
