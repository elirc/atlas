using Atlas.Application.Abstractions;
using Atlas.Application.Pagination;
using Atlas.Domain.Engagements;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Persistence.Repositories;

internal sealed class EngagementRepository(AtlasDbContext db) : IEngagementRepository
{
    public async Task AddAsync(Engagement engagement, CancellationToken ct) =>
        await db.Set<Engagement>().AddAsync(engagement, ct);

    // WHY: TRACKED (no AsNoTracking) — a transition use case loads the engagement
    // here, mutates it, and relies on the change tracker so CommitAsync persists the
    // new state. The owned checklist rides along automatically (OwnsMany), so the
    // aggregate is whole when Activate checks "every item complete".
    public Task<Engagement?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken ct) =>
        db.Set<Engagement>().SingleOrDefaultAsync(e => e.Id == id && e.OrgId == orgId, ct);

    public async Task<IReadOnlyList<Engagement>> ListForOrgAsync(
        Guid orgId, TimelineCursor? after, int fetch, CancellationToken ct)
    {
        IQueryable<Engagement> query = db.Set<Engagement>().AsNoTracking()
            .Where(e => e.OrgId == orgId)
            .OrderByDescending(e => e.CreatedAtUtc)
            .ThenByDescending(e => e.Id);

        if (after is { } cursor)
        {
            query = query.Where(e =>
                e.CreatedAtUtc < cursor.CreatedAtUtc
                || (e.CreatedAtUtc == cursor.CreatedAtUtc
                    && e.Id.CompareTo(cursor.Id) < 0));
        }

        return await query.Take(fetch).ToListAsync(ct);
    }
}
