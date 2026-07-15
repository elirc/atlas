using Atlas.Application.Abstractions;
using Atlas.Application.Pagination;
using Atlas.Domain.Orgs;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Persistence.Repositories;

internal sealed class ClientOrgRepository(AtlasDbContext db) : IClientOrgRepository
{
    public async Task AddAsync(ClientOrg org, CancellationToken cancellationToken) =>
        await db.ClientOrgs.AddAsync(org, cancellationToken);

    public async Task<ClientOrg?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.ClientOrgs.FindAsync([id], cancellationToken);

    public async Task<IReadOnlyList<ClientOrg>> ListAfterAsync(
        TimelineCursor? after,
        int fetch,
        CancellationToken cancellationToken)
    {
        IQueryable<ClientOrg> query = db.ClientOrgs.AsNoTracking()
            .OrderByDescending(o => o.CreatedAtUtc)
            .ThenByDescending(o => o.Id);

        if (after is { } cursor)
        {
            // WHY: strict keyset predicate — everything strictly after the cursor in
            // (CreatedAtUtc DESC, Id DESC) order. The tiebreak arm is what makes two
            // orgs created in the same tick impossible to skip or duplicate.
            query = query.Where(o =>
                o.CreatedAtUtc < cursor.CreatedAtUtc
                || (o.CreatedAtUtc == cursor.CreatedAtUtc
                    && o.Id.CompareTo(cursor.Id) < 0));
        }

        return await query.Take(fetch).ToListAsync(cancellationToken);
    }
}
