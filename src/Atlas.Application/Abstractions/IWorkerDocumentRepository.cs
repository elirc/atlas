using Atlas.Domain.Compliance;

namespace Atlas.Application.Abstractions;

public interface IWorkerDocumentRepository
{
    Task AddAsync(WorkerDocument document, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkerDocument>> ListForWorkerAsync(
        Guid workerId, Guid orgId, CancellationToken cancellationToken);

    /// <summary>
    /// Expiry-dated documents in an org that expire on or before <paramref name="cutoff"/> — the
    /// expiring-documents view. Only documents that HAVE an expiry are candidates.
    /// </summary>
    Task<IReadOnlyList<WorkerDocument>> ListExpiringForOrgAsync(
        Guid orgId, DateOnly cutoff, CancellationToken cancellationToken);
}
