using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Payroll;

namespace Atlas.Application.Payroll;

// 🔍 Three transitions, one shape: load the run tenant-safely, ask the aggregate to make
// the move (the STATE guard decides legality — an illegal move is a 422 from the domain,
// not an if-ladder here), audit the fact, commit. The role check gates WHO may attempt a
// move; the state machine gates WHICH move is legal from here. Authorization is both.

/// <summary>Draft → InReview. Submit a prepared run for a human to check the numbers.</summary>
public sealed class SubmitPayrollRun(
    IPayrollRunRepository runs,
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

        run.SubmitForReview();

        DateTimeOffset now = clock.GetUtcNow();
        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "payroll.run_submitted", "payroll_run", run.Id,
            JsonSerializer.Serialize(new { State = run.State.ToString() }), now));

        await unitOfWork.CommitAsync(cancellationToken);
        return run;
    }
}

/// <summary>InReview → Approved. A reviewer signs off on the figures.</summary>
public sealed class ApprovePayrollRun(
    IPayrollRunRepository runs,
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

        run.Approve();

        DateTimeOffset now = clock.GetUtcNow();
        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "payroll.run_approved", "payroll_run", run.Id,
            JsonSerializer.Serialize(new { State = run.State.ToString() }), now));

        await unitOfWork.CommitAsync(cancellationToken);
        return run;
    }
}

/// <summary>InReview → Draft. Send a run back to be fixed; the audit log keeps the rejection.</summary>
public sealed class RejectPayrollRun(
    IPayrollRunRepository runs,
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

        run.Reject();

        DateTimeOffset now = clock.GetUtcNow();
        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "payroll.run_rejected", "payroll_run", run.Id,
            JsonSerializer.Serialize(new { State = run.State.ToString() }), now));

        await unitOfWork.CommitAsync(cancellationToken);
        return run;
    }
}
