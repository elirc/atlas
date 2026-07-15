namespace Atlas.Application.Auth;

/// <summary>
/// The observable state of one identifier's recent failed logins. Immutable — every failure folds
/// into a NEW value (see <see cref="LoginThrottlePolicy.RegisterFailure"/>), which is what makes
/// the lockout decision a pure function a test can pin without a clock or a store.
/// </summary>
public sealed record LoginAttempts(int Failures, DateTimeOffset WindowStartUtc, DateTimeOffset? LockedUntilUtc)
{
    public static readonly LoginAttempts None = new(0, default, null);
}

/// <summary>
/// The rule for turning a run of failed logins into a lockout — separated from where the counters
/// are stored (<c>InMemoryLoginThrottle</c>) so the DECISION is pure and unit-tested in isolation.
/// </summary>
// 📘 Two constants carry the security posture: how many misses before we lock (MaxFailures) and
// how the lock grows (BaseLock doubling to MaxLock). Exponential backoff means an online guesser
// gets a handful of tries per quarter-hour, not thousands — while a real user who fat-fingers
// twice pays nothing. The window means yesterday's typos don't count against today.
public static class LoginThrottlePolicy
{
    /// <summary>Failures within the window before the identifier is first locked.</summary>
    public const int MaxFailures = 5;

    /// <summary>Failures older than this (since the window's first failure) are forgotten.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    /// <summary>The first lockout duration; each further failure doubles it…</summary>
    public static readonly TimeSpan BaseLock = TimeSpan.FromSeconds(30);

    /// <summary>…up to this cap, so a stuck client is inconvenienced, not permanently bricked.</summary>
    public static readonly TimeSpan MaxLock = TimeSpan.FromMinutes(15);

    /// <summary>Remaining lockout as of <paramref name="now"/>; zero (or negative) means not locked.</summary>
    public static TimeSpan RetryAfter(LoginAttempts attempts, DateTimeOffset now) =>
        attempts.LockedUntilUtc is { } until && until > now ? until - now : TimeSpan.Zero;

    /// <summary>True when this identifier is locked out at <paramref name="now"/>.</summary>
    public static bool IsLocked(LoginAttempts attempts, DateTimeOffset now) =>
        RetryAfter(attempts, now) > TimeSpan.Zero;

    /// <summary>Fold one failed attempt into the state, applying the window and backoff rules.</summary>
    public static LoginAttempts RegisterFailure(LoginAttempts attempts, DateTimeOffset now)
    {
        // Roll the counting window: a fresh identifier, or one whose window has fully elapsed
        // since its first counted failure, begins the count again from this failure.
        bool windowExpired = attempts.Failures == 0 || now - attempts.WindowStartUtc > Window;
        int failures = (windowExpired ? 0 : attempts.Failures) + 1;
        DateTimeOffset windowStart = windowExpired ? now : attempts.WindowStartUtc;

        DateTimeOffset? lockedUntil = null;
        if (failures >= MaxFailures)
        {
            int over = failures - MaxFailures; // 0 on the first lock, then 1, 2, …
            long ticks = Math.Min(MaxLock.Ticks, BaseLock.Ticks * (1L << Math.Min(over, 20)));
            lockedUntil = now + TimeSpan.FromTicks(ticks);
        }

        return new LoginAttempts(failures, windowStart, lockedUntil);
    }
}
