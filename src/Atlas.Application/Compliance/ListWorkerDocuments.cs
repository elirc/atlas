using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Access;
using Atlas.Domain.Compliance;

namespace Atlas.Application.Compliance;

/// <summary>Every recorded document for one worker in the caller's org. HR/admin only.</summary>
public sealed class ListWorkerDocuments(
    IWorkerRepository workers,
    IWorkerDocumentRepository documents,
    ICurrentContext context)
{
    public async Task<IReadOnlyList<WorkerDocument>> ExecuteAsync(Guid workerId, CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        _ = await workers.FindInOrgAsync(workerId, orgId, cancellationToken)
            ?? throw new NotFoundException("worker", workerId);

        return await documents.ListForWorkerAsync(workerId, orgId, cancellationToken);
    }
}
