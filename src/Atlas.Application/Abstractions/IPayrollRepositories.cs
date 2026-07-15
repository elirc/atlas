using Atlas.Application.Pagination;
using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;

namespace Atlas.Application.Abstractions;

public interface IPayCycleRepository
{
    Task AddAsync(PayCycle cycle, CancellationToken cancellationToken);

    /// <summary>Load one cycle within one org — the tenant-safe by-id read (cross-org → null → 404).</summary>
    Task<PayCycle?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken cancellationToken);

    /// <summary>The org's existing cycle for a country+frequency, if any — the duplicate guard.</summary>
    Task<PayCycle?> FindForCountryAsync(
        Guid orgId,
        CountryCode country,
        PayFrequency frequency,
        CancellationToken cancellationToken);

    /// <summary>All of one org's cycles, newest first. Cycles are few (one per country) — no cursor.</summary>
    Task<IReadOnlyList<PayCycle>> ListForOrgAsync(Guid orgId, CancellationToken cancellationToken);
}

public interface IPayrollRunRepository
{
    Task AddAsync(PayrollRun run, CancellationToken cancellationToken);

    /// <summary>Load one run within one org, WITH its payslips (for the detail view / totals).</summary>
    Task<PayrollRun?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken cancellationToken);

    /// <summary>
    /// Load the run for a (cycle, periodStart) natural key, WITH payslips — the idempotency
    /// probe. Returns null when this period has never been prepared.
    /// </summary>
    Task<PayrollRun?> FindByNaturalKeyAsync(
        Guid payCycleId,
        DateOnly periodStart,
        CancellationToken cancellationToken);

    /// <summary>
    /// Newest-first org-scoped page WITHOUT payslips — the list is a lightweight index;
    /// callers over-fetch by one (see ListClientOrgs).
    /// </summary>
    Task<IReadOnlyList<PayrollRun>> ListForOrgAsync(
        Guid orgId,
        TimelineCursor? after,
        int fetch,
        CancellationToken cancellationToken);
}

public interface IPayslipRepository
{
    /// <summary>Load one payslip within one org, WITH its lines (cross-org → null → 404).</summary>
    Task<Payslip?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken cancellationToken);
}
