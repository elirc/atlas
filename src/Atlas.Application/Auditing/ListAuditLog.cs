using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Pagination;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;

namespace Atlas.Application.Auditing;

public sealed class ListAuditLog(IAuditTrail audit, ICurrentContext context)
{
    public async Task<Page<AuditEntry>> ExecuteAsync(
        string? cursor,
        int? limit,
        CancellationToken cancellationToken)
    {
        // WHY: the audit log names people and salaries — HR-manager-and-up reading,
        // same tenancy rules as everything else.
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        int pageSize = limit ?? Page<AuditEntry>.DefaultLimit;
        if (pageSize < 1 || pageSize > Page<AuditEntry>.MaxLimit)
        {
            throw new RequestValidationException(
                $"limit must be between 1 and {Page<AuditEntry>.MaxLimit}.");
        }

        TimelineCursor? after = cursor is null ? null : TimelineCursor.Decode(cursor);

        IReadOnlyList<AuditEntry> fetched =
            await audit.ListForOrgAsync(orgId, after, pageSize + 1, cancellationToken);

        bool hasMore = fetched.Count > pageSize;
        IReadOnlyList<AuditEntry> items = hasMore ? [.. fetched.Take(pageSize)] : fetched;

        string? next = hasMore
            ? new TimelineCursor(items[^1].AtUtc, items[^1].Id).Encode()
            : null;

        return new Page<AuditEntry>(items, next);
    }
}
