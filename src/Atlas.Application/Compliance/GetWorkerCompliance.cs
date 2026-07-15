using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Access;
using Atlas.Domain.Compliance;
using Atlas.Domain.Countries;
using Atlas.Domain.Workforce;

namespace Atlas.Application.Compliance;

/// <summary>The worker, plus their compliance rollup against their country's required documents.</summary>
public sealed record WorkerComplianceResult(Worker Worker, WorkerComplianceReport Report);

/// <summary>
/// Evaluate a worker's document compliance: their country's rule pack declares the required documents,
/// the worker's held documents are folded against them as of today, and the pure
/// <see cref="ComplianceEvaluator"/> returns the verdict. The country's requirements come from the
/// PLUGIN — so a JPN or CAN worker is evaluated the instant their pack file exists, with no change here.
/// </summary>
public sealed class GetWorkerCompliance(
    IWorkerRepository workers,
    IWorkerDocumentRepository documents,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<WorkerComplianceResult> ExecuteAsync(Guid workerId, CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        // Tenant-safe by-id read (FindInOrgAsync, not FindAsync) — compliance never leaks across orgs.
        Worker worker = await workers.FindInOrgAsync(workerId, orgId, cancellationToken)
            ?? throw new NotFoundException("worker", workerId);

        ICountryRulePack pack = CountryRulePacks.For(worker.Country);
        IReadOnlyList<WorkerDocument> held = await documents.ListForWorkerAsync(workerId, orgId, cancellationToken);

        DateOnly today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        WorkerComplianceReport report = ComplianceEvaluator.Evaluate(pack.RequiredDocuments, held, today);

        return new WorkerComplianceResult(worker, report);
    }
}
