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
/// Prepare a payroll run for a cycle's period: resolve the org+country's vendor, PIN it on the
/// run, and compute a payslip for EVERY active worker of the org in that country through that
/// vendor. Idempotent by natural key (PayCycleId, PeriodStart) — preparing the same period again
/// returns the EXISTING run, untouched, and computes nothing new (nor re-resolves the vendor).
/// </summary>
// 📘 The composition ROOT'S root — now with a vendor seam. This use case is where S03 (terms
// history), S04 (Money + packs) and S06 (leave) are gathered and handed to a calculation VENDOR,
// once per worker, inside one unit of work. S12's single change to the hot path: the pure
// PayrollCalculator.Calculate(...) call became `await vendor.CalculateAsync(...)`, where `vendor`
// is resolved from the run's pinned SelectedVendor. That is the entire blast radius of "we now
// have two payroll engines" on this file — one resolution up front, one await in the loop. The
// locality table in the PR measures exactly this.
//
// ⚠️ THE VENDOR IS PINNED HERE, AT CREATION (the cycle-in-flight rule). We resolve the vendor
// once, as of the creation DAY, from the effective-dated assignment log, and hand it to
// PayrollRun.Create — which stores it. Every payslip in this run is computed with that pinned
// vendor. A mid-quarter switch effective tomorrow does not touch a run created today: it will be
// picked up by the NEXT period's run. Resolving per-worker, or at execute time, is the bug this
// avoids — it would let a flip rewrite an open cycle's engine under a reviewer's feet.
public sealed class PreparePayrollRun(
    IPayCycleRepository cycles,
    IPayrollRunRepository runs,
    IWorkerRepository workers,
    IEmploymentRecordRepository records,
    IVendorAssignmentRepository vendorAssignments,
    IPayrollVendorDirectory vendorDirectory,
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
        // "March" twice is the same run — return the existing one and write nothing (and DO NOT
        // re-resolve the vendor: the existing run's pin is authoritative). The unique index
        // (PayCycleId, PeriodStart) is the real guarantee under a race; this query is the
        // friendly, common-case path.
        PayrollRun? existing =
            await runs.FindByNaturalKeyAsync(cycle.Id, period.Start, cancellationToken);
        if (existing is not null)
        {
            return new PreparedRun(existing, AlreadyExisted: true);
        }

        DateTimeOffset now = clock.GetUtcNow();

        // ── Resolve the vendor ONCE, as of the creation day, and pin it (see the ⚠️ above) ──
        // Every org starts on AtlasNative and stays there until an effective-dated assignment
        // says otherwise; an org that never touched vendor selection loads an empty history and
        // resolves to the fallback — a single cheap read on the common path.
        IReadOnlyList<VendorAssignment> assignments =
            await vendorAssignments.ListForOrgCountryAsync(orgId, cycle.Country, cancellationToken);
        PayrollVendor vendor = VendorSelection.Resolve(
            assignments, DateOnly.FromDateTime(now.UtcDateTime), fallback: PayrollVendor.AtlasNative);

        var run = PayrollRun.Create(orgId, cycle.Id, period, context.UserId, vendor, now);

        IPayrollCalculationVendor calculator = vendorDirectory.Resolve(vendor);
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

            PayslipCalculation calculation = await calculator.CalculateAsync(
                new PayrollCalculationInput(history, pack, period, unpaidDays), cancellationToken);

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
                Vendor = vendor.ToString(),
                PayslipCount = run.Payslips.Count,
            }),
            now));

        await runs.AddAsync(run, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return new PreparedRun(run, AlreadyExisted: false);
    }
}
