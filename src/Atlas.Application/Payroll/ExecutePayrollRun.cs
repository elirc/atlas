using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Events;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Payroll;

namespace Atlas.Application.Payroll;

/// <summary>
/// Approved → Executed. Records the run as final and announces it. "Executed" means the
/// payroll is committed to records — it does NOT move money; a payment provider does that
/// in S09. Idempotent: re-executing an already-executed run returns it unchanged.
/// </summary>
// 🔗 Idempotent execution is the pattern S10 turns into middleware ("today a guard in one
// use case; tomorrow, infrastructure every money-moving POST inherits"). The natural key
// (from prepare) plus the terminal-state guard (here) are the two halves: you cannot
// create a second run for the period, and you cannot execute the one run twice.
public sealed class ExecutePayrollRun(
    IPayrollRunRepository runs,
    IEventSink events,
    IAuditTrail audit,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<PayrollRun> ExecuteAsync(Guid runId, CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin);
        Guid orgId = context.RequireOrg();

        PayrollRun run = await runs.FindInOrgAsync(runId, orgId, cancellationToken)
            ?? throw new NotFoundException("payroll run", runId);

        // IDEMPOTENT EXECUTION. An already-executed run is a no-op that succeeds: no second
        // state write, no second audit row, no second event. This is what makes a retried
        // "execute" safe — the caller cannot tell (nor need to) whether theirs was the call
        // that executed it or a duplicate.
        if (run.State == PayrollRunState.Executed)
        {
            return run;
        }

        DateTimeOffset now = clock.GetUtcNow();
        run.Execute(now);

        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "payroll.run_executed", "payroll_run", run.Id,
            JsonSerializer.Serialize(new { run.PayCycleId, run.PeriodStart, ExecutedAtUtc = now }),
            now));

        await events.PublishAsync(
            new AtlasEvent(
                "payroll.run.executed", orgId, "payroll_run", run.Id,
                JsonSerializer.Serialize(new
                {
                    run.PayCycleId,
                    run.PeriodStart,
                    run.PeriodEnd,
                    ExecutedAtUtc = now,
                }),
                now),
            cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);

        return run;
    }
}
