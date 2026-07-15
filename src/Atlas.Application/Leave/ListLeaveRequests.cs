using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Pagination;
using Atlas.Domain.Access;
using Atlas.Domain.Leave;

namespace Atlas.Application.Leave;

public sealed class ListLeaveRequests(ILeaveRequestRepository requests, ICurrentContext context)
{
    public async Task<Page<LeaveRequest>> ExecuteAsync(
        string? cursor,
        int? limit,
        CancellationToken cancellationToken)
    {
        // WHY: leave requests name workers and their absences — HR-manager-and-up reading,
        // same tenancy rules as the audit log. The org filter comes from the CALLER'S
        // context, never a query parameter.
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        int pageSize = limit ?? Page<LeaveRequest>.DefaultLimit;
        if (pageSize < 1 || pageSize > Page<LeaveRequest>.MaxLimit)
        {
            throw new RequestValidationException(
                $"limit must be between 1 and {Page<LeaveRequest>.MaxLimit}.");
        }

        TimelineCursor? after = cursor is null ? null : TimelineCursor.Decode(cursor);

        IReadOnlyList<LeaveRequest> fetched =
            await requests.ListForOrgAsync(orgId, after, pageSize + 1, cancellationToken);

        bool hasMore = fetched.Count > pageSize;
        IReadOnlyList<LeaveRequest> items = hasMore ? [.. fetched.Take(pageSize)] : fetched;

        string? next = hasMore
            ? new TimelineCursor(items[^1].CreatedAtUtc, items[^1].Id).Encode()
            : null;

        return new Page<LeaveRequest>(items, next);
    }
}
