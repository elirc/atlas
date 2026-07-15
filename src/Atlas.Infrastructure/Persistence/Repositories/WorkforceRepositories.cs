using Atlas.Application.Abstractions;
using Atlas.Application.Pagination;
using Atlas.Domain.Auditing;
using Atlas.Domain.Countries;
using Atlas.Domain.Workforce;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Persistence.Repositories;

internal sealed class WorkerRepository(AtlasDbContext db) : IWorkerRepository
{
    public async Task AddAsync(Worker worker, CancellationToken ct) =>
        await db.Set<Worker>().AddAsync(worker, ct);

    public async Task<Worker?> FindAsync(Guid id, CancellationToken ct) =>
        await db.Set<Worker>().FindAsync([id], ct);

    public Task<Worker?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken ct) =>
        db.Set<Worker>().SingleOrDefaultAsync(w => w.Id == id && w.OrgId == orgId, ct);

    public async Task<IReadOnlyList<Worker>> ListForOrgAsync(
        Guid orgId, TimelineCursor? after, int fetch, CancellationToken ct)
    {
        IQueryable<Worker> query = db.Set<Worker>().AsNoTracking()
            .Where(w => w.OrgId == orgId)
            .OrderByDescending(w => w.CreatedAtUtc)
            .ThenByDescending(w => w.Id);

        if (after is { } cursor)
        {
            query = query.Where(w =>
                w.CreatedAtUtc < cursor.CreatedAtUtc
                || (w.CreatedAtUtc == cursor.CreatedAtUtc
                    && w.Id.CompareTo(cursor.Id) < 0));
        }

        return await query.Take(fetch).ToListAsync(ct);
    }

    // S07 addition: the payroll population — active workers of one org in one country as of
    // a date. Whole set, not a page (a run processes everyone in one pass). "Active" = hired
    // on or before asOf; ordered oldest-first so the run's payslips read in hire order.
    public async Task<IReadOnlyList<Worker>> ListActiveInCountryAsync(
        Guid orgId, CountryCode country, DateOnly asOf, CancellationToken ct) =>
        await db.Set<Worker>().AsNoTracking()
            .Where(w => w.OrgId == orgId && w.Country == country && w.HiredOn <= asOf)
            .OrderBy(w => w.CreatedAtUtc)
            .ThenBy(w => w.Id)
            .ToListAsync(ct);
}

internal sealed class EmploymentRecordRepository(AtlasDbContext db) : IEmploymentRecordRepository
{
    public async Task AddAsync(EmploymentRecord record, CancellationToken ct) =>
        await db.Set<EmploymentRecord>().AddAsync(record, ct);

    public async Task<IReadOnlyList<EmploymentRecord>> ListForWorkerAsync(
        Guid workerId, CancellationToken ct) =>
        await db.Set<EmploymentRecord>().AsNoTracking()
            .Where(r => r.WorkerId == workerId)
            .ToListAsync(ct);
}

internal sealed class EfAuditTrail(AtlasDbContext db) : IAuditTrail
{
    // LEARN: Append stages the entry into the SAME change tracker the use case's
    // other writes live in — IUnitOfWork.CommitAsync makes them durable together.
    // The audit row cannot outlive or predate the change it describes.
    public void Append(AuditEntry entry) => db.Set<AuditEntry>().Add(entry);

    public async Task<IReadOnlyList<AuditEntry>> ListForOrgAsync(
        Guid orgId, TimelineCursor? after, int fetch, CancellationToken ct)
    {
        IQueryable<AuditEntry> query = db.Set<AuditEntry>().AsNoTracking()
            .Where(e => e.OrgId == orgId)
            .OrderByDescending(e => e.AtUtc)
            .ThenByDescending(e => e.Id);

        if (after is { } cursor)
        {
            query = query.Where(e =>
                e.AtUtc < cursor.CreatedAtUtc
                || (e.AtUtc == cursor.CreatedAtUtc
                    && e.Id.CompareTo(cursor.Id) < 0));
        }

        return await query.Take(fetch).ToListAsync(ct);
    }
}
