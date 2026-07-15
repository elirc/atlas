using Atlas.Application.Abstractions;
using Atlas.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Persistence.Repositories;

internal sealed class ApiKeyRepository(AtlasDbContext db) : IApiKeyRepository
{
    public async Task AddAsync(ApiKey apiKey, CancellationToken ct) =>
        await db.Set<ApiKey>().AddAsync(apiKey, ct);

    // Auth lookup: by hash, across the whole table (the key carries its own org). AsNoTracking —
    // authentication reads, never writes.
    public Task<ApiKey?> FindByHashAsync(string keyHash, CancellationToken ct) =>
        db.Set<ApiKey>().AsNoTracking().SingleOrDefaultAsync(k => k.KeyHash == keyHash, ct);

    public Task<ApiKey?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken ct) =>
        db.Set<ApiKey>().SingleOrDefaultAsync(k => k.Id == id && k.OrgId == orgId, ct);

    public async Task<IReadOnlyList<ApiKey>> ListForOrgAsync(Guid orgId, CancellationToken ct) =>
        await db.Set<ApiKey>().AsNoTracking()
            .Where(k => k.OrgId == orgId)
            .OrderByDescending(k => k.CreatedAtUtc).ThenByDescending(k => k.Id)
            .ToListAsync(ct);
}
