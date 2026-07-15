using Atlas.Application.Tests.Fakes;
using Atlas.Application.Workforce;
using Atlas.Domain.Access;
using Atlas.Domain.Countries;
using Atlas.Domain.Workforce;

namespace Atlas.Application.Tests.Workforce;

public class WorkerReadsTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrgA = Guid.NewGuid();
    private static readonly Guid OrgB = Guid.NewGuid();

    private readonly InMemoryWorkerRepository _workers = new();
    private readonly InMemoryEmploymentRecordRepository _records = new();
    private readonly Worker _mariaInA;

    public WorkerReadsTests()
    {
        _mariaInA = Worker.Hire(
            OrgA, "Maria Santos", EmailAddress.From("maria@example.com"),
            CountryCode.From("DEU"), new DateOnly(2026, 3, 1), Now);
        var benInB = Worker.Hire(
            OrgB, "Ben Okafor", EmailAddress.From("ben@example.com"),
            CountryCode.From("GBR"), new DateOnly(2026, 4, 1), Now.AddMinutes(1));

        _workers.Rows.Add(_mariaInA);
        _workers.Rows.Add(benInB);

        _records.Rows.Add(EmploymentRecord.Create(
            _mariaInA.Id, new DateOnly(2026, 3, 1), "Engineer", 60_000m, "EUR", 40,
            Guid.NewGuid(), Now));
        _records.Rows.Add(EmploymentRecord.Create(
            _mariaInA.Id, new DateOnly(2026, 9, 1), "Senior Engineer", 75_000m, "EUR", 40,
            Guid.NewGuid(), Now.AddMinutes(2)));
    }

    [Fact]
    public async Task Terms_as_of_resolves_past_and_future_correctly()
    {
        var useCase = new GetWorkerTerms(
            _workers, _records, StubContext.OrgUser(OrgA, Role.HrManager), new FixedClock(Now));

        WorkerTerms march = await useCase.ExecuteAsync(
            _mariaInA.Id, new DateOnly(2026, 5, 1), CancellationToken.None);
        Assert.Equal(60_000m, march.Effective!.AnnualSalary);

        WorkerTerms october = await useCase.ExecuteAsync(
            _mariaInA.Id, new DateOnly(2026, 10, 1), CancellationToken.None);
        Assert.Equal(75_000m, october.Effective!.AnnualSalary);

        // History is newest-first for display.
        Assert.Equal(
            [new DateOnly(2026, 9, 1), new DateOnly(2026, 3, 1)],
            october.History.Select(r => r.EffectiveFrom));
    }

    [Fact]
    public async Task Terms_default_to_today_via_the_injected_clock()
    {
        var useCase = new GetWorkerTerms(
            _workers, _records, StubContext.OrgUser(OrgA, Role.HrManager),
            new FixedClock(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero)));

        WorkerTerms today = await useCase.ExecuteAsync(_mariaInA.Id, null, CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 12, 1), today.AsOf);
        Assert.Equal(75_000m, today.Effective!.AnnualSalary);
    }

    [Fact]
    public async Task Listing_is_scoped_to_the_callers_org()
    {
        var listForA = new ListWorkers(_workers, StubContext.OrgUser(OrgA, Role.Employee));

        var page = await listForA.ExecuteAsync(null, 10, CancellationToken.None);

        Assert.Single(page.Items);
        Assert.Equal("Maria Santos", page.Items[0].FullName);
    }
}
