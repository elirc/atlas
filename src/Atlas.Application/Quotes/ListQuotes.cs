using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Pagination;
using Atlas.Domain.Quotes;

namespace Atlas.Application.Quotes;

/// <summary>
/// One org's quotes, newest first, cursor-paginated. Org-scoped: the caller only ever
/// sees their own tenant's quotes, because the org id comes from context, not the query
/// string (see docs/concepts/multi-tenancy.md).
/// </summary>
public sealed class ListQuotes(IQuoteRepository quotes, ICurrentContext context)
{
    public async Task<Page<Quote>> ExecuteAsync(
        string? cursor,
        int? limit,
        CancellationToken cancellationToken)
    {
        context.RequireUser();
        Guid orgId = context.RequireOrg();

        int pageSize = limit ?? Page<Quote>.DefaultLimit;

        // WHY: reject, don't clamp — same contract as every other list (ADR-0002).
        if (pageSize < 1 || pageSize > Page<Quote>.MaxLimit)
        {
            throw new RequestValidationException(
                $"limit must be between 1 and {Page<Quote>.MaxLimit}.");
        }

        TimelineCursor? after = cursor is null ? null : TimelineCursor.Decode(cursor);

        // Over-fetch by one to detect a next page; the extra row never leaves this method.
        IReadOnlyList<Quote> fetched =
            await quotes.ListForOrgAsync(orgId, after, pageSize + 1, cancellationToken);

        bool hasMore = fetched.Count > pageSize;
        IReadOnlyList<Quote> items = hasMore ? [.. fetched.Take(pageSize)] : fetched;

        string? next = hasMore
            ? new TimelineCursor(items[^1].CreatedAtUtc, items[^1].Id).Encode()
            : null;

        return new Page<Quote>(items, next);
    }
}
