using Atlas.Application.Errors;
using Atlas.Application.Payroll;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;

namespace Atlas.Application.Tests.Payroll;

public class CreatePayCycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrgId = Guid.NewGuid();

    private readonly InMemoryPayCycleRepository _cycles = new();
    private readonly RecordingAuditTrail _audit = new();
    private readonly CountingUnitOfWork _uow = new();

    private CreatePayCycle UseCase(StubContext? ctx = null) => new(
        _cycles,
        new InMemoryCountryRepository(new Country(CountryCode.From("DEU"), "Germany", "EUR")),
        _audit, _uow,
        ctx ?? StubContext.OrgUser(OrgId, Role.OrgAdmin),
        new FixedClock(Now));

    [Fact]
    public async Task Creates_a_monthly_cycle_and_audits()
    {
        PayCycle cycle = await UseCase().ExecuteAsync("DEU", "monthly", CancellationToken.None);

        Assert.Equal("DEU", cycle.Country.Value);
        Assert.Equal(PayFrequency.Monthly, cycle.Frequency);
        Assert.Single(_cycles.Rows);
        Assert.Equal(1, _uow.Commits);
        Assert.Contains(_audit.Entries, e => e.Action == "payroll.cycle_created");
    }

    [Fact]
    public async Task Refuses_a_duplicate_cycle_for_the_same_country()
    {
        await UseCase().ExecuteAsync("DEU", "monthly", CancellationToken.None);

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            UseCase().ExecuteAsync("DEU", "monthly", CancellationToken.None));

        Assert.Single(_cycles.Rows);
    }

    [Fact]
    public async Task An_unsupported_country_is_a_422()
    {
        await Assert.ThrowsAsync<DomainRuleException>(() =>
            UseCase().ExecuteAsync("FRA", "monthly", CancellationToken.None));
    }

    [Fact]
    public async Task An_unknown_frequency_is_a_422()
    {
        await Assert.ThrowsAsync<DomainRuleException>(() =>
            UseCase().ExecuteAsync("DEU", "weekly", CancellationToken.None));
    }

    [Fact]
    public async Task Only_org_admins_may_create_a_cycle()
    {
        var useCase = UseCase(StubContext.OrgUser(OrgId, Role.Employee));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            useCase.ExecuteAsync("DEU", "monthly", CancellationToken.None));
        Assert.Equal(0, _uow.Commits);
    }
}
