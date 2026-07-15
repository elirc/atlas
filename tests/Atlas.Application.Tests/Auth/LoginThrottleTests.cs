using Atlas.Application.Auth;
using Atlas.Application.Errors;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain.Access;

namespace Atlas.Application.Tests.Auth;

/// <summary>
/// Two halves of the S13 login-hardening: the pure lockout POLICY (no clock, no store), and how
/// the <see cref="Login"/> use case DRIVES an <see cref="Atlas.Application.Abstractions.ILoginThrottle"/>
/// — gate before verify, count failures under the normalized key, reset on success.
/// </summary>
public class LoginThrottleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    // ── The pure policy ──────────────────────────────────────────────────────

    [Fact]
    public void An_unseen_identifier_is_never_locked() =>
        Assert.False(LoginThrottlePolicy.IsLocked(LoginAttempts.None, T0));

    [Fact]
    public void Below_the_threshold_stays_unlocked()
    {
        LoginAttempts state = FoldFailures(LoginAttempts.None, LoginThrottlePolicy.MaxFailures - 1, T0);

        Assert.False(LoginThrottlePolicy.IsLocked(state, T0));
    }

    [Fact]
    public void The_threshold_failure_locks_for_the_base_duration()
    {
        LoginAttempts state = FoldFailures(LoginAttempts.None, LoginThrottlePolicy.MaxFailures, T0);

        Assert.True(LoginThrottlePolicy.IsLocked(state, T0));
        Assert.Equal(LoginThrottlePolicy.BaseLock, LoginThrottlePolicy.RetryAfter(state, T0));
    }

    [Fact]
    public void Each_further_failure_doubles_the_lockout()
    {
        LoginAttempts sixth = FoldFailures(LoginAttempts.None, LoginThrottlePolicy.MaxFailures + 1, T0);
        LoginAttempts seventh = FoldFailures(LoginAttempts.None, LoginThrottlePolicy.MaxFailures + 2, T0);

        Assert.Equal(LoginThrottlePolicy.BaseLock * 2, LoginThrottlePolicy.RetryAfter(sixth, T0));
        Assert.Equal(LoginThrottlePolicy.BaseLock * 4, LoginThrottlePolicy.RetryAfter(seventh, T0));
    }

    [Fact]
    public void Backoff_is_capped_so_a_client_is_never_bricked_forever()
    {
        LoginAttempts state = FoldFailures(LoginAttempts.None, 40, T0);

        Assert.Equal(LoginThrottlePolicy.MaxLock, LoginThrottlePolicy.RetryAfter(state, T0));
    }

    [Fact]
    public void The_lockout_expires_after_its_window()
    {
        LoginAttempts state = FoldFailures(LoginAttempts.None, LoginThrottlePolicy.MaxFailures, T0);

        Assert.True(LoginThrottlePolicy.IsLocked(state, T0));
        Assert.False(LoginThrottlePolicy.IsLocked(state, T0 + LoginThrottlePolicy.BaseLock + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void The_counting_window_forgets_stale_failures()
    {
        // Four misses, then a long pause, then one more: the count restarts from the recent miss.
        LoginAttempts stale = FoldFailures(LoginAttempts.None, LoginThrottlePolicy.MaxFailures - 1, T0);
        LoginAttempts afterPause = LoginThrottlePolicy.RegisterFailure(stale, T0 + LoginThrottlePolicy.Window + TimeSpan.FromMinutes(1));

        Assert.Equal(1, afterPause.Failures);
        Assert.False(LoginThrottlePolicy.IsLocked(afterPause, afterPause.WindowStartUtc));
    }

    // ── The use case drives the throttle ─────────────────────────────────────

    [Fact]
    public async Task A_locked_identifier_is_refused_before_the_password_is_even_checked()
    {
        var throttle = new RecordingLoginThrottle();
        throttle.Locked.Add("ada@example.com");
        Login login = LoginWith(throttle);

        // The password is CORRECT, and it still doesn't matter — locked is locked.
        await Assert.ThrowsAsync<TooManyRequestsException>(
            () => login.ExecuteAsync("ada@example.com", "correct-horse-battery", CancellationToken.None));

        Assert.Empty(Sessions.Rows);        // no session minted
        Assert.Empty(throttle.Failures);    // verify was never reached, so nothing was counted
    }

    [Fact]
    public async Task A_failed_login_is_counted_under_the_normalized_identifier()
    {
        var throttle = new RecordingLoginThrottle();
        Login login = LoginWith(throttle);

        await Assert.ThrowsAsync<UnauthenticatedException>(
            () => login.ExecuteAsync("ADA@Example.com", "wrong-password", CancellationToken.None));

        Assert.Equal(["ada@example.com"], throttle.Failures); // case-folded to one bucket
        Assert.Empty(throttle.Resets);
    }

    [Fact]
    public async Task A_successful_login_clears_the_counter()
    {
        var throttle = new RecordingLoginThrottle();
        Login login = LoginWith(throttle);

        await login.ExecuteAsync("ada@example.com", "correct-horse-battery", CancellationToken.None);

        Assert.Equal(["ada@example.com"], throttle.Resets);
        Assert.Empty(throttle.Failures);
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private InMemoryUserRepository Users { get; } = new();
    private InMemorySessionRepository Sessions { get; } = new();
    private CountingUnitOfWork Uow { get; } = new();
    private FakePasswordHasher Hasher { get; } = new();
    private SequentialSessionTokens Tokens { get; } = new();

    public LoginThrottleTests() =>
        Users.Rows.Add(User.Create(
            EmailAddress.From("ada@example.com"), "Ada", Hasher.Hash("correct-horse-battery"), T0));

    private Login LoginWith(RecordingLoginThrottle throttle) =>
        new(Users, Sessions, Hasher, Tokens, throttle, Uow, new FixedClock(T0));

    private static LoginAttempts FoldFailures(LoginAttempts start, int count, DateTimeOffset at)
    {
        LoginAttempts state = start;
        for (int i = 0; i < count; i++)
        {
            state = LoginThrottlePolicy.RegisterFailure(state, at);
        }

        return state;
    }
}
