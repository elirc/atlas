namespace Atlas.Domain.Auditing;

/// <summary>
/// One append-only fact: who did what to which thing, when. Audit entries are
/// written in the SAME transaction as the change they describe — an audit log
/// that can miss entries is a liability wearing a compliance costume.
/// </summary>
public sealed class AuditEntry
{
    public Guid Id { get; }
    public Guid OrgId { get; }
    public Guid ActorUserId { get; }

    /// <summary>Dotted verb, e.g. "worker.hired", "worker.terms_changed".</summary>
    public string Action { get; }

    public string SubjectType { get; }
    public Guid SubjectId { get; }

    /// <summary>JSON details (before/after fields). Opaque to the domain.</summary>
    public string DetailsJson { get; }

    public DateTimeOffset AtUtc { get; }

    private AuditEntry(
        Guid id,
        Guid orgId,
        Guid actorUserId,
        string action,
        string subjectType,
        Guid subjectId,
        string detailsJson,
        DateTimeOffset atUtc)
    {
        Id = id;
        OrgId = orgId;
        ActorUserId = actorUserId;
        Action = action;
        SubjectType = subjectType;
        SubjectId = subjectId;
        DetailsJson = detailsJson;
        AtUtc = atUtc;
    }

    public static AuditEntry Record(
        Guid orgId,
        Guid actorUserId,
        string action,
        string subjectType,
        Guid subjectId,
        string detailsJson,
        DateTimeOffset atUtc)
    {
        if (string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(subjectType))
        {
            throw new DomainRuleException("Audit entries need an action and a subject type.");
        }

        return new AuditEntry(
            Guid.NewGuid(), orgId, actorUserId, action.Trim(), subjectType.Trim(),
            subjectId, detailsJson, atUtc);
    }

    // WHY: EF Core materialization only.
    private AuditEntry()
    {
        Action = null!;
        SubjectType = null!;
        DetailsJson = null!;
    }
}
