using Atlas.Application.Abstractions;
using Atlas.Application.Events;
using Atlas.Application.Pagination;
using Atlas.Domain.Engagements;

namespace Atlas.Application.Tests.Fakes;

public sealed class InMemoryEngagementRepository : IEngagementRepository
{
    public List<Engagement> Rows { get; } = [];

    public Task AddAsync(Engagement engagement, CancellationToken ct)
    {
        Rows.Add(engagement);
        return Task.CompletedTask;
    }

    public Task<Engagement?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken ct) =>
        Task.FromResult(Rows.FirstOrDefault(e => e.Id == id && e.OrgId == orgId));

    public Task<IReadOnlyList<Engagement>> ListForOrgAsync(
        Guid orgId, TimelineCursor? after, int fetch, CancellationToken ct)
    {
        IEnumerable<Engagement> ordered = Rows
            .Where(e => e.OrgId == orgId)
            .OrderByDescending(e => e.CreatedAtUtc)
            .ThenByDescending(e => e.Id);

        if (after is { } cursor)
        {
            ordered = ordered.Where(e =>
                e.CreatedAtUtc < cursor.CreatedAtUtc
                || (e.CreatedAtUtc == cursor.CreatedAtUtc
                    && e.Id.CompareTo(cursor.Id) < 0));
        }

        IReadOnlyList<Engagement> page = [.. ordered.Take(fetch)];
        return Task.FromResult(page);
    }
}

/// <summary>Records every event a use case publishes — the test's window on the seam.</summary>
public sealed class RecordingEventSink : IEventSink
{
    public List<AtlasEvent> Published { get; } = [];

    public Task PublishAsync(AtlasEvent evt, CancellationToken cancellationToken)
    {
        Published.Add(evt);
        return Task.CompletedTask;
    }
}

/// <summary>A fake quote reader seeded with snapshots — no S4 types required to test.</summary>
public sealed class InMemoryQuoteReader : IQuoteReader
{
    private readonly Dictionary<(Guid Quote, Guid Org), QuoteSnapshot> _rows = [];

    public void Add(Guid quoteId, Guid orgId, QuoteSnapshot snapshot) =>
        _rows[(quoteId, orgId)] = snapshot;

    public Task<QuoteSnapshot?> FindInOrgAsync(Guid quoteId, Guid orgId, CancellationToken ct) =>
        Task.FromResult(_rows.TryGetValue((quoteId, orgId), out QuoteSnapshot? snapshot)
            ? snapshot
            : null);
}
