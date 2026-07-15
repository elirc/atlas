using Atlas.Application.Abstractions;
using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Persistence.Repositories;

internal sealed class VendorAssignmentRepository(AtlasDbContext db) : IVendorAssignmentRepository
{
    public async Task AddAsync(VendorAssignment assignment, CancellationToken ct) =>
        await db.Set<VendorAssignment>().AddAsync(assignment, ct);

    // WHY the whole history, unpaginated: vendor switches are counted on one hand over a company's
    // life, and the pure resolver (VendorSelection) needs the full set to fold "current" or "pending"
    // in memory. AsNoTracking — resolution never mutates. Ordering is left to the caller/resolver,
    // which is total over any order.
    public async Task<IReadOnlyList<VendorAssignment>> ListForOrgCountryAsync(
        Guid orgId, CountryCode country, CancellationToken ct) =>
        await db.Set<VendorAssignment>().AsNoTracking()
            .Where(a => a.OrgId == orgId && a.Country == country)
            .ToListAsync(ct);

    public Task<VendorAssignment?> FindByEffectiveDayAsync(
        Guid orgId, CountryCode country, DateOnly effectiveFrom, CancellationToken ct) =>
        db.Set<VendorAssignment>().SingleOrDefaultAsync(
            a => a.OrgId == orgId && a.Country == country && a.EffectiveFrom == effectiveFrom, ct);
}

internal sealed class VendorComparisonReportRepository(AtlasDbContext db) : IVendorComparisonReportRepository
{
    public async Task AddAsync(VendorComparisonReport report, CancellationToken ct) =>
        await db.Set<VendorComparisonReport>().AddAsync(report, ct);

    // Newest report wins (documents are superseded, never edited). Include the delta lines so the
    // detail read has the full graph — reports are small (one line per worker), so the eager load
    // is cheap and matches the payslip-detail pattern.
    public Task<VendorComparisonReport?> FindLatestForRunAsync(
        Guid runId, Guid orgId, CancellationToken ct) =>
        db.Set<VendorComparisonReport>()
            .Include(r => r.Lines)
            .Where(r => r.RunId == runId && r.OrgId == orgId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(ct);
}
