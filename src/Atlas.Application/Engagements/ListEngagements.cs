using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Pagination;
using Atlas.Domain.Access;
using Atlas.Domain.Engagements;

namespace Atlas.Application.Engagements;

/// <summary>Newest-first, org-scoped page of engagements — the hiring pipeline view.</summary>
public sealed class ListEngagements(IEngagementRepository engagements, ICurrentContext context)
{
    public async Task<Page<Engagement>> ExecuteAsync(
        string? cursor,
        int? limit,
        CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin, Role.HrManager);

        // WHY: the org filter is the caller's, taken from context — never a query
        // parameter — so one tenant cannot page through another's pipeline.
        Guid orgId = context.RequireOrg();

        int pageSize = limit ?? Page<Engagement>.DefaultLimit;
        if (pageSize < 1 || pageSize > Page<Engagement>.MaxLimit)
        {
            throw new RequestValidationException(
                $"limit must be between 1 and {Page<Engagement>.MaxLimit}.");
        }

        TimelineCursor? after = cursor is null ? null : TimelineCursor.Decode(cursor);

        IReadOnlyList<Engagement> fetched =
            await engagements.ListForOrgAsync(orgId, after, pageSize + 1, cancellationToken);

        bool hasMore = fetched.Count > pageSize;
        IReadOnlyList<Engagement> items = hasMore ? [.. fetched.Take(pageSize)] : fetched;

        string? next = hasMore
            ? new TimelineCursor(items[^1].CreatedAtUtc, items[^1].Id).Encode()
            : null;

        return new Page<Engagement>(items, next);
    }
}
