using Atlas.Application.Errors;
using Atlas.Application.Tests.Fakes;
using Atlas.Application.Workforce;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Countries;

namespace Atlas.Application.Tests.Workforce;

public class HireWorkerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrgId = Guid.NewGuid();

    private readonly InMemoryWorkerRepository _workers = new();
    private readonly InMemoryEmploymentRecordRepository _records = new();
    private readonly RecordingAuditTrail _audit = new();
    private readonly CountingUnitOfWork _uow = new();

    private HireWorker UseCase(StubContext? ctx = null) => new(
        _workers, _records,
        new InMemoryCountryRepository(new Country(CountryCode.From("DEU"), "Germany", "EUR")),
        _audit, _uow,
        ctx ?? StubContext.OrgUser(OrgId, Role.HrManager),
        new FixedClock(Now));

    [Fact]
    public async Task Hires_atomically_worker_terms_and_audit_in_one_commit()
    {
        HiredWorker hired = await UseCase().ExecuteAsync(
            "Maria Santos", "maria@example.com", "DEU", new DateOnly(2026, 8, 1),
            "Engineer", 60_000m, 40, CancellationToken.None);

        Assert.Single(_workers.Rows);
        Assert.Single(_records.Rows);
        Assert.Single(_audit.Entries);
        Assert.Equal(1, _uow.Commits);

        Assert.Equal(OrgId, hired.Worker.OrgId);
        // Salary is priced in the COUNTRY's currency, not the org's.
        Assert.Equal("EUR", hired.InitialTerms.SalaryCurrency);
        Assert.Equal(new DateOnly(2026, 8, 1), hired.InitialTerms.EffectiveFrom);
        Assert.Equal("worker.hired", _audit.Entries[0].Action);
        Assert.Equal(OrgId, _audit.Entries[0].OrgId);
    }

    [Fact]
    public async Task Employees_may_not_hire()
    {
        var useCase = UseCase(StubContext.OrgUser(OrgId, Role.Employee));

        await Assert.ThrowsAsync<ForbiddenException>(() => useCase.ExecuteAsync(
            "Maria", "maria@example.com", "DEU", new DateOnly(2026, 8, 1),
            "Engineer", 60_000m, 40, CancellationToken.None));

        Assert.Empty(_workers.Rows);
        Assert.Equal(0, _uow.Commits);
    }

    [Fact]
    public async Task Unsupported_countries_are_rejected_before_any_write()
    {
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => UseCase().ExecuteAsync(
            "Maria", "maria@example.com", "FRA", new DateOnly(2026, 8, 1),
            "Engineer", 60_000m, 40, CancellationToken.None));

        Assert.Contains("FRA", ex.Message);
        Assert.Empty(_workers.Rows);
        Assert.Equal(0, _uow.Commits);
    }

    [Fact]
    public async Task Invalid_terms_leave_no_orphaned_worker()
    {
        // WHY: the worker object is created before the terms object — if terms
        // validation throws, nothing was committed. This test pins the atomicity.
        await Assert.ThrowsAsync<DomainRuleException>(() => UseCase().ExecuteAsync(
            "Maria", "maria@example.com", "DEU", new DateOnly(2026, 8, 1),
            "", 60_000m, 40, CancellationToken.None));

        Assert.Equal(0, _uow.Commits);
        Assert.Empty(_audit.Entries);
    }
}
