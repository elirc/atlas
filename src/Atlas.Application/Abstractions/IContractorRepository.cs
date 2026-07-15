using Atlas.Domain.Invoicing;

namespace Atlas.Application.Abstractions;

/// <summary>Persistence port for <see cref="Contractor"/> — org-scoped, like every other repo.</summary>
public interface IContractorRepository
{
    Task AddAsync(Contractor contractor, CancellationToken cancellationToken);

    /// <summary>The contractor with this id IN this org, or null (a cross-org id reads as absent).</summary>
    Task<Contractor?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken cancellationToken);

    /// <summary>Every contractor in the org, newest first. A small reference list — no cursor (yet).</summary>
    Task<IReadOnlyList<Contractor>> ListForOrgAsync(Guid orgId, CancellationToken cancellationToken);
}
