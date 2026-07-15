using Atlas.Application.Payroll;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain.Access;
using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;
using Atlas.Domain.Workforce;

namespace Atlas.Application.Tests.Payroll;

/// <summary>
/// The cycle-in-flight rule: a run pins its vendor at CREATION, from the effective-dated assignment
/// log, and never re-resolves. A switch effective in the future does not touch a run created today;
/// an idempotent re-prepare returns the existing run with its original pin, unmoved.
/// </summary>
public class PreparePayrollRunVendorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly InMemoryPayCycleRepository _cycles = new();
    private readonly InMemoryPayrollRunRepository _runs = new();
    private readonly InMemoryWorkerRepository _workers = new();
    private readonly InMemoryEmploymentRecordRepository _records = new();
    private readonly InMemoryVendorAssignmentRepository _assignments = new();
    private readonly StubUnpaidLeaveCalendar _leave = new(days: 0);
    private readonly RecordingAuditTrail _audit = new();
    private readonly CountingUnitOfWork _uow = new();

    // Both vendors resolvable; the LedgerPay stub reuses the native maths (the pin, not the numbers,
    // is what these tests assert).
    private readonly FakePayrollVendorDirectory _directory = new(
        new NativeCalculationVendor(),
        new StubCalculationVendor(PayrollVendor.LedgerPay, PayrollCalculator.Calculate));

    private readonly PayCycle _cycle =
        PayCycle.Create(OrgId, CountryCode.From("DEU"), PayFrequency.Monthly, Now);

    private PreparePayrollRun UseCase() => new(
        _cycles, _runs, _workers, _records, _assignments, _directory, _leave, _audit, _uow,
        StubContext.OrgUser(OrgId, Role.OrgAdmin, UserId), new FixedClock(Now));

    private void SeedCycleAndWorker()
    {
        _cycles.Rows.Add(_cycle);
        var worker = Worker.Hire(
            OrgId, "ann", EmailAddress.From("ann@example.com"),
            CountryCode.From("DEU"), new DateOnly(2026, 1, 1), Now);
        _workers.Rows.Add(worker);
        _records.Rows.Add(EmploymentRecord.Create(
            worker.Id, new DateOnly(2026, 1, 1), "Engineer", 60_000m, "EUR", 40, UserId, Now));
    }

    private void Assign(PayrollVendor vendor, DateOnly effectiveFrom) =>
        _assignments.Rows.Add(VendorAssignment.Create(
            OrgId, CountryCode.From("DEU"), effectiveFrom, vendor, UserId, Now));

    [Fact]
    public async Task With_no_assignment_the_run_pins_the_native_fallback()
    {
        SeedCycleAndWorker();

        PreparedRun prepared = await UseCase().ExecuteAsync(_cycle.Id, 2026, 7, CancellationToken.None);

        Assert.Equal(PayrollVendor.AtlasNative, prepared.Run.SelectedVendor);
        Assert.NotEmpty(prepared.Run.Payslips);
    }

    [Fact]
    public async Task An_already_effective_assignment_pins_that_vendor()
    {
        SeedCycleAndWorker();
        Assign(PayrollVendor.LedgerPay, new DateOnly(2026, 1, 1)); // effective before Now

        PreparedRun prepared = await UseCase().ExecuteAsync(_cycle.Id, 2026, 7, CancellationToken.None);

        Assert.Equal(PayrollVendor.LedgerPay, prepared.Run.SelectedVendor);
    }

    [Fact]
    public async Task A_future_switch_does_not_touch_a_run_created_before_it()
    {
        SeedCycleAndWorker();
        Assign(PayrollVendor.LedgerPay, new DateOnly(2026, 8, 1)); // effective AFTER Now (2026-07-01)

        PreparedRun prepared = await UseCase().ExecuteAsync(_cycle.Id, 2026, 7, CancellationToken.None);

        // The pending switch is in the future — the run pins the vendor CURRENT at creation (native).
        Assert.Equal(PayrollVendor.AtlasNative, prepared.Run.SelectedVendor);
    }

    [Fact]
    public async Task Re_preparing_returns_the_existing_run_without_re_resolving_the_vendor()
    {
        SeedCycleAndWorker();

        // First prepare pins native (no assignment yet).
        PreparedRun first = await UseCase().ExecuteAsync(_cycle.Id, 2026, 7, CancellationToken.None);
        Assert.Equal(PayrollVendor.AtlasNative, first.Run.SelectedVendor);

        // Now a LedgerPay assignment lands, effective in the past — but the run already exists.
        Assign(PayrollVendor.LedgerPay, new DateOnly(2026, 1, 1));
        PreparedRun second = await UseCase().ExecuteAsync(_cycle.Id, 2026, 7, CancellationToken.None);

        // Idempotent: same run, still pinned to native — the pin is authoritative, never re-resolved.
        Assert.True(second.AlreadyExisted);
        Assert.Equal(first.Run.Id, second.Run.Id);
        Assert.Equal(PayrollVendor.AtlasNative, second.Run.SelectedVendor);
    }
}
