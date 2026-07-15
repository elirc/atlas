using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Pagination;
using Atlas.Domain.Access;
using Atlas.Domain.Expenses;

namespace Atlas.Application.Expenses;

public sealed class ListExpenses(IExpenseClaimRepository claims, ICurrentContext context)
{
    public async Task<Page<ExpenseClaim>> ExecuteAsync(
        string? cursor,
        int? limit,
        CancellationToken cancellationToken)
    {
        // WHY: the org filter comes from the CALLER'S context, never a query parameter — the same
        // tenancy stance as every other list. Copy of the ListClientOrgs / ListLeaveRequests shape.
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        int pageSize = limit ?? Page<ExpenseClaim>.DefaultLimit;
        if (pageSize < 1 || pageSize > Page<ExpenseClaim>.MaxLimit)
        {
            throw new RequestValidationException(
                $"limit must be between 1 and {Page<ExpenseClaim>.MaxLimit}.");
        }

        TimelineCursor? after = cursor is null ? null : TimelineCursor.Decode(cursor);

        IReadOnlyList<ExpenseClaim> fetched =
            await claims.ListForOrgAsync(orgId, after, pageSize + 1, cancellationToken);

        bool hasMore = fetched.Count > pageSize;
        IReadOnlyList<ExpenseClaim> items = hasMore ? [.. fetched.Take(pageSize)] : fetched;

        string? next = hasMore
            ? new TimelineCursor(items[^1].CreatedAtUtc, items[^1].Id).Encode()
            : null;

        return new Page<ExpenseClaim>(items, next);
    }
}
