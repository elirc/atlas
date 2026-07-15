using Atlas.Application.Abstractions;
using Atlas.Domain.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Persistence.Repositories;

internal sealed class WorkerDocumentRepository(AtlasDbContext db) : IWorkerDocumentRepository
{
    public async Task AddAsync(WorkerDocument document, CancellationToken ct) =>
        await db.Set<WorkerDocument>().AddAsync(document, ct);

    public async Task<IReadOnlyList<WorkerDocument>> ListForWorkerAsync(Guid workerId, Guid orgId, CancellationToken ct) =>
        await db.Set<WorkerDocument>().AsNoTracking()
            .Where(d => d.WorkerId == workerId && d.OrgId == orgId)
            .OrderBy(d => d.Kind).ThenBy(d => d.ExpiresOn)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<WorkerDocument>> ListExpiringForOrgAsync(Guid orgId, DateOnly cutoff, CancellationToken ct) =>
        await db.Set<WorkerDocument>().AsNoTracking()
            .Where(d => d.OrgId == orgId && d.ExpiresOn != null && d.ExpiresOn <= cutoff)
            .OrderBy(d => d.ExpiresOn).ThenBy(d => d.WorkerId)
            .ToListAsync(ct);
}
