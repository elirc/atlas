using Atlas.Application.Errors;
using Atlas.Application.Orgs;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain.Access;
using Atlas.Domain.Orgs;

namespace Atlas.Application.Tests.Orgs;

public class OrgTenancyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 14, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryClientOrgRepository _orgs = new();
    private readonly ClientOrg _orgA;
    private readonly ClientOrg _orgB;

    public OrgTenancyTests()
    {
        _orgA = ClientOrg.Create("Org A", Now);
        _orgB = ClientOrg.Create("Org B", Now);
        _orgs.AddAsync(_orgA, CancellationToken.None).GetAwaiter().GetResult();
        _orgs.AddAsync(_orgB, CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task A_member_reads_their_own_org()
    {
        var useCase = new GetClientOrg(_orgs, StubContext.OrgUser(_orgA.Id, Role.HrManager));

        var org = await useCase.ExecuteAsync(_orgA.Id, CancellationToken.None);

        Assert.Equal("Org A", org.Name);
    }

    [Fact]
    public async Task Cross_org_read_is_not_found_not_forbidden()
    {
        var useCase = new GetClientOrg(_orgs, StubContext.OrgUser(_orgA.Id, Role.OrgAdmin));

        // WHY 404: a 403 would confirm org B exists. Existence across tenant
        // boundaries is need-to-know.
        await Assert.ThrowsAsync<NotFoundException>(
            () => useCase.ExecuteAsync(_orgB.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Platform_staff_may_read_any_org()
    {
        var useCase = new GetClientOrg(_orgs, StubContext.Ops());

        Assert.Equal("Org B", (await useCase.ExecuteAsync(_orgB.Id, CancellationToken.None)).Name);
    }

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        var useCase = new GetClientOrg(_orgs, StubContext.Anonymous());

        await Assert.ThrowsAsync<UnauthenticatedException>(
            () => useCase.ExecuteAsync(_orgA.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Listing_all_orgs_is_platform_staff_only()
    {
        var forOrgAdmin = new ListClientOrgs(_orgs, StubContext.OrgUser(_orgA.Id, Role.OrgAdmin));
        await Assert.ThrowsAsync<ForbiddenException>(
            () => forOrgAdmin.ExecuteAsync(null, 10, CancellationToken.None));

        var forOps = new ListClientOrgs(_orgs, StubContext.Ops());
        var page = await forOps.ExecuteAsync(null, 10, CancellationToken.None);
        Assert.Equal(2, page.Items.Count);
    }
}
