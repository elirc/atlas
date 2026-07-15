using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Pagination;
using Atlas.Domain.Payroll;

namespace Atlas.Application.Payroll;

/// <summary>
/// One org's payroll runs, newest first, cursor-paginated. The list is a lightweight
/// index — runs come back WITHOUT payslips (totals live on the detail view), so the list
/// stays cheap no matter how many workers each run pays.
/// </summary>
public sealed class ListPayrollRuns(IPayrollRunRepository runs, ICurrentContext context)
{
    public async Task<Page<PayrollRun>> ExecuteAsync(
        string? cursor,
        int? limit,
        CancellationToken cancellationToken)
    {
        context.RequireUser();
        Guid orgId = context.RequireOrg();

        int pageSize = limit ?? Page<PayrollRun>.DefaultLimit;

        // WHY: reject, don't clamp — same contract as every other list (ADR-0002).
        if (pageSize < 1 || pageSize > Page<PayrollRun>.MaxLimit)
        {
            throw new RequestValidationException(
                $"limit must be between 1 and {Page<PayrollRun>.MaxLimit}.");
        }

        TimelineCursor? after = cursor is null ? null : TimelineCursor.Decode(cursor);

        IReadOnlyList<PayrollRun> fetched =
            await runs.ListForOrgAsync(orgId, after, pageSize + 1, cancellationToken);

        bool hasMore = fetched.Count > pageSize;
        IReadOnlyList<PayrollRun> items = hasMore ? [.. fetched.Take(pageSize)] : fetched;

        string? next = hasMore
            ? new TimelineCursor(items[^1].CreatedAtUtc, items[^1].Id).Encode()
            : null;

        return new Page<PayrollRun>(items, next);
    }
}
