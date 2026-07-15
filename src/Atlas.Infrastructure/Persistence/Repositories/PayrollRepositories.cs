using Atlas.Application.Abstractions;
using Atlas.Application.Pagination;
using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Persistence.Repositories;

internal sealed class PayCycleRepository(AtlasDbContext db) : IPayCycleRepository
{
    public async Task AddAsync(PayCycle cycle, CancellationToken ct) =>
        await db.Set<PayCycle>().AddAsync(cycle, ct);

    public Task<PayCycle?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken ct) =>
        db.Set<PayCycle>().SingleOrDefaultAsync(c => c.Id == id && c.OrgId == orgId, ct);

    public Task<PayCycle?> FindForCountryAsync(
        Guid orgId, CountryCode country, PayFrequency frequency, CancellationToken ct) =>
        db.Set<PayCycle>().SingleOrDefaultAsync(
            c => c.OrgId == orgId && c.Country == country && c.Frequency == frequency, ct);

    public async Task<IReadOnlyList<PayCycle>> ListForOrgAsync(Guid orgId, CancellationToken ct) =>
        await db.Set<PayCycle>().AsNoTracking()
            .Where(c => c.OrgId == orgId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ThenByDescending(c => c.Id)
            .ToListAsync(ct);
}

internal sealed class PayrollRunRepository(AtlasDbContext db) : IPayrollRunRepository
{
    public async Task AddAsync(PayrollRun run, CancellationToken ct) =>
        await db.Set<PayrollRun>().AddAsync(run, ct);

    // WHY Include payslips + lines: the detail view and the domain totals both need the
    // full graph in memory (decimal can't be aggregated in SQL). This is the deliberate,
    // eager read; the LIST deliberately does NOT do it. If the graph ever hurts, an S14
    // read-model is the answer, not a partial load that leaves Total*() lying.
    public Task<PayrollRun?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken ct) =>
        db.Set<PayrollRun>()
            .Include(r => r.Payslips).ThenInclude(p => p.Lines)
            .SingleOrDefaultAsync(r => r.Id == id && r.OrgId == orgId, ct);

    public Task<PayrollRun?> FindByNaturalKeyAsync(
        Guid payCycleId, DateOnly periodStart, CancellationToken ct) =>
        db.Set<PayrollRun>()
            .Include(r => r.Payslips).ThenInclude(p => p.Lines)
            .SingleOrDefaultAsync(r => r.PayCycleId == payCycleId && r.PeriodStart == periodStart, ct);

    public async Task<IReadOnlyList<PayrollRun>> ListForOrgAsync(
        Guid orgId, TimelineCursor? after, int fetch, CancellationToken ct)
    {
        // No Include: the list is a lightweight index (no totals, no payslips).
        IQueryable<PayrollRun> query = db.Set<PayrollRun>().AsNoTracking()
            .Where(r => r.OrgId == orgId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ThenByDescending(r => r.Id);

        if (after is { } cursor)
        {
            query = query.Where(r =>
                r.CreatedAtUtc < cursor.CreatedAtUtc
                || (r.CreatedAtUtc == cursor.CreatedAtUtc
                    && r.Id.CompareTo(cursor.Id) < 0));
        }

        return await query.Take(fetch).ToListAsync(ct);
    }
}

internal sealed class PayslipRepository(AtlasDbContext db) : IPayslipRepository
{
    public Task<Payslip?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken ct) =>
        db.Set<Payslip>()
            .Include(p => p.Lines)
            .SingleOrDefaultAsync(p => p.Id == id && p.OrgId == orgId, ct);
}
