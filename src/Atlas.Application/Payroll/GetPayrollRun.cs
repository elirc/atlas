using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Payroll;

namespace Atlas.Application.Payroll;

/// <summary>Load one run (with payslips) inside the caller's org, or 404.</summary>
public sealed class GetPayrollRun(IPayrollRunRepository runs, ICurrentContext context)
{
    public async Task<PayrollRun> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        context.RequireUser();

        // LEARN: tenancy in the QUERY — FindInOrgAsync filters by (id AND orgId), so another
        // org's run comes back null, indistinguishable from one that never existed. 404, not
        // 403 (ADR-0003).
        PayrollRun? run = await runs.FindInOrgAsync(id, context.RequireOrg(), cancellationToken);

        return run ?? throw new NotFoundException("payroll run", id);
    }
}
