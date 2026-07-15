using Atlas.Application.Orgs;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain;

namespace Atlas.Application.Tests.Orgs;

public class CreateClientOrgTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 14, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Persists_the_org_and_commits_exactly_once()
    {
        var repo = new InMemoryClientOrgRepository();
        var uow = new CountingUnitOfWork();
        var useCase = new CreateClientOrg(repo, uow, new FixedClock(Now));

        var org = await useCase.ExecuteAsync("Initech GmbH", CancellationToken.None);

        Assert.Single(repo.Rows);
        Assert.Equal(1, uow.Commits);
        Assert.Equal("Initech GmbH", org.Name);
        Assert.Equal(Now, org.CreatedAtUtc);
    }

    [Fact]
    public async Task Invalid_name_commits_nothing()
    {
        var repo = new InMemoryClientOrgRepository();
        var uow = new CountingUnitOfWork();
        var useCase = new CreateClientOrg(repo, uow, new FixedClock(Now));

        await Assert.ThrowsAsync<DomainRuleException>(
            () => useCase.ExecuteAsync("   ", CancellationToken.None));

        Assert.Empty(repo.Rows);
        Assert.Equal(0, uow.Commits);
    }
}
