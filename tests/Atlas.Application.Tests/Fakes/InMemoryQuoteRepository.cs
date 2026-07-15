using Atlas.Application.Abstractions;
using Atlas.Application.Pagination;
using Atlas.Domain.Quotes;

namespace Atlas.Application.Tests.Fakes;

// Honors the port contract: org-scoped by-id read, and the exclusive (CreatedAtUtc DESC,
// Id DESC) keyset the real repository walks — so use-case tests exercise real paging logic.
public sealed class InMemoryQuoteRepository : IQuoteRepository
{
    private readonly List<Quote> _rows = [];

    public IReadOnlyList<Quote> Rows => _rows;

    public Task AddAsync(Quote quote, CancellationToken cancellationToken)
    {
        _rows.Add(quote);
        return Task.CompletedTask;
    }

    public Task<Quote?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.FirstOrDefault(q => q.Id == id && q.OrgId == orgId));

    public Task<IReadOnlyList<Quote>> ListForOrgAsync(
        Guid orgId,
        TimelineCursor? after,
        int fetch,
        CancellationToken cancellationToken)
    {
        IEnumerable<Quote> ordered = _rows
            .Where(q => q.OrgId == orgId)
            .OrderByDescending(q => q.CreatedAtUtc)
            .ThenByDescending(q => q.Id);

        if (after is { } cursor)
        {
            ordered = ordered.Where(q =>
                q.CreatedAtUtc < cursor.CreatedAtUtc
                || (q.CreatedAtUtc == cursor.CreatedAtUtc
                    && q.Id.CompareTo(cursor.Id) < 0));
        }

        IReadOnlyList<Quote> page = [.. ordered.Take(fetch)];
        return Task.FromResult(page);
    }
}
