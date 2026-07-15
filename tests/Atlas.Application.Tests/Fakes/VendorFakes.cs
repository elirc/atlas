using Atlas.Application.Abstractions;
using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;

namespace Atlas.Application.Tests.Fakes;

/// <summary>The native vendor as a test double — wraps the pure PayrollCalculator, no Infrastructure
/// needed. Reproduces exactly what AtlasNativeCalculator does, in a Domain-only fake.</summary>
public sealed class NativeCalculationVendor : IPayrollCalculationVendor
{
    public PayrollVendor Vendor => PayrollVendor.AtlasNative;

    public Task<PayslipCalculation> CalculateAsync(PayrollCalculationInput input, CancellationToken ct) =>
        Task.FromResult(PayrollCalculator.Calculate(input));
}

/// <summary>A calculation vendor whose output is supplied by a delegate — for divergence tests that
/// need a second engine that disagrees with the native one on purpose.</summary>
public sealed class StubCalculationVendor(
    PayrollVendor vendor, Func<PayrollCalculationInput, PayslipCalculation> compute) : IPayrollCalculationVendor
{
    public PayrollVendor Vendor => vendor;

    public Task<PayslipCalculation> CalculateAsync(PayrollCalculationInput input, CancellationToken ct) =>
        Task.FromResult(compute(input));
}

public sealed class FakePayrollVendorDirectory : IPayrollVendorDirectory
{
    private readonly Dictionary<PayrollVendor, IPayrollCalculationVendor> _map;

    public FakePayrollVendorDirectory(params IPayrollCalculationVendor[] vendors) =>
        _map = vendors.ToDictionary(v => v.Vendor);

    public IPayrollCalculationVendor Resolve(PayrollVendor vendor) =>
        _map.TryGetValue(vendor, out IPayrollCalculationVendor? impl)
            ? impl
            : throw new InvalidOperationException($"No fake vendor registered for {vendor}.");

    /// <summary>A directory with only the native vendor — the common case for non-vendor tests.</summary>
    public static FakePayrollVendorDirectory Native() => new(new NativeCalculationVendor());
}

public sealed class InMemoryVendorAssignmentRepository : IVendorAssignmentRepository
{
    public List<VendorAssignment> Rows { get; } = [];

    public Task AddAsync(VendorAssignment assignment, CancellationToken ct)
    {
        Rows.Add(assignment);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VendorAssignment>> ListForOrgCountryAsync(
        Guid orgId, CountryCode country, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<VendorAssignment>>(
            [.. Rows.Where(a => a.OrgId == orgId && a.Country == country)]);

    public Task<VendorAssignment?> FindByEffectiveDayAsync(
        Guid orgId, CountryCode country, DateOnly effectiveFrom, CancellationToken ct) =>
        Task.FromResult(Rows.FirstOrDefault(
            a => a.OrgId == orgId && a.Country == country && a.EffectiveFrom == effectiveFrom));
}

public sealed class InMemoryVendorComparisonReportRepository : IVendorComparisonReportRepository
{
    public List<VendorComparisonReport> Rows { get; } = [];

    public Task AddAsync(VendorComparisonReport report, CancellationToken ct)
    {
        Rows.Add(report);
        return Task.CompletedTask;
    }

    public Task<VendorComparisonReport?> FindLatestForRunAsync(Guid runId, Guid orgId, CancellationToken ct) =>
        Task.FromResult(Rows
            .Where(r => r.RunId == runId && r.OrgId == orgId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ThenByDescending(r => r.Id)
            .FirstOrDefault());
}

/// <summary>A shadow-run gate stub: enrol specific orgs, optionally with an expiry, set the tolerance.</summary>
public sealed class StubShadowRunPolicy : IShadowRunPolicy
{
    public HashSet<Guid> Enrolled { get; } = [];
    public DateOnly? EnrolledUntil { get; set; }
    public int DivergenceToleranceMinorUnits { get; set; } = 2;

    public bool IsEnrolled(Guid orgId, DateOnly asOf) =>
        Enrolled.Contains(orgId) && (EnrolledUntil is not { } until || asOf <= until);
}
