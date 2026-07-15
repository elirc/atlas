using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Events;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;
using Atlas.Domain.Workforce;

namespace Atlas.Application.Payroll;

/// <summary>
/// SHADOW RUN: recompute an already-prepared run with the vendor the org is about to switch to,
/// diff it worker-by-worker against the stored (pinned) figures, and persist a redacted
/// comparison report — deltas only, never a second copy of anyone's net pay. Gated to enrolled
/// orgs with a pending switch, so the doubled compute and the sensitive comparison exist only
/// where a migration is actually in flight.
/// </summary>
// 📘 HOW REAL MIGRATIONS DE-RISK (ADR-0014). You do not flip a client to LedgerPay and hope; you
// run LedgerPay in the SHADOW of the run you already computed, store where the two disagree, and
// let finance read it before a single worker's pay depends on the new engine. The primary vendor
// already ran at prepare time — its numbers are the frozen payslips on the run — so shadow mode
// recomputes ONLY the other vendor and diffs. Doubling compute is therefore opt-in and bounded:
// enrolled orgs only, and only while there is a pending switch to trial (the gate auto-expires the
// day the flip becomes effective — there is nothing left to shadow).
//
// 🔍 Everything sensitive is redacted at the domain boundary (VendorComparison.DeltaOf): this use
// case never holds two absolute nets long enough to store one. The report it persists has no field
// that could carry a salary — it carries signed minute deltas and a divergence verdict. That is the
// Reviewer's redaction win expressed as a data-flow, not a rule someone has to remember to follow.
public sealed class ShadowComparePayrollRun(
    IPayrollRunRepository runs,
    IPayCycleRepository cycles,
    IVendorAssignmentRepository vendorAssignments,
    IPayrollVendorDirectory vendorDirectory,
    IEmploymentRecordRepository records,
    IUnpaidLeaveCalendar leave,
    IShadowRunPolicy shadowPolicy,
    IVendorComparisonReportRepository reports,
    IAuditTrail audit,
    IEventSink events,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<VendorComparisonReport> ExecuteAsync(Guid runId, CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin);
        Guid orgId = context.RequireOrg();

        PayrollRun run = await runs.FindInOrgAsync(runId, orgId, cancellationToken)
            ?? throw new NotFoundException("payroll run", runId);

        PayCycle cycle = await cycles.FindInOrgAsync(run.PayCycleId, orgId, cancellationToken)
            ?? throw new NotFoundException("pay cycle", run.PayCycleId);

        DateTimeOffset now = clock.GetUtcNow();
        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);

        // ── GATE 1: enrolment + time-box (the Reviewer's gating win) ────────────────────────────
        if (!shadowPolicy.IsEnrolled(orgId, today))
        {
            throw new DomainRuleException(
                "Shadow comparison is not enabled for this org (not enrolled, or the trial window has closed).");
        }

        // ── GATE 2: there must be a pending switch to trial (auto-expiry after the flip) ─────────
        IReadOnlyList<VendorAssignment> assignments =
            await vendorAssignments.ListForOrgCountryAsync(orgId, cycle.Country, cancellationToken);
        VendorAssignment? pending = VendorSelection.PendingSwitch(assignments, today);
        if (pending is null)
        {
            throw new DomainRuleException(
                "No pending vendor switch for this country to shadow — comparison auto-expired once the flip took effect.");
        }

        PayrollVendor shadowVendor = pending.Vendor;
        if (shadowVendor == run.SelectedVendor)
        {
            // The pending switch is to the SAME vendor the run already used — nothing to compare.
            throw new DomainRuleException(
                $"The pending vendor ({shadowVendor}) matches the run's vendor; there is nothing to shadow.");
        }

        IPayrollCalculationVendor shadowCalculator = vendorDirectory.Resolve(shadowVendor);
        ICountryRulePack pack = CountryRulePacks.For(cycle.Country);
        var period = new PayPeriod(run.PeriodStart, run.PeriodEnd);

        // ── Run the shadow vendor over the SAME workers and diff against the stored payslips ─────
        var deltas = new List<VendorDelta>();
        foreach (Payslip payslip in run.Payslips)
        {
            IReadOnlyList<EmploymentRecord> history =
                await records.ListForWorkerAsync(payslip.WorkerId, cancellationToken);

            int unpaidDays = await leave.ApprovedUnpaidDaysAsync(
                payslip.WorkerId, run.PeriodStart, run.PeriodEnd, cancellationToken);

            PayslipCalculation shadow = await shadowCalculator.CalculateAsync(
                new PayrollCalculationInput(history, pack, period, unpaidDays), cancellationToken);

            // REDACTION HAPPENS HERE: two absolute payslips in, one delta out (see VendorComparison).
            deltas.Add(VendorComparison.DeltaOf(payslip, shadow));
        }

        VendorComparisonReport report = VendorComparisonReport.Build(
            run.Id, orgId, cycle.Country, run.SelectedVendor, shadowVendor,
            shadowPolicy.DivergenceToleranceMinorUnits, deltas, now);

        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "payroll.shadow_compared", "payroll_run", run.Id,
            JsonSerializer.Serialize(new
            {
                PrimaryVendor = run.SelectedVendor.ToString(),
                ShadowVendor = shadowVendor.ToString(),
                report.WorkerCount,
                report.DivergentCount,
                report.MaxAbsNetDeltaMinor,
                report.ToleranceMinorUnits,
            }),
            now));

        // A divergence is news finance must act on before flipping — announce it (deltas only, no
        // net pay). Published BEFORE commit; the S10 outbox makes that ordering correct (ADR-0011).
        if (report.DivergentCount > 0)
        {
            await events.PublishAsync(
                new AtlasEvent(
                    "payroll.shadow_divergence_detected", orgId, "payroll_run", run.Id,
                    JsonSerializer.Serialize(new
                    {
                        ShadowVendor = shadowVendor.ToString(),
                        report.DivergentCount,
                        report.MaxAbsNetDeltaMinor,
                    }),
                    now),
                cancellationToken);
        }

        await reports.AddAsync(report, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return report;
    }
}
