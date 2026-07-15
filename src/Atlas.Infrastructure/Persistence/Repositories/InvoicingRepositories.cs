using Atlas.Application.Abstractions;
using Atlas.Application.Pagination;
using Atlas.Domain.Invoicing;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Persistence.Repositories;

internal sealed class ContractorRepository(AtlasDbContext db) : IContractorRepository
{
    public async Task AddAsync(Contractor contractor, CancellationToken ct) =>
        await db.Set<Contractor>().AddAsync(contractor, ct);

    public Task<Contractor?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken ct) =>
        db.Set<Contractor>().SingleOrDefaultAsync(c => c.Id == id && c.OrgId == orgId, ct);

    public async Task<IReadOnlyList<Contractor>> ListForOrgAsync(Guid orgId, CancellationToken ct) =>
        await db.Set<Contractor>().AsNoTracking()
            .Where(c => c.OrgId == orgId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ThenByDescending(c => c.Id)
            .ToListAsync(ct);
}

internal sealed class ContractorInvoiceRepository(AtlasDbContext db) : IContractorInvoiceRepository
{
    public async Task AddAsync(ContractorInvoice invoice, CancellationToken ct) =>
        await db.Set<ContractorInvoice>().AddAsync(invoice, ct);

    // WHY Include the lines: the detail view and the domain Total() both need the full graph in
    // memory (decimal cannot be aggregated in SQL). This is the deliberate eager read; the LIST
    // below deliberately does NOT do it.
    public Task<ContractorInvoice?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken ct) =>
        db.Set<ContractorInvoice>()
            .Include(i => i.Lines)
            .SingleOrDefaultAsync(i => i.Id == id && i.OrgId == orgId, ct);

    public async Task<IReadOnlyList<ContractorInvoice>> ListForOrgAsync(
        Guid orgId, InvoiceStatus? status, TimelineCursor? after, int fetch, CancellationToken ct)
    {
        // No Include: the list is a lightweight index (no lines, no totals).
        IQueryable<ContractorInvoice> query = db.Set<ContractorInvoice>().AsNoTracking()
            .Where(i => i.OrgId == orgId);

        if (status is { } wanted)
        {
            query = query.Where(i => i.Status == wanted);
        }

        query = query
            .OrderByDescending(i => i.CreatedAtUtc)
            .ThenByDescending(i => i.Id);

        if (after is { } cursor)
        {
            query = query.Where(i =>
                i.CreatedAtUtc < cursor.CreatedAtUtc
                || (i.CreatedAtUtc == cursor.CreatedAtUtc
                    && i.Id.CompareTo(cursor.Id) < 0));
        }

        return await query.Take(fetch).ToListAsync(ct);
    }
}
