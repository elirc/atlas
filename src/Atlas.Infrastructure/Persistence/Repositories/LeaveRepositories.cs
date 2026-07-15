using Atlas.Application.Abstractions;
using Atlas.Application.Pagination;
using Atlas.Domain.Countries;
using Atlas.Domain.Leave;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Persistence.Repositories;

// INTEGRATOR NOTE (S08): the ONLY change from S06 is the new ListPendingForOrgAsync on
// LeaveRequestRepository (the leave feed for the approval inbox). LeavePolicyRepository is
// verbatim S06. See meta/NOTES.md.
internal sealed class LeavePolicyRepository(AtlasDbContext db) : ILeavePolicyRepository
{
    public async Task AddAsync(LeavePolicy policy, CancellationToken ct) =>
        await db.Set<LeavePolicy>().AddAsync(policy, ct);

    public Task<LeavePolicy?> FindForAsync(Guid orgId, CountryCode country, CancellationToken ct) =>
        db.Set<LeavePolicy>()
            .SingleOrDefaultAsync(p => p.OrgId == orgId && p.Country == country, ct);

    public async Task<IReadOnlyList<LeavePolicy>> ListForOrgAsync(Guid orgId, CancellationToken ct) =>
        await db.Set<LeavePolicy>().AsNoTracking()
            .Where(p => p.OrgId == orgId)
            .OrderBy(p => p.Country)
            .ToListAsync(ct);
}

internal sealed class LeaveRequestRepository(AtlasDbContext db) : ILeaveRequestRepository
{
    public async Task AddAsync(LeaveRequest request, CancellationToken ct) =>
        await db.Set<LeaveRequest>().AddAsync(request, ct);

    public Task<LeaveRequest?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken ct) =>
        db.Set<LeaveRequest>().SingleOrDefaultAsync(r => r.Id == id && r.OrgId == orgId, ct);

    public async Task<IReadOnlyList<LeaveRequest>> ListApprovedForWorkerAsync(
        Guid workerId, CancellationToken ct) =>
        await db.Set<LeaveRequest>().AsNoTracking()
            .Where(r => r.WorkerId == workerId && r.Status == LeaveRequestStatus.Approved)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<LeaveRequest>> ListForOrgAsync(
        Guid orgId, TimelineCursor? after, int fetch, CancellationToken ct)
    {
        IQueryable<LeaveRequest> query = db.Set<LeaveRequest>().AsNoTracking()
            .Where(r => r.OrgId == orgId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ThenByDescending(r => r.Id);

        if (after is { } cursor)
        {
            query = query.Where(r =>
                r.CreatedAtUtc < cursor.CreatedAtUtc
                || (r.CreatedAtUtc == cursor.CreatedAtUtc
                    && r.Id.CompareTo(cursor.Id) < 0));
        }

        return await query.Take(fetch).ToListAsync(ct);
    }

    // S08: the leave feed for the cross-type approval inbox — submitted requests, newest first.
    public async Task<IReadOnlyList<LeaveRequest>> ListPendingForOrgAsync(
        Guid orgId, int fetch, CancellationToken ct) =>
        await db.Set<LeaveRequest>().AsNoTracking()
            .Where(r => r.OrgId == orgId && r.Status == LeaveRequestStatus.Submitted)
            .OrderByDescending(r => r.SubmittedAtUtc)
            .ThenByDescending(r => r.Id)
            .Take(fetch)
            .ToListAsync(ct);
}
