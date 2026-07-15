using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Pagination;
using Atlas.Domain.Workforce;

namespace Atlas.Application.Workforce;

public sealed class ListWorkers(IWorkerRepository workers, ICurrentContext context)
{
    public async Task<Page<Worker>> ExecuteAsync(
        string? cursor,
        int? limit,
        CancellationToken cancellationToken)
    {
        // WHY: the org filter comes from the CALLER'S context, never from a query
        // parameter — a tenant cannot ask for someone else's list by construction.
        Guid orgId = context.RequireOrg();

        int pageSize = limit ?? Page<Worker>.DefaultLimit;
        if (pageSize < 1 || pageSize > Page<Worker>.MaxLimit)
        {
            throw new RequestValidationException(
                $"limit must be between 1 and {Page<Worker>.MaxLimit}.");
        }

        TimelineCursor? after = cursor is null ? null : TimelineCursor.Decode(cursor);

        IReadOnlyList<Worker> fetched =
            await workers.ListForOrgAsync(orgId, after, pageSize + 1, cancellationToken);

        bool hasMore = fetched.Count > pageSize;
        IReadOnlyList<Worker> items = hasMore ? [.. fetched.Take(pageSize)] : fetched;

        string? next = hasMore
            ? new TimelineCursor(items[^1].CreatedAtUtc, items[^1].Id).Encode()
            : null;

        return new Page<Worker>(items, next);
    }
}
