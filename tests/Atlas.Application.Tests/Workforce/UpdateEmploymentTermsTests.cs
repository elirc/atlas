using Atlas.Application.Errors;
using Atlas.Application.Tests.Fakes;
using Atlas.Application.Workforce;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Countries;
using Atlas.Domain.Workforce;

namespace Atlas.Application.Tests.Workforce;

public class UpdateEmploymentTermsTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrgId = Guid.NewGuid();

    private readonly InMemoryWorkerRepository _workers = new();
    private readonly InMemoryEmploymentRecordRepository _records = new();
    private readonly RecordingAuditTrail _audit = new();
    private readonly CountingUnitOfWork _uow = new();
    private readonly Worker _worker;

    public UpdateEmploymentTermsTests()
    {
        _worker = Worker.Hire(
            OrgId, "Maria Santos", EmailAddress.From("maria@example.com"),
            CountryCode.From("DEU"), new DateOnly(2026, 3, 1), Now);
        _workers.Rows.Add(_worker);
        _records.Rows.Add(EmploymentRecord.Create(
            _worker.Id, new DateOnly(2026, 3, 1), "Engineer", 60_000m, "EUR", 40,
            Guid.NewGuid(), Now));
    }

    private UpdateEmploymentTerms UseCase(StubContext? ctx = null) => new(
        _workers, _records, _audit, _uow,
        ctx ?? StubContext.OrgUser(OrgId, Role.HrManager), new FixedClock(Now));

    [Fact]
    public async Task A_change_appends_a_record_and_never_mutates_history()
    {
        var appended = await UseCase().ExecuteAsync(
            _worker.Id, new DateOnly(2026, 9, 1), "Senior Engineer", 75_000m, 40,
            CancellationToken.None);

        Assert.Equal(2, _records.Rows.Count);
        // The original record is untouched — same title, same salary.
        Assert.Equal(60_000m, _records.Rows[0].AnnualSalary);
        // Currency carries over from the previous record.
        Assert.Equal("EUR", appended.SalaryCurrency);
        Assert.Equal(1, _uow.Commits);

        Assert.Equal("worker.terms_changed", _audit.Entries.Single().Action);
        Assert.Contains("Before", _audit.Entries[0].DetailsJson);
        Assert.Contains("After", _audit.Entries[0].DetailsJson);
    }

    [Fact]
    public async Task Terms_before_the_hire_date_are_rejected()
    {
        await Assert.ThrowsAsync<DomainRuleException>(() => UseCase().ExecuteAsync(
            _worker.Id, new DateOnly(2026, 2, 1), "Time Traveler", 75_000m, 40,
            CancellationToken.None));
    }

    [Fact]
    public async Task Duplicate_effective_dates_are_rejected()
    {
        await Assert.ThrowsAsync<DomainRuleException>(() => UseCase().ExecuteAsync(
            _worker.Id, new DateOnly(2026, 3, 1), "Engineer II", 62_000m, 40,
            CancellationToken.None));

        Assert.Single(_records.Rows);
    }

    [Fact]
    public async Task Another_orgs_worker_does_not_exist_for_this_caller()
    {
        var useCase = UseCase(StubContext.OrgUser(Guid.NewGuid(), Role.OrgAdmin));

        await Assert.ThrowsAsync<NotFoundException>(() => useCase.ExecuteAsync(
            _worker.Id, new DateOnly(2026, 9, 1), "Sneaky", 1m, 40, CancellationToken.None));
    }

    [Fact]
    public async Task Employees_may_not_change_terms()
    {
        var useCase = UseCase(StubContext.OrgUser(OrgId, Role.Employee));

        await Assert.ThrowsAsync<ForbiddenException>(() => useCase.ExecuteAsync(
            _worker.Id, new DateOnly(2026, 9, 1), "Self Promotion", 999_999m, 40,
            CancellationToken.None));
    }
}
