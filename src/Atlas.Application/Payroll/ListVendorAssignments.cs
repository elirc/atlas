using Atlas.Application.Abstractions;
using Atlas.Domain.Access;
using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;

namespace Atlas.Application.Payroll;

/// <summary>
/// The effective-dated vendor history for the caller's org and one country, newest-effective
/// first, plus the vendor currently governing today. The read finance uses to see "what did we
/// switch, when, and what's live now."
/// </summary>
// LEARN: no cursor. Vendor switches are counted on one hand over a company's lifetime (this is
// not a timeline of events), so the whole history loads and the "current" answer is a pure fold
// (VendorSelection.Resolve) over it — the same resolution PreparePayrollRun pins from, computed
// here for display. One source of truth for "which vendor governs", used two ways.
public sealed record VendorHistory(
    CountryCode Country,
    PayrollVendor Current,
    IReadOnlyList<VendorAssignment> Assignments);

public sealed class ListVendorAssignments(
    IVendorAssignmentRepository assignments,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<VendorHistory> ExecuteAsync(string? country, CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        CountryCode countryCode = CountryCode.From(country ?? string.Empty);

        IReadOnlyList<VendorAssignment> history =
            await assignments.ListForOrgCountryAsync(orgId, countryCode, cancellationToken);

        DateOnly today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        PayrollVendor current = VendorSelection.Resolve(history, today, fallback: PayrollVendor.AtlasNative);

        IReadOnlyList<VendorAssignment> ordered =
            [.. history.OrderByDescending(a => a.EffectiveFrom).ThenByDescending(a => a.CreatedAtUtc)];

        return new VendorHistory(countryCode, current, ordered);
    }
}
