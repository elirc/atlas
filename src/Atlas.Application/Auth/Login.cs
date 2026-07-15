using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Access;

namespace Atlas.Application.Auth;

public sealed record LoggedIn(User User, string Token, DateTimeOffset ExpiresAtUtc);

public sealed class Login(
    IUserRepository users,
    ISessionRepository sessions,
    IPasswordHasher hasher,
    ISessionTokens tokens,
    ILoginThrottle throttle,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<LoggedIn> ExecuteAsync(
        string? email,
        string? password,
        CancellationToken cancellationToken)
    {
        // WHY key on the SUBMITTED email (normalized), not on a resolved user: an address with no
        // account must throttle exactly like one with an account, or the lockout leaks which
        // addresses exist. Normalizing (trim + lower) means "Ada@x" and "ada@x" share one bucket.
        string identifier = (email ?? string.Empty).Trim().ToLowerInvariant();

        // Gate BEFORE verifying anything: a locked identifier gets a 429 (with Retry-After) and
        // never reaches the password check, so brute force meets exponential backoff (S13 / ADR-0016).
        await throttle.EnsureNotLockedAsync(identifier, cancellationToken);

        // WHY: unknown email and wrong password produce the SAME exception and the
        // same message. Distinct errors are an oracle that lets an attacker enumerate
        // which addresses have accounts — a real vulnerability class (user enumeration).
        User? user = null;
        try
        {
            user = await users.FindByEmailAsync(EmailAddress.From(email), cancellationToken);
        }
        catch (Domain.DomainRuleException)
        {
            // Malformed email at login is just a failed login, not a 422 —
            // the shape of the failure must not reveal anything either.
        }

        if (user is null || !hasher.Verify(password ?? string.Empty, user.PasswordHash))
        {
            // Count the miss toward lockout, THEN fail uniformly (same 401, same message).
            await throttle.RecordFailureAsync(identifier, cancellationToken);
            throw new UnauthenticatedException("Invalid email or password.");
        }

        // A clean login is a clean slate: clear the counter so a user who mistyped twice and then
        // succeeded is not one fat-finger away from a lockout on their next visit.
        await throttle.ResetAsync(identifier, cancellationToken);

        DateTimeOffset now = clock.GetUtcNow();
        (string token, string hash) = tokens.Mint();
        var session = Session.Start(user.Id, hash, now);

        await sessions.AddAsync(session, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return new LoggedIn(user, token, session.ExpiresAtUtc);
    }
}
