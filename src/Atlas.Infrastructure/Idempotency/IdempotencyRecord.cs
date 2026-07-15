namespace Atlas.Infrastructure.Idempotency;

/// <summary>
/// The stored fingerprint + response of a completed money-moving request, keyed by the client's
/// <c>Idempotency-Key</c> within an org. A retry that presents the same key replays THIS response
/// instead of re-executing — so a client that timed out and retried never pays twice.
/// </summary>
// ⚠️ The key alone is not enough — RequestHash is load-bearing. If two DIFFERENT requests reused the
// same key (a client bug), replaying the first response for the second would be silent, dangerous
// nonsense. So the middleware compares the request fingerprint and surfaces a mismatch as a 422
// rather than masking it. "Same key, different body" is a bug to report, not a coincidence to swallow.
internal sealed class IdempotencyRecord
{
    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public string Key { get; private set; }
    public string Method { get; private set; }
    public string Path { get; private set; }
    public string RequestHash { get; private set; }
    public int StatusCode { get; private set; }
    public string? ContentType { get; private set; }
    public byte[] ResponseBody { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public IdempotencyRecord(
        Guid orgId, string key, string method, string path, string requestHash,
        int statusCode, string? contentType, byte[] responseBody, DateTimeOffset createdAtUtc)
    {
        Id = Guid.NewGuid();
        OrgId = orgId;
        Key = key;
        Method = method;
        Path = path;
        RequestHash = requestHash;
        StatusCode = statusCode;
        ContentType = contentType;
        ResponseBody = responseBody;
        CreatedAtUtc = createdAtUtc;
    }

    // EF materialization only.
    private IdempotencyRecord()
    {
        Key = null!;
        Method = null!;
        Path = null!;
        RequestHash = null!;
        ResponseBody = null!;
    }
}
