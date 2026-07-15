using Atlas.Application.Abstractions;
using Atlas.Application.Pagination;
using Atlas.Domain.Approvals;
using Atlas.Domain.Expenses;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Persistence.Repositories;

internal sealed class ExpenseClaimRepository(AtlasDbContext db) : IExpenseClaimRepository
{
    public async Task AddAsync(ExpenseClaim claim, CancellationToken ct) =>
        await db.Set<ExpenseClaim>().AddAsync(claim, ct);

    public Task<ExpenseClaim?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken ct) =>
        db.Set<ExpenseClaim>().SingleOrDefaultAsync(c => c.Id == id && c.OrgId == orgId, ct);

    public async Task<IReadOnlyList<ExpenseClaim>> ListForOrgAsync(
        Guid orgId, TimelineCursor? after, int fetch, CancellationToken ct)
    {
        IQueryable<ExpenseClaim> query = db.Set<ExpenseClaim>().AsNoTracking()
            .Where(c => c.OrgId == orgId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ThenByDescending(c => c.Id);

        if (after is { } cursor)
        {
            query = query.Where(c =>
                c.CreatedAtUtc < cursor.CreatedAtUtc
                || (c.CreatedAtUtc == cursor.CreatedAtUtc
                    && c.Id.CompareTo(cursor.Id) < 0));
        }

        return await query.Take(fetch).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ExpenseClaim>> ListPendingForOrgAsync(
        Guid orgId, int fetch, CancellationToken ct) =>
        await db.Set<ExpenseClaim>().AsNoTracking()
            .Where(c => c.OrgId == orgId && c.Approval == ApprovalState.Submitted)
            .OrderByDescending(c => c.SubmittedAtUtc)
            .ThenByDescending(c => c.Id)
            .Take(fetch)
            .ToListAsync(ct);
}
