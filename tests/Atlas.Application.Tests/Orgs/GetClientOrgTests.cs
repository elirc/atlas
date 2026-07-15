using Atlas.Application.Errors;
using Atlas.Application.Orgs;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain.Orgs;

namespace Atlas.Application.Tests.Orgs;

// Tenancy-specific behavior (cross-org 404, ops override) lives in OrgTenancyTests;
// this file keeps the basic found/not-found contract.
public class GetClientOrgTests
{
    [Fact]
    public async Task Returns_the_org_when_it_exists_and_the_caller_belongs_to_it()
    {
        var repo = new InMemoryClientOrgRepository();
        var org = ClientOrg.Create("Initech", DateTimeOffset.UnixEpoch);
        await repo.AddAsync(org, CancellationToken.None);

        var useCase = new GetClientOrg(
            repo, StubContext.OrgUser(org.Id, Domain.Access.Role.Employee));

        var found = await useCase.ExecuteAsync(org.Id, CancellationToken.None);

        Assert.Equal(org.Id, found.Id);
    }

    [Fact]
    public async Task Unknown_id_raises_not_found_even_for_ops()
    {
        var useCase = new GetClientOrg(new InMemoryClientOrgRepository(), StubContext.Ops());

        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => useCase.ExecuteAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal("client org", ex.Resource);
    }
}
