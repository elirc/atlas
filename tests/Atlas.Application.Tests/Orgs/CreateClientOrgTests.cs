using Atlas.Application.Errors;
using Atlas.Application.Orgs;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain;
using Atlas.Domain.Access;

namespace Atlas.Application.Tests.Orgs;

public class CreateClientOrgTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 14, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Ops_can_create_an_org_with_one_commit()
    {
        var repo = new InMemoryClientOrgRepository();
        var uow = new CountingUnitOfWork();
        var useCase = new CreateClientOrg(repo, uow, StubContext.Ops(), new FixedClock(Now));

        var org = await useCase.ExecuteAsync("Initech GmbH", CancellationToken.None);

        Assert.Single(repo.Rows);
        Assert.Equal(1, uow.Commits);
        Assert.Equal("Initech GmbH", org.Name);
        Assert.Equal(Now, org.CreatedAtUtc);
    }

    [Fact]
    public async Task Org_users_may_not_use_the_ops_creation_path()
    {
        var repo = new InMemoryClientOrgRepository();
        var uow = new CountingUnitOfWork();
        var useCase = new CreateClientOrg(
            repo, uow, StubContext.OrgUser(Guid.NewGuid(), Role.OrgAdmin), new FixedClock(Now));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => useCase.ExecuteAsync("Sneaky Org", CancellationToken.None));

        Assert.Empty(repo.Rows);
    }

    [Fact]
    public async Task Invalid_name_commits_nothing()
    {
        var repo = new InMemoryClientOrgRepository();
        var uow = new CountingUnitOfWork();
        var useCase = new CreateClientOrg(repo, uow, StubContext.Ops(), new FixedClock(Now));

        await Assert.ThrowsAsync<DomainRuleException>(
            () => useCase.ExecuteAsync("   ", CancellationToken.None));

        Assert.Empty(repo.Rows);
        Assert.Equal(0, uow.Commits);
    }
}
