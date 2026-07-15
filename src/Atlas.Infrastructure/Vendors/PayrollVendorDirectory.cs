using Atlas.Application.Abstractions;
using Atlas.Domain.Payroll;

namespace Atlas.Infrastructure.Vendors;

/// <summary>
/// Indexes every registered <see cref="IPayrollCalculationVendor"/> by the vendor it speaks for,
/// so a use case can turn the <see cref="PayrollVendor"/> a run pinned into the engine that
/// computes it. Built once from the DI-registered set; a missing vendor is a wiring bug, not a
/// runtime maybe.
/// </summary>
// LEARN: the composition of a set of adapters behind one lookup — the same shape CountryRulePacks
// uses for rule packs, only registered explicitly through DI rather than discovered by reflection
// (there are two vendors, hand-wired in DependencyInjection, not a growing plugin family). A
// duplicate registration for one vendor is an authoring bug we refuse at construction, and a
// Resolve for an unregistered vendor throws — both fail LOUD, at first touch, the way the whole
// codebase treats "our map of the world is wrong" (ADR-0010, ADR-0013).
internal sealed class PayrollVendorDirectory : IPayrollVendorDirectory
{
    private readonly IReadOnlyDictionary<PayrollVendor, IPayrollCalculationVendor> _byVendor;

    public PayrollVendorDirectory(IEnumerable<IPayrollCalculationVendor> vendors)
    {
        var map = new Dictionary<PayrollVendor, IPayrollCalculationVendor>();
        foreach (IPayrollCalculationVendor vendor in vendors)
        {
            if (!map.TryAdd(vendor.Vendor, vendor))
            {
                throw new InvalidOperationException(
                    $"Two calculation adapters are registered for {vendor.Vendor}: " +
                    $"{map[vendor.Vendor].GetType().Name} and {vendor.GetType().Name}.");
            }
        }

        _byVendor = map;
    }

    public IPayrollCalculationVendor Resolve(PayrollVendor vendor) =>
        _byVendor.TryGetValue(vendor, out IPayrollCalculationVendor? impl)
            ? impl
            // A run pinned a vendor that has no registered adapter — a deployment/wiring hole, not
            // a business outcome. Throw so it surfaces as a loud 500 that pages someone, never a
            // silent "compute nothing" that would leave a payroll run mysteriously empty.
            : throw new InvalidOperationException(
                $"No payroll calculation adapter is registered for vendor '{vendor}'.");
}
