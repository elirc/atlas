using Atlas.Application.Leave;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Countries;

namespace Atlas.Application.Tests.Leave;

public class CreateLeavePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrgId = Guid.NewGuid();

    private readonly InMemoryLeavePolicyRepository _policies = new();
    private readonly RecordingAuditTrail _audit = new();
    private readonly CountingUnitOfWork _uow = new();

    private CreateLeavePolicy UseCase(StubContext? ctx = null) => new(
        _policies,
        new InMemoryCountryRepository(
            new Country(CountryCode.From("USA"), "United States", "USD"),
            new Country(CountryCode.From("DEU"), "Germany", "EUR")),
        _audit, _uow,
        ctx ?? StubContext.OrgUser(OrgId, Role.OrgAdmin),
        new FixedClock(Now));

    [Fact]
    public async Task Creates_a_policy_and_audits_in_one_commit()
    {
        var policy = await UseCase().ExecuteAsync("USA", 24m, 10m, CancellationToken.None);

        Assert.Single(_policies.Rows);
        Assert.Equal(1, _uow.Commits);
        Assert.Single(_audit.Entries);
        Assert.Equal("leave_policy.created", _audit.Entries[0].Action);
        Assert.Equal(24m, policy.AnnualEntitlementDays);
        Assert.Equal(OrgId, policy.OrgId);
    }

    [Fact]
    public async Task Below_minimum_entitlement_is_rejected_with_the_minimum_named()
    {
        // DEU statutory minimum is 20 days (rule pack); 10 is below it.
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() =>
            UseCase().ExecuteAsync("DEU", 10m, 5m, CancellationToken.None));

        Assert.Contains("20", ex.Message);
        Assert.Empty(_policies.Rows);
        Assert.Equal(0, _uow.Commits);
    }

    [Fact]
    public async Task Duplicate_policy_for_a_country_is_rejected()
    {
        await UseCase().ExecuteAsync("USA", 24m, 10m, CancellationToken.None);

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            UseCase().ExecuteAsync("USA", 20m, 5m, CancellationToken.None));

        Assert.Single(_policies.Rows);
    }

    [Fact]
    public async Task Unsupported_country_is_rejected_before_any_write()
    {
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() =>
            UseCase().ExecuteAsync("FRA", 24m, 10m, CancellationToken.None));

        Assert.Contains("FRA", ex.Message);
        Assert.Equal(0, _uow.Commits);
    }

    [Fact]
    public async Task Hr_managers_cannot_set_policy()
    {
        await Assert.ThrowsAsync<Atlas.Application.Errors.ForbiddenException>(() =>
            UseCase(StubContext.OrgUser(OrgId, Role.HrManager))
                .ExecuteAsync("USA", 24m, 10m, CancellationToken.None));

        Assert.Empty(_policies.Rows);
    }
}
