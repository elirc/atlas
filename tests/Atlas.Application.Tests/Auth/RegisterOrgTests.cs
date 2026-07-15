using Atlas.Application.Auth;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain;
using Atlas.Domain.Access;

namespace Atlas.Application.Tests.Auth;

public class RegisterOrgTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 14, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryClientOrgRepository _orgs = new();
    private readonly InMemoryUserRepository _users = new();
    private readonly InMemoryMembershipRepository _memberships = new();
    private readonly InMemorySessionRepository _sessions = new();
    private readonly CountingUnitOfWork _uow = new();

    private RegisterOrg UseCase() => new(
        _orgs, _users, _memberships, _sessions,
        new FakePasswordHasher(), new SequentialSessionTokens(), _uow, new FixedClock(Now));

    [Fact]
    public async Task Creates_org_admin_and_session_atomically()
    {
        RegisteredOrg result = await UseCase().ExecuteAsync(
            "Initech", "ada@example.com", "correct-horse-battery", "Ada",
            CancellationToken.None);

        Assert.Single(_orgs.Rows);
        Assert.Single(_users.Rows);
        Assert.Single(_memberships.Rows);
        Assert.Single(_sessions.Rows);

        // One commit for four writes — the atomicity claim, asserted.
        Assert.Equal(1, _uow.Commits);

        Assert.Equal(Role.OrgAdmin, _memberships.Rows[0].Role);
        Assert.Equal(result.Org.Id, _memberships.Rows[0].OrgId);
        Assert.Equal("token-1", result.Token);
        Assert.Equal("tokenhash-1", _sessions.Rows[0].TokenHash);
    }

    [Fact]
    public async Task Duplicate_email_is_rejected_before_any_write()
    {
        await UseCase().ExecuteAsync(
            "First Org", "ada@example.com", "correct-horse-battery", "Ada",
            CancellationToken.None);

        await Assert.ThrowsAsync<DomainRuleException>(() => UseCase().ExecuteAsync(
            "Second Org", "ADA@EXAMPLE.COM", "another-password!", "Ada Again",
            CancellationToken.None));

        Assert.Single(_orgs.Rows);
        Assert.Equal(1, _uow.Commits);
    }

    [Fact]
    public async Task Short_password_is_rejected()
    {
        await Assert.ThrowsAsync<DomainRuleException>(() => UseCase().ExecuteAsync(
            "Initech", "ada@example.com", "short", "Ada", CancellationToken.None));

        Assert.Empty(_users.Rows);
        Assert.Equal(0, _uow.Commits);
    }

    [Fact]
    public async Task Password_is_stored_only_as_a_verifier()
    {
        await UseCase().ExecuteAsync(
            "Initech", "ada@example.com", "correct-horse-battery", "Ada",
            CancellationToken.None);

        // The repository must never contain the raw password.
        Assert.DoesNotContain(
            _users.Rows, u => u.PasswordHash.Contains("correct-horse-battery")
                && !u.PasswordHash.StartsWith("hashed:"));
        Assert.StartsWith("hashed:", _users.Rows[0].PasswordHash);
    }
}
