using Atlas.Application.Abstractions;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Idempotency;

/// <summary>
/// EF-backed idempotency store. It works through its OWN short-lived <see cref="AtlasDbContext"/>
/// (built from the shared options) rather than the request's context — on purpose: recording the
/// response must never accidentally flush a use case's tracked-but-uncommitted changes. The record
/// is written after the endpoint's own transaction has already committed.
/// </summary>
internal sealed class EfIdempotencyStore(DbContextOptions<AtlasDbContext> options) : IIdempotencyStore
{
    public async Task<StoredResponse?> FindAsync(Guid orgId, string key, CancellationToken cancellationToken)
    {
        await using var db = new AtlasDbContext(options);

        return await db.Set<IdempotencyRecord>()
            .AsNoTracking()
            .Where(r => r.OrgId == orgId && r.Key == key)
            .Select(r => new StoredResponse(r.RequestHash, r.StatusCode, r.ContentType, r.ResponseBody))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task SaveAsync(
        Guid orgId, string key, string method, string path, string requestHash,
        int statusCode, string? contentType, byte[] body, DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var db = new AtlasDbContext(options);
        db.Set<IdempotencyRecord>().Add(new IdempotencyRecord(
            orgId, key, method, path, requestHash, statusCode, contentType, body, nowUtc));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent request with the same key won the race and already stored its response.
            // That is fine: the unique (OrgId, Key) index did its job; this write is simply redundant.
        }
    }
}
