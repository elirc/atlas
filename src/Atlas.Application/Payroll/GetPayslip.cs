using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Payroll;

namespace Atlas.Application.Payroll;

/// <summary>Load one payslip (with lines) inside the caller's org, or 404.</summary>
// LEARN: a payslip is org-scoped by its own OrgId column (denormalised from the run), so
// this is a single tenant-safe by-id read — no join back to the run needed. Cross-tenant
// reads are 404, never 403 (ADR-0003).
public sealed class GetPayslip(IPayslipRepository payslips, ICurrentContext context)
{
    public async Task<Payslip> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        context.RequireUser();

        Payslip? payslip = await payslips.FindInOrgAsync(id, context.RequireOrg(), cancellationToken);

        return payslip ?? throw new NotFoundException("payslip", id);
    }
}
