using Atlas.Application.Errors;
using Atlas.Application.Orgs;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain.Orgs;

namespace Atlas.Application.Tests.Orgs;

public class GetClientOrgTests
{
    [Fact]
    public async Task Returns_the_org_when_it_exists()
    {
        var repo = new InMemoryClientOrgRepository();
        var org = ClientOrg.Create("Initech", DateTimeOffset.UnixEpoch);
        await repo.AddAsync(org, CancellationToken.None);

        var found = await new GetClientOrg(repo).ExecuteAsync(org.Id, CancellationToken.None);

        Assert.Equal(org.Id, found.Id);
    }

    [Fact]
    public async Task Unknown_id_raises_not_found()
    {
        var useCase = new GetClientOrg(new InMemoryClientOrgRepository());

        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => useCase.ExecuteAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal("client org", ex.Resource);
    }
}
