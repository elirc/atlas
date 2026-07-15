using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Leave;
using Atlas.Domain.Workforce;

namespace Atlas.Application.Leave;

/// <summary>A worker's leave standing on a chosen calendar day. Derived, never stored.</summary>
public sealed record LeaveBalanceView(
    Guid WorkerId,
    string Country,
    DateOnly AsOf,
    decimal AnnualEntitlementDays,
    decimal AccruedDays,
    decimal SpentDays,
    decimal AvailableDays);

public sealed class GetLeaveBalance(
    IWorkerRepository workers,
    ILeavePolicyRepository policies,
    ILeaveRequestRepository requests,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<LeaveBalanceView> ExecuteAsync(
        Guid workerId,
        DateOnly? asOf,
        CancellationToken cancellationToken)
    {
        // Any org member may read a worker's balance — the SAME stance GetWorkerTerms
        // takes (worker reads are org-scoped, not role-gated); cross-org is a 404.
        context.RequireUser();
        Guid orgId = context.RequireOrg();

        Worker worker = await workers.FindInOrgAsync(workerId, orgId, cancellationToken)
            ?? throw new NotFoundException("worker", workerId);

        LeavePolicy policy = await policies.FindForAsync(orgId, worker.Country, cancellationToken)
            ?? throw new NotFoundException("leave policy", worker.Country.Value);

        // WHY: "today" is a calendar question; UTC is the least-wrong org-facing default —
        // the SAME stance GetWorkerTerms takes, and the one ADR-0007's Manila exercise
        // pokes at. The caller may pin an explicit asOf to ask "what will they have on X?".
        DateOnly on = asOf ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        // The ledger: sum the worker's approved requests in memory (never SUM decimals in
        // SQL). "Spent" includes future-dated approved leave — approved days are committed,
        // so they reduce what's available now. That is a modeling choice, noted in the ADR.
        IReadOnlyList<LeaveRequest> approved =
            await requests.ListApprovedForWorkerAsync(worker.Id, cancellationToken);
        decimal spent = approved.Sum(r => r.WorkingDays);

        decimal accrued = LeaveAccrual.AccruedThrough(policy, worker.HiredOn, on);

        return new LeaveBalanceView(
            worker.Id, worker.Country.Value, on, policy.AnnualEntitlementDays,
            accrued, spent, accrued - spent);
    }
}
