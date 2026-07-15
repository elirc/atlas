using Atlas.Application.Abstractions;
using Atlas.Application.Pagination;
using Atlas.Domain.Orgs;

namespace Atlas.Application.Tests.Fakes;

// LEARN: a fake is a real implementation with a toy storage engine — it honors the
// port's CONTRACT (ordering, exclusive cursor bound) so use-case tests exercise real
// logic, not mock choreography. When the contract changes, the fake fails loudly.
public sealed class InMemoryClientOrgRepository : IClientOrgRepository
{
    private readonly List<ClientOrg> _rows = [];

    public IReadOnlyList<ClientOrg> Rows => _rows;

    public Task AddAsync(ClientOrg org, CancellationToken cancellationToken)
    {
        _rows.Add(org);
        return Task.CompletedTask;
    }

    public Task<ClientOrg?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.FirstOrDefault(o => o.Id == id));

    public Task<IReadOnlyList<ClientOrg>> ListAfterAsync(
        TimelineCursor? after,
        int fetch,
        CancellationToken cancellationToken)
    {
        IEnumerable<ClientOrg> ordered = _rows
            .OrderByDescending(o => o.CreatedAtUtc)
            .ThenByDescending(o => o.Id);

        if (after is { } cursor)
        {
            ordered = ordered.Where(o =>
                o.CreatedAtUtc < cursor.CreatedAtUtc
                || (o.CreatedAtUtc == cursor.CreatedAtUtc
                    && o.Id.CompareTo(cursor.Id) < 0));
        }

        IReadOnlyList<ClientOrg> page = [.. ordered.Take(fetch)];
        return Task.FromResult(page);
    }
}
