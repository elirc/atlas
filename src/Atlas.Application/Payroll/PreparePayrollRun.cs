using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;
using Atlas.Domain.Workforce;

namespace Atlas.Application.Payroll;

/// <summary>The prepared run plus whether it already existed (drives 201-new vs 200-existing).</summary>
public sealed record PreparedRun(PayrollRun Run, bool AlreadyExisted);

/// <summary>
/// Prepare a payroll run for a cycle's period: create the run and compute a payslip for
/// EVERY active worker of the org in that country. Idempotent by natural key
/// (PayCycleId, PeriodStart) — preparing the same period again returns the EXISTING run,
/// untouched, and computes nothing new.
/// </summary>
// 📘 The composition ROOT'S root. This use case is where S03 (terms history), S04 (Money +
// packs) and S06 (leave) are gathered and handed to the pure calculator, once per worker,
// inside one unit of work: the run, all its payslips, all their lines, and the audit row
// commit together or not at all.
public sealed class PreparePayrollRun(
    IPayCycleRepository cycles,
    IPayrollRunRepository runs,
    IWorkerRepository workers,
    IEmploymentRecordRepository records,
    IUnpaidLeaveCalendar leave,
    IAuditTrail audit,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<PreparedRun> ExecuteAsync(
        Guid cycleId,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin);
        Guid orgId = context.RequireOrg();

        PayCycle cycle = await cycles.FindInOrgAsync(cycleId, orgId, cancellationToken)
            ?? throw new NotFoundException("pay cycle", cycleId);

        PayPeriod period = cycle.PeriodFor(year, month);

        // IDEMPOTENT CREATION. (cycleId, periodStart) is the run's identity, so preparing
        // "March" twice is the same run — return the existing one and write nothing. The
        // unique index (PayCycleId, PeriodStart) is the real guarantee under a race; this
        // query is the friendly, common-case path. Full idempotency-key handling for
        // money-moving POSTs matures into middleware in S10.
        PayrollRun? existing =
            await runs.FindByNaturalKeyAsync(cycle.Id, period.Start, cancellationToken);
        if (existing is not null)
        {
            return new PreparedRun(existing, AlreadyExisted: true);
        }

        DateTimeOffset now = clock.GetUtcNow();
        var run = PayrollRun.Create(orgId, cycle.Id, period, context.UserId, now);

        ICountryRulePack pack = CountryRulePacks.For(cycle.Country);

        // "Active" = hired on or before the period end (see IWorkerRepository). When S05's
        // engagement states are integrated this tightens to "engagement Active on the
        // period" — refinement recorded in NOTES/ADR-0008.
        IReadOnlyList<Worker> active =
            await workers.ListActiveInCountryAsync(orgId, cycle.Country, period.End, cancellationToken);

        foreach (Worker worker in active)
        {
            IReadOnlyList<EmploymentRecord> history =
                await records.ListForWorkerAsync(worker.Id, cancellationToken);

            // Defensive: an active worker always has initial terms from HiredOn, so this is
            // belt-and-suspenders — but a worker with no terms as of period end has nothing
            // to pay, and the calculator would (correctly) throw. Skip rather than fail the
            // whole run for one anomaly.
            if (EmploymentTerms.Resolve(history, period.End) is null)
            {
                continue;
            }

            int unpaidDays = await leave.ApprovedUnpaidDaysAsync(
                worker.Id, period.Start, period.End, cancellationToken);

            PayslipCalculation calculation = PayrollCalculator.Calculate(
                new PayrollCalculationInput(history, pack, period, unpaidDays));

            run.AddPayslip(Payslip.FromCalculation(
                run.Id, worker.Id, orgId, period, calculation, now));
        }

        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "payroll.run_prepared", "payroll_run", run.Id,
            JsonSerializer.Serialize(new
            {
                CycleId = cycle.Id,
                Country = cycle.Country.Value,
                PeriodStart = period.Start,
                PeriodEnd = period.End,
                PayslipCount = run.Payslips.Count,
            }),
            now));

        await runs.AddAsync(run, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return new PreparedRun(run, AlreadyExisted: false);
    }
}
