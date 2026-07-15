namespace Atlas.Application.Events;

/// <summary>
/// A domain event on its way OUT of Atlas — "an engagement was activated", say.
/// Application-owned and deliberately primitive (a type, an org, a subject, an
/// opaque JSON payload) so the thing that eventually consumes it — a log line
/// today, a webhook or partner system tomorrow — needs no Atlas types.
/// </summary>
// LEARN: this record is the CONTRACT of the notification seam. It looks a lot like
// AuditEntry on purpose — both say "who/what/when about which subject" — but they
// answer different questions: the audit row is the internal, same-transaction
// record of what happened; the event is the outward announcement. S10 is where the
// two stop being able to disagree (the transactional outbox). For now, the sink is
// a log line and the shape is the point.
public sealed record AtlasEvent(
    string Type,
    Guid OrgId,
    string SubjectType,
    Guid SubjectId,
    string PayloadJson,
    DateTimeOffset AtUtc);
