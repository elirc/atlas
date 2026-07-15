using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Access;
using Atlas.Domain.Payroll;

namespace Atlas.Application.Payroll;

/// <summary>Load the latest shadow-comparison report for a run within the caller's org, or 404.</summary>
// LEARN: a tenant-safe by-run read (the report carries its own OrgId, denormalised from the run),
// returning the newest report — a fresh shadow supersedes an older one. Cross-tenant reads are
// 404, never 403 (ADR-0003). The report is deltas-only by construction, so there is nothing to
// authorize field-by-field: you either may see this org's divergences or you may not.
public sealed class GetVendorComparison(
    IVendorComparisonReportRepository reports,
    ICurrentContext context)
{
    public async Task<VendorComparisonReport> ExecuteAsync(Guid runId, CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin, Role.HrManager);

        VendorComparisonReport? report =
            await reports.FindLatestForRunAsync(runId, context.RequireOrg(), cancellationToken);

        return report ?? throw new NotFoundException("vendor comparison for run", runId);
    }
}
