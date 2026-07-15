using Atlas.Application.Auth;
using Atlas.Application.Errors;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain.Access;

namespace Atlas.Application.Tests.Auth;

public class LoginLogoutTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 14, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryUserRepository _users = new();
    private readonly InMemorySessionRepository _sessions = new();
    private readonly CountingUnitOfWork _uow = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly SequentialSessionTokens _tokens = new();

    public LoginLogoutTests()
    {
        _users.Rows.Add(User.Create(
            EmailAddress.From("ada@example.com"), "Ada",
            _hasher.Hash("correct-horse-battery"), Now));
    }

    private Login LoginUseCase() =>
        new(_users, _sessions, _hasher, _tokens, _uow, new FixedClock(Now));

    [Fact]
    public async Task Valid_credentials_mint_a_session()
    {
        LoggedIn result = await LoginUseCase().ExecuteAsync(
            "ADA@example.com", "correct-horse-battery", CancellationToken.None);

        Assert.Equal("token-1", result.Token);
        Assert.Single(_sessions.Rows);
        Assert.Equal(Now + Session.Lifetime, result.ExpiresAtUtc);
    }

    [Theory]
    [InlineData("ada@example.com", "wrong-password")]
    [InlineData("nobody@example.com", "correct-horse-battery")]
    [InlineData("not-even-an-email", "whatever-here")]
    public async Task All_failures_are_indistinguishable(string email, string password)
    {
        // LEARN: wrong password, unknown user, and garbage email must produce the
        // SAME exception and message — distinct errors are a user-enumeration oracle.
        var ex = await Assert.ThrowsAsync<UnauthenticatedException>(
            () => LoginUseCase().ExecuteAsync(email, password, CancellationToken.None));

        Assert.Equal("Invalid email or password.", ex.Message);
        Assert.Empty(_sessions.Rows);
    }

    [Fact]
    public async Task Logout_revokes_and_is_idempotent()
    {
        LoggedIn login = await LoginUseCase().ExecuteAsync(
            "ada@example.com", "correct-horse-battery", CancellationToken.None);

        var logout = new Logout(_sessions, _tokens, _uow, new FixedClock(Now.AddMinutes(1)));

        await logout.ExecuteAsync(login.Token, CancellationToken.None);
        Assert.False(_sessions.Rows[0].IsActive(Now.AddMinutes(2)));

        // Second logout with the same (now dead) token: still succeeds.
        await logout.ExecuteAsync(login.Token, CancellationToken.None);
    }
}
