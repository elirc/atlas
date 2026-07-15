using Atlas.Domain.Payroll;

namespace Atlas.Application.Abstractions;

public interface IVendorComparisonReportRepository
{
    Task AddAsync(VendorComparisonReport report, CancellationToken cancellationToken);

    /// <summary>
    /// The most recent comparison report for one run within one org, WITH its per-worker delta
    /// lines — or null when none has been generated. Newest wins: a fresh shadow run supersedes
    /// an older one (reports are documents, never edited), so the read returns the latest.
    /// </summary>
    Task<VendorComparisonReport?> FindLatestForRunAsync(
        Guid runId,
        Guid orgId,
        CancellationToken cancellationToken);
}
