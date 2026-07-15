using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Access;

namespace Atlas.Application.Reporting;

/// <summary>
/// Employment cost for one month, grouped by country and currency, folded from EXECUTED payroll runs.
/// </summary>
// 🔗 THIS REPORT CAN BE TRUSTED BECAUSE S7 MADE PAYSLIPS IMMUTABLE. It sums only payslips belonging
// to EXECUTED runs — and an executed run is frozen (Payslip has no mutators; PayrollRun.Execute is
// terminal). So the number this report shows for March is the number that was true when March was
// executed, and it cannot drift because someone edited a payslip afterwards. A cost report built over
// a mutable write model would be a lie on a delay. The immutability we paid for in S7 is what makes
// the projection here safe to publish. (ADR-0008 on execute-to-records; ADR-0017 on read models.)
public sealed class GetEmploymentCostReport(IReportQueries reports, ICurrentContext context)
{
    public const int EarliestYear = 2020;

    public async Task<IReadOnlyList<EmploymentCostRow>> ExecuteAsync(
        int year, int month, CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        // Parse-and-reject the period, the ListInvoices stance: a bad ?month=13 is a client mistake
        // worth a 400, not an empty report that hides the typo.
        if (month is < 1 or > 12)
        {
            throw new RequestValidationException("month must be between 1 and 12.");
        }

        if (year < EarliestYear || year > DateTime.UtcNow.Year + 1)
        {
            throw new RequestValidationException(
                $"year must be between {EarliestYear} and {DateTime.UtcNow.Year + 1}.");
        }

        return await reports.EmploymentCostByCountryMonthAsync(orgId, year, month, cancellationToken);
    }
}
