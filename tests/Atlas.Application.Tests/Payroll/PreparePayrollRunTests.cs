using Atlas.Application.Errors;
using Atlas.Application.Payroll;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain.Access;
using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;
using Atlas.Domain.Workforce;

namespace Atlas.Application.Tests.Payroll;

public class PreparePayrollRunTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly InMemoryPayCycleRepository _cycles = new();
    private readonly InMemoryPayrollRunRepository _runs = new();
    private readonly InMemoryWorkerRepository _workers = new();
    private readonly InMemoryEmploymentRecordRepository _records = new();
    // S12: PreparePayrollRun now resolves + pins a vendor. With no assignment seeded, the resolver
    // falls back to AtlasNative, and the directory resolves it to the pure engine — so these
    // pre-S12 behaviour tests stay behaviour-identical (only the two new constructor args are added).
    private readonly InMemoryVendorAssignmentRepository _vendorAssignments = new();
    private readonly FakePayrollVendorDirectory _vendorDirectory = FakePayrollVendorDirectory.Native();
    private readonly StubUnpaidLeaveCalendar _leave = new(days: 0);
    private readonly RecordingAuditTrail _audit = new();
    private readonly CountingUnitOfWork _uow = new();

    private readonly PayCycle _cycle =
        PayCycle.Create(OrgId, CountryCode.From("DEU"), PayFrequency.Monthly, Now);

    private PreparePayrollRun UseCase(StubContext? ctx = null) => new(
        _cycles, _runs, _workers, _records, _vendorAssignments, _vendorDirectory, _leave, _audit, _uow,
        ctx ?? StubContext.OrgUser(OrgId, Role.OrgAdmin, UserId),
        new FixedClock(Now));

    private void SeedCycle() => _cycles.Rows.Add(_cycle);

    private Worker SeedWorker(string name, DateOnly hiredOn, decimal salary = 60_000m)
    {
        var worker = Worker.Hire(
            OrgId, name, EmailAddress.From($"{name}@example.com"),
            CountryCode.From("DEU"), hiredOn, Now);
        _workers.Rows.Add(worker);
        _records.Rows.Add(EmploymentRecord.Create(
            worker.Id, hiredOn, "Engineer", salary, "EUR", 40, UserId, Now));
        return worker;
    }

    [Fact]
    public async Task Prepares_a_run_with_one_payslip_per_active_worker()
    {
        SeedCycle();
        SeedWorker("ann", new DateOnly(2026, 1, 1));
        SeedWorker("ben", new DateOnly(2026, 2, 1));

        PreparedRun prepared = await UseCase().ExecuteAsync(_cycle.Id, 2026, 3, CancellationToken.None);

        Assert.False(prepared.AlreadyExisted);
        Assert.Equal(PayrollRunState.Draft, prepared.Run.State);
        Assert.Equal(PayrollVendor.AtlasNative, prepared.Run.SelectedVendor);
        Assert.Equal(2, prepared.Run.Payslips.Count);
        Assert.Single(_runs.Rows);
        Assert.Equal(1, _uow.Commits);
        Assert.Contains(_audit.Entries, e => e.Action == "payroll.run_prepared");
    }

    [Fact]
    public async Task Preparing_the_same_period_twice_returns_the_existing_run()
    {
        SeedCycle();
        SeedWorker("ann", new DateOnly(2026, 1, 1));

        PreparedRun first = await UseCase().ExecuteAsync(_cycle.Id, 2026, 3, CancellationToken.None);
        PreparedRun second = await UseCase().ExecuteAsync(_cycle.Id, 2026, 3, CancellationToken.None);

        // Idempotent by natural key (cycle, period start): same run, nothing duplicated.
        Assert.False(first.AlreadyExisted);
        Assert.True(second.AlreadyExisted);
        Assert.Equal(first.Run.Id, second.Run.Id);
        Assert.Single(_runs.Rows);
        // The second call wrote nothing — no second commit, no second audit row.
        Assert.Equal(1, _uow.Commits);
        Assert.Single(_audit.Entries);
    }

    [Fact]
    public async Task Excludes_workers_hired_after_the_period_end()
    {
        SeedCycle();
        SeedWorker("early", new DateOnly(2026, 1, 1));
        SeedWorker("future", new DateOnly(2026, 4, 15)); // hired AFTER March closes

        PreparedRun prepared = await UseCase().ExecuteAsync(_cycle.Id, 2026, 3, CancellationToken.None);

        // "Active" = hired on or before the period end (2026-03-31).
        Assert.Single(prepared.Run.Payslips);
    }

    [Fact]
    public async Task Wires_unpaid_leave_days_into_the_payslip()
    {
        SeedCycle();
        Worker worker = SeedWorker("carol", new DateOnly(2026, 1, 1), salary: 60_000m);
        _leave.Days = 3; // three approved unpaid days this period

        PreparedRun prepared = await UseCase().ExecuteAsync(_cycle.Id, 2026, 3, CancellationToken.None);

        Payslip slip = prepared.Run.Payslips.Single(p => p.WorkerId == worker.Id);
        Assert.Contains(slip.Lines, l => l.Kind == PayslipLineKind.UnpaidLeave);
    }

    [Fact]
    public async Task Only_org_admins_may_prepare()
    {
        SeedCycle();

        var useCase = UseCase(StubContext.OrgUser(OrgId, Role.Employee, UserId));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            useCase.ExecuteAsync(_cycle.Id, 2026, 3, CancellationToken.None));
        Assert.Equal(0, _uow.Commits);
    }

    [Fact]
    public async Task A_missing_cycle_is_a_404()
    {
        // No cycle seeded.
        await Assert.ThrowsAsync<NotFoundException>(() =>
            UseCase().ExecuteAsync(Guid.NewGuid(), 2026, 3, CancellationToken.None));
    }

    [Fact]
    public async Task A_cycle_in_another_org_is_a_404()
    {
        SeedCycle();

        var otherOrg = UseCase(StubContext.OrgUser(Guid.NewGuid(), Role.OrgAdmin));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            otherOrg.ExecuteAsync(_cycle.Id, 2026, 3, CancellationToken.None));
    }
}
