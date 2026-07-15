using Atlas.Domain.Countries;

namespace Atlas.Domain.Compliance;

/// <summary>
/// A compliance document held for a worker: a kind (matching what the country's rule pack requires),
/// an opaque reference (we store metadata, never the binary — S11 scope), and, for expiry-tracked
/// kinds, an expiry date. Atlas records that the document EXISTS and when it lapses; the file itself
/// lives elsewhere.
/// </summary>
public sealed class WorkerDocument
{
    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid WorkerId { get; private set; }
    public DocumentKind Kind { get; private set; }
    public string Reference { get; private set; }
    public DateOnly? ExpiresOn { get; private set; }
    public DateTimeOffset RecordedAtUtc { get; private set; }

    private WorkerDocument(
        Guid orgId, Guid workerId, DocumentKind kind, string reference, DateOnly? expiresOn, DateTimeOffset recordedAtUtc)
    {
        Id = Guid.NewGuid();
        OrgId = orgId;
        WorkerId = workerId;
        Kind = kind;
        Reference = reference;
        ExpiresOn = expiresOn;
        RecordedAtUtc = recordedAtUtc;
    }

    public static WorkerDocument Record(
        Guid orgId, Guid workerId, DocumentKind kind, string? reference, DateOnly? expiresOn, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new DomainRuleException("A document needs a reference (an identifier, not the file itself).");
        }

        return new WorkerDocument(orgId, workerId, kind, reference.Trim(), expiresOn, nowUtc);
    }

    // EF materialization only.
    private WorkerDocument()
    {
        Reference = null!;
    }
}
