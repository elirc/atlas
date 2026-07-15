using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Leave;
using Atlas.Domain.Workforce;

namespace Atlas.Application.Leave;

/// <summary>
/// Approve a submitted leave request — but only if it neither overlaps an already-approved
/// request nor overdraws the worker's balance. Both guards run BEFORE the state change.
/// </summary>
public sealed class ApproveLeaveRequest(
    ILeaveRequestRepository requests,
    ILeavePolicyRepository policies,
    IWorkerRepository workers,
    IAuditTrail audit,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<LeaveRequest> ExecuteAsync(Guid requestId, CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        LeaveRequest request = await requests.FindInOrgAsync(requestId, orgId, cancellationToken)
            ?? throw new NotFoundException("leave request", requestId);

        Worker worker = await workers.FindInOrgAsync(request.WorkerId, orgId, cancellationToken)
            ?? throw new NotFoundException("worker", request.WorkerId);

        LeavePolicy policy = await policies.FindForAsync(orgId, worker.Country, cancellationToken)
            ?? throw new DomainRuleException(
                $"No leave policy is configured for {worker.Country}; approve it once a policy exists.");

        IReadOnlyList<LeaveRequest> approved =
            await requests.ListApprovedForWorkerAsync(worker.Id, cancellationToken);

        // WHY: overlap first — you cannot be on approved leave twice for the same day, and
        // that check needs no balance math. (The current request is still Submitted, so it
        // is NOT in `approved` — no need to exclude self.)
        LeaveRequest? clash = approved.FirstOrDefault(r => r.Overlaps(request.From, request.To));
        if (clash is not null)
        {
            throw new DomainRuleException(
                $"This leave overlaps an already-approved request " +
                $"({clash.From:yyyy-MM-dd}–{clash.To:yyyy-MM-dd}).");
        }

        // Then balance — the pure engine folds accrual (as of the leave's START date) and
        // subtracts days already approved. Approving must not overdraw the worker.
        // LEARN: decimals are SUMMED in memory, never in SQL (the SQLite/precision rule) —
        // the repository handed us the rows; the arithmetic is ours.
        decimal spent = approved.Sum(r => r.WorkingDays);
        decimal available = LeaveAccrual.BalanceOn(policy, worker.HiredOn, request.From, spent);
        if (request.WorkingDays > available)
        {
            throw new DomainRuleException(
                $"Insufficient leave balance: {request.WorkingDays:0.##} working days requested, " +
                $"{available:0.##} available as of {request.From:yyyy-MM-dd}.");
        }

        DateTimeOffset now = clock.GetUtcNow();
        request.Approve(context.UserId, now);

        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "leave.approved", "leave_request", request.Id,
            JsonSerializer.Serialize(new
            {
                WorkerId = worker.Id,
                request.WorkingDays,
                AvailableBefore = available,
            }),
            now));

        await unitOfWork.CommitAsync(cancellationToken);

        return request;
    }
}
