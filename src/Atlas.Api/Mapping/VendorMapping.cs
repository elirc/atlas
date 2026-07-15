using Atlas.Application.Errors;
using Atlas.Application.Payroll;
using Atlas.Contracts.V1;
using Atlas.Domain.Payroll;

namespace Atlas.Api.Mapping;

// Vendor selection + shadow-comparison translations (S12). Kept beside the payroll maps; the same
// Atlas.Api.Mapping namespace brings every .ToDto() into scope for the endpoints.
internal static class VendorMapping
{
    /// <summary>Parse the wire vendor string to the enum at the edge — an unknown name is a clean 400.</summary>
    // WHY parse here, not in the use case: the use case's parameter is a PayrollVendor (a domain
    // concept); turning the client's string into that concept — and rejecting a typo before any
    // business logic runs — is an edge responsibility. Enum.TryParse alone would accept the numeric
    // "2" or an undefined value, so we also require Enum.IsDefined.
    public static PayrollVendor ParseVendor(string? raw) =>
        Enum.TryParse(raw, ignoreCase: true, out PayrollVendor vendor) && Enum.IsDefined(vendor)
            ? vendor
            : throw new RequestValidationException($"'{raw}' is not a known payroll vendor.");

    public static VendorAssignmentDto ToDto(this VendorAssignment assignment) =>
        new(assignment.Id, assignment.Country.Value, assignment.Vendor.ToString(),
            assignment.EffectiveFrom, assignment.CreatedAtUtc);

    public static VendorHistoryDto ToDto(this VendorHistory history) =>
        new(history.Country.Value, history.Current.ToString(),
            [.. history.Assignments.Select(a => a.ToDto())]);

    public static VendorComparisonLineDto ToDto(this VendorComparisonLine line) =>
        new(line.WorkerId, line.GrossDeltaMinor, line.NetDeltaMinor,
            line.EmployerCostDeltaMinor, line.Currency, line.Divergent);

    public static VendorComparisonReportDto ToDto(this VendorComparisonReport report) =>
        new(report.RunId, report.Country.Value,
            report.PrimaryVendor.ToString(), report.ShadowVendor.ToString(),
            report.ToleranceMinorUnits, report.WorkerCount, report.DivergentCount,
            report.MaxAbsNetDeltaMinor, report.IsClean, report.CreatedAtUtc,
            [.. report.Lines.Select(l => l.ToDto())]);
}
