namespace Atlas.Application.Abstractions;

/// <summary>A previously-recorded response, replayed for a repeated idempotency key.</summary>
public sealed record StoredResponse(string RequestHash, int StatusCode, string? ContentType, byte[] Body);

/// <summary>
/// Remembers the response of a completed money-moving request so a retry can replay it instead of
/// re-executing. Scoped per org + key. Only successful (2xx) responses are worth storing — a failed
/// call left nothing to be idempotent about.
/// </summary>
// LEARN: this is a PORT, not a middleware detail, because "have we already done this exact request?"
// is a question the transport layer asks but the STORAGE layer answers — and storage is swappable
// (a table today, Redis with a TTL tomorrow). The middleware in Atlas.Api depends on this interface,
// never on EF.
public interface IIdempotencyStore
{
    Task<StoredResponse?> FindAsync(Guid orgId, string key, CancellationToken cancellationToken);

    Task SaveAsync(
        Guid orgId,
        string key,
        string method,
        string path,
        string requestHash,
        int statusCode,
        string? contentType,
        byte[] body,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}
