using Atlas.Application.Abstractions;
using Atlas.Application.Pagination;
using Atlas.Domain.Quotes;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Persistence.Repositories;

internal sealed class QuoteRepository(AtlasDbContext db) : IQuoteRepository
{
    public async Task AddAsync(Quote quote, CancellationToken cancellationToken) =>
        await db.Set<Quote>().AddAsync(quote, cancellationToken);

    // WHY: no explicit Include for Gross/TotalEmployerCost/DisplayTotal/Lines — owned
    // types are part of the aggregate and EF loads them automatically. The org filter is
    // in the predicate, so a cross-tenant id simply finds nothing.
    public Task<Quote?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken cancellationToken) =>
        db.Set<Quote>().SingleOrDefaultAsync(q => q.Id == id && q.OrgId == orgId, cancellationToken);

    public async Task<IReadOnlyList<Quote>> ListForOrgAsync(
        Guid orgId,
        TimelineCursor? after,
        int fetch,
        CancellationToken cancellationToken)
    {
        IQueryable<Quote> query = db.Set<Quote>().AsNoTracking()
            .Where(q => q.OrgId == orgId)
            .OrderByDescending(q => q.CreatedAtUtc)
            .ThenByDescending(q => q.Id);

        if (after is { } cursor)
        {
            // Strict keyset predicate in (CreatedAtUtc DESC, Id DESC) — the Id tiebreak
            // makes two quotes created in the same tick impossible to skip or duplicate.
            query = query.Where(q =>
                q.CreatedAtUtc < cursor.CreatedAtUtc
                || (q.CreatedAtUtc == cursor.CreatedAtUtc
                    && q.Id.CompareTo(cursor.Id) < 0));
        }

        // GOTCHA: we order and page by (CreatedAtUtc, Id) — never by a Money/decimal
        // column. Decimal ORDER BY/SUM on SQLite is unsupported; monetary math stays in
        // the Domain (see AtlasDbContext's SQLite notes and EmploymentRecord's warning).
        return await query.Take(fetch).ToListAsync(cancellationToken);
    }
}
