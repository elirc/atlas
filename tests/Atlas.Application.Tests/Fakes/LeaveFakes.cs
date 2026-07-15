using Atlas.Application.Abstractions;
using Atlas.Application.Pagination;
using Atlas.Domain.Countries;
using Atlas.Domain.Leave;

namespace Atlas.Application.Tests.Fakes;

public sealed class InMemoryLeavePolicyRepository : ILeavePolicyRepository
{
    public List<LeavePolicy> Rows { get; } = [];

    public Task AddAsync(LeavePolicy policy, CancellationToken ct)
    {
        Rows.Add(policy);
        return Task.CompletedTask;
    }

    public Task<LeavePolicy?> FindForAsync(Guid orgId, CountryCode country, CancellationToken ct) =>
        Task.FromResult(Rows.FirstOrDefault(p => p.OrgId == orgId && p.Country == country));

    public Task<IReadOnlyList<LeavePolicy>> ListForOrgAsync(Guid orgId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LeavePolicy>>([.. Rows.Where(p => p.OrgId == orgId)]);
}

public sealed class InMemoryLeaveRequestRepository : ILeaveRequestRepository
{
    public List<LeaveRequest> Rows { get; } = [];

    public Task AddAsync(LeaveRequest request, CancellationToken ct)
    {
        Rows.Add(request);
        return Task.CompletedTask;
    }

    public Task<LeaveRequest?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken ct) =>
        Task.FromResult(Rows.FirstOrDefault(r => r.Id == id && r.OrgId == orgId));

    public Task<IReadOnlyList<LeaveRequest>> ListApprovedForWorkerAsync(Guid workerId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LeaveRequest>>(
            [.. Rows.Where(r => r.WorkerId == workerId && r.Status == LeaveRequestStatus.Approved)]);

    public Task<IReadOnlyList<LeaveRequest>> ListForOrgAsync(
        Guid orgId, TimelineCursor? after, int fetch, CancellationToken ct)
    {
        IEnumerable<LeaveRequest> ordered = Rows
            .Where(r => r.OrgId == orgId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ThenByDescending(r => r.Id);

        if (after is { } cursor)
        {
            ordered = ordered.Where(r =>
                r.CreatedAtUtc < cursor.CreatedAtUtc
                || (r.CreatedAtUtc == cursor.CreatedAtUtc && r.Id.CompareTo(cursor.Id) < 0));
        }

        IReadOnlyList<LeaveRequest> page = [.. ordered.Take(fetch)];
        return Task.FromResult(page);
    }
}
