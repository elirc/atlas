using Atlas.Domain.Access;

namespace Atlas.Application.Abstractions;

public interface IApiKeyRepository
{
    Task AddAsync(ApiKey apiKey, CancellationToken cancellationToken);

    /// <summary>
    /// Resolve a presented key by its at-rest hash — the authentication lookup, done BEFORE any org
    /// context exists (the key itself tells us the org). Returns null for an unknown hash.
    /// </summary>
    Task<ApiKey?> FindByHashAsync(string keyHash, CancellationToken cancellationToken);

    Task<ApiKey?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ApiKey>> ListForOrgAsync(Guid orgId, CancellationToken cancellationToken);
}
