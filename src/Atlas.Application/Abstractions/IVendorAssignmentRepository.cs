using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;

namespace Atlas.Application.Abstractions;

public interface IVendorAssignmentRepository
{
    Task AddAsync(VendorAssignment assignment, CancellationToken cancellationToken);

    /// <summary>
    /// The full effective-dated switch history for one org+country, so the caller can RESOLVE
    /// the governing (or pending) vendor in memory (VendorSelection). History is tiny — a
    /// handful of switches over a company's life — so it is loaded whole, never paginated, the
    /// same call the pure resolver expects (mirrors IEmploymentRecordRepository.ListForWorker).
    /// </summary>
    Task<IReadOnlyList<VendorAssignment>> ListForOrgCountryAsync(
        Guid orgId,
        CountryCode country,
        CancellationToken cancellationToken);

    /// <summary>The org's existing assignment on an exact effective day, if any — the append duplicate guard.</summary>
    Task<VendorAssignment?> FindByEffectiveDayAsync(
        Guid orgId,
        CountryCode country,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken);
}
