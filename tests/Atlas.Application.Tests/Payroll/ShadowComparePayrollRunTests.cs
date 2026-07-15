using Atlas.Application.Payroll;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;
using Atlas.Domain.Workforce;

namespace Atlas.Application.Tests.Payroll;

/// <summary>
/// Shadow running end to end at the use-case level: the two gates (enrolment + a pending switch),
/// the redacted deltas-only report, the divergence event, and the clean case. The stored native
/// payslips are the baseline; a divergent LedgerPay stub is the trial vendor.
/// </summary>
public class ShadowComparePayrollRunTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid WorkerId = Guid.NewGuid();

    private readonly InMemoryPayCycleRepository _cycles = new();
    private readonly InMemoryPayrollRunRepository _runs = new();
    private readonly InMemoryEmploymentRecordRepository _records = new();
    private readonly InMemoryVendorAssignmentRepository _assignments = new();
    private readonly InMemoryVendorComparisonReportRepository _reports = new();
    private readonly StubUnpaidLeaveCalendar _leave = new(days: 0);
    private readonly StubShadowRunPolicy _policy = new() { DivergenceToleranceMinorUnits = 2 };
    private readonly RecordingAuditTrail _audit = new();
    private readonly RecordingEventSink _events = new();
    private readonly CountingUnitOfWork _uow = new();

    private readonly PayCycle _cycle =
        PayCycle.Create(OrgId, CountryCode.From("DEU"), PayFrequency.Monthly, Now);

    // LedgerPay stub that shaves €6.05 off net — a divergence well beyond the 2-minor-unit tolerance.
    private static readonly Func<PayrollCalculationInput, PayslipCalculation> DivergentLedgerPay = input =>
    {
        PayslipCalculation n = PayrollCalculator.Calculate(input);
        return new PayslipCalculation(n.Gross, n.Lines, Money.Of(n.Net.Amount - 6.05m, n.Net.Currency), n.EmployerCost);
    };

    private FakePayrollVendorDirectory Directory(Func<PayrollCalculationInput, PayslipCalculation> ledgerPay) =>
        new(new NativeCalculationVendor(), new StubCalculationVendor(PayrollVendor.LedgerPay, ledgerPay));

    private ShadowComparePayrollRun UseCase(FakePayrollVendorDirectory directory) => new(
        _runs, _cycles, _assignments, directory, _records, _leave, _policy, _reports,
        _audit, _events, _uow, StubContext.OrgUser(OrgId, Role.OrgAdmin, UserId), new FixedClock(Now));

    private PayrollRun SeedNativeRun()
    {
        _cycles.Rows.Add(_cycle);
        _records.Rows.Add(EmploymentRecord.Create(
            WorkerId, new DateOnly(2026, 1, 1), "Engineer", 60_000m, "EUR", 40, UserId, Now));

        PayPeriod period = PayPeriod.ForMonth(2026, 7);
        var run = PayrollRun.Create(OrgId, _cycle.Id, period, UserId, PayrollVendor.AtlasNative, Now);

        PayslipCalculation native = PayrollCalculator.Calculate(new PayrollCalculationInput(
            [.. _records.Rows], CountryRulePacks.For(CountryCode.From("DEU")), period, 0));
        run.AddPayslip(Payslip.FromCalculation(run.Id, WorkerId, OrgId, period, native, Now));

        _runs.Rows.Add(run);
        return run;
    }

    private void EnrolAndQueueSwitch()
    {
        _policy.Enrolled.Add(OrgId);
        // A pending switch to LedgerPay, effective AFTER today — this is what shadow mode trials.
        _assignments.Rows.Add(VendorAssignment.Create(
            OrgId, CountryCode.From("DEU"), new DateOnly(2026, 8, 1), PayrollVendor.LedgerPay, UserId, Now));
    }

    [Fact]
    public async Task Enrolled_with_a_pending_switch_produces_a_redacted_divergence_report()
    {
        PayrollRun run = SeedNativeRun();
        EnrolAndQueueSwitch();

        VendorComparisonReport report = await UseCase(Directory(DivergentLedgerPay))
            .ExecuteAsync(run.Id, CancellationToken.None);

        Assert.Equal(PayrollVendor.AtlasNative, report.PrimaryVendor);
        Assert.Equal(PayrollVendor.LedgerPay, report.ShadowVendor);
        Assert.Equal(1, report.WorkerCount);
        Assert.Equal(1, report.DivergentCount);
        Assert.False(report.IsClean);

        // DELTAS ONLY: the line carries the -605 minor-unit net delta, and there is no field on it
        // that could hold either vendor's absolute net pay (redaction is the type shape).
        VendorComparisonLine line = Assert.Single(report.Lines);
        Assert.Equal(-605, line.NetDeltaMinor);
        Assert.True(line.Divergent);

        Assert.Single(_reports.Rows);
        Assert.Equal(1, _uow.Commits);
        Assert.Contains(_audit.Entries, e => e.Action == "payroll.shadow_compared");
        Assert.Contains(_events.Published, e => e.Type == "payroll.shadow_divergence_detected");
    }

    [Fact]
    public async Task A_clean_comparison_publishes_no_divergence_event()
    {
        PayrollRun run = SeedNativeRun();
        EnrolAndQueueSwitch();

        // LedgerPay stub == native exactly → no divergence.
        VendorComparisonReport report = await UseCase(Directory(PayrollCalculator.Calculate))
            .ExecuteAsync(run.Id, CancellationToken.None);

        Assert.True(report.IsClean);
        Assert.Equal(0, report.DivergentCount);
        Assert.DoesNotContain(_events.Published, e => e.Type == "payroll.shadow_divergence_detected");
        // The comparison itself is still audited and persisted, even when clean.
        Assert.Single(_reports.Rows);
        Assert.Contains(_audit.Entries, e => e.Action == "payroll.shadow_compared");
    }

    [Fact]
    public async Task An_un_enrolled_org_is_gated_out()
    {
        PayrollRun run = SeedNativeRun();
        // Queue the switch but do NOT enrol the org.
        _assignments.Rows.Add(VendorAssignment.Create(
            OrgId, CountryCode.From("DEU"), new DateOnly(2026, 8, 1), PayrollVendor.LedgerPay, UserId, Now));

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            UseCase(Directory(DivergentLedgerPay)).ExecuteAsync(run.Id, CancellationToken.None));
        Assert.Empty(_reports.Rows);
        Assert.Equal(0, _uow.Commits);
    }

    [Fact]
    public async Task With_no_pending_switch_the_comparison_has_auto_expired()
    {
        PayrollRun run = SeedNativeRun();
        _policy.Enrolled.Add(OrgId); // enrolled, but no pending assignment queued

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            UseCase(Directory(DivergentLedgerPay)).ExecuteAsync(run.Id, CancellationToken.None));
        Assert.Empty(_reports.Rows);
    }
}
