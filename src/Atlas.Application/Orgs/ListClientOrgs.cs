using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Pagination;
using Atlas.Domain.Orgs;

namespace Atlas.Application.Orgs;

public sealed class ListClientOrgs(IClientOrgRepository orgs, ICurrentContext context)
{
    public async Task<Page<ClientOrg>> ExecuteAsync(
        string? cursor,
        int? limit,
        CancellationToken cancellationToken)
    {
        // WHY: "every org on the platform" is a platform-staff view. Org users see
        // exactly one org — theirs — via GET /client-orgs/{id}.
        context.RequireAtlasOps();

        int pageSize = limit ?? Page<ClientOrg>.DefaultLimit;

        // WHY: reject, don't clamp. A silently-clamped limit=5000 teaches the client
        // that 5000 "works" — until the day the data grows and their pagination loop
        // was never exercised. Explicit contracts fail fast on both sides.
        if (pageSize < 1 || pageSize > Page<ClientOrg>.MaxLimit)
        {
            throw new RequestValidationException(
                $"limit must be between 1 and {Page<ClientOrg>.MaxLimit}.");
        }

        TimelineCursor? after = cursor is null ? null : TimelineCursor.Decode(cursor);

        // LEARN: fetch limit+1 — the classic "is there a next page?" probe. The extra
        // row is evidence, not payload; it never leaves this method.
        IReadOnlyList<ClientOrg> fetched =
            await orgs.ListAfterAsync(after, pageSize + 1, cancellationToken);

        bool hasMore = fetched.Count > pageSize;
        IReadOnlyList<ClientOrg> items = hasMore ? [.. fetched.Take(pageSize)] : fetched;

        string? next = hasMore
            ? new TimelineCursor(items[^1].CreatedAtUtc, items[^1].Id).Encode()
            : null;

        return new Page<ClientOrg>(items, next);
    }
}
