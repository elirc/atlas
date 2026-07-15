using Atlas.Application.Events;

namespace Atlas.Infrastructure.Outbox;

/// <summary>
/// A domain event, durably parked in the SAME transaction as the state change that
/// produced it, waiting for a dispatcher to deliver it. This row is the whole trick:
/// because it is written through the use case's <see cref="AtlasDbContext"/>, it commits
/// (or rolls back) atomically with the business change. No commit, no row, no phantom.
/// </summary>
// LEARN: this is the transactional-outbox pattern. The two tempting-but-wrong options are
//   (a) publish BEFORE commit — a rolled-back transaction still fired the event (phantom); and
//   (b) publish AFTER commit — a crash in the gap loses the event forever.
// The outbox is the honest third option: the event becomes a row in the transaction, and a
// separate dispatcher turns rows into deliveries with at-least-once semantics. Delivery can
// double (crash after send, before mark) — which is exactly why consumers must be idempotent,
// the other half of this sprint. See ADR-0011 and docs/concepts/at-least-once.md.
internal sealed class OutboxMessage
{
    public const int MaxAttempts = 12;

    public Guid Id { get; private set; }
    public string Type { get; private set; }
    public Guid OrgId { get; private set; }
    public string SubjectType { get; private set; }
    public Guid SubjectId { get; private set; }
    public string PayloadJson { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }

    // Delivery bookkeeping — all mapped, none computed (ModelMappingTests enforces).
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAtUtc { get; private set; }
    public DateTimeOffset? DispatchedAtUtc { get; private set; }
    public string? LastError { get; private set; }

    private OutboxMessage(
        Guid id, string type, Guid orgId, string subjectType, Guid subjectId,
        string payloadJson, DateTimeOffset occurredAtUtc)
    {
        Id = id;
        Type = type;
        OrgId = orgId;
        SubjectType = subjectType;
        SubjectId = subjectId;
        PayloadJson = payloadJson;
        OccurredAtUtc = occurredAtUtc;
        NextAttemptAtUtc = occurredAtUtc; // eligible immediately
    }

    public static OutboxMessage Enqueue(AtlasEvent evt) =>
        new(Guid.NewGuid(), evt.Type, evt.OrgId, evt.SubjectType, evt.SubjectId, evt.PayloadJson, evt.AtUtc);

    /// <summary>Reconstruct the event the dispatcher hands to the delivery adapters.</summary>
    public AtlasEvent ToEvent() =>
        new(Type, OrgId, SubjectType, SubjectId, PayloadJson, OccurredAtUtc);

    public void MarkDispatched(DateTimeOffset nowUtc)
    {
        DispatchedAtUtc = nowUtc;
        LastError = null;
    }

    /// <summary>
    /// Record a failed delivery and schedule the next attempt with EXPONENTIAL BACKOFF, capped.
    /// After <see cref="MaxAttempts"/> the message is parked (NextAttempt far future) as a poison
    /// message — it stops burning the dispatcher and waits for a human, rather than looping forever.
    /// </summary>
    public void MarkFailed(string error, DateTimeOffset nowUtc)
    {
        Attempts++;
        LastError = error;

        if (Attempts >= MaxAttempts)
        {
            // Poison: park it a century out. A human redelivers after fixing the cause.
            NextAttemptAtUtc = nowUtc.AddYears(100);
            return;
        }

        // 2^attempts seconds, capped at 1 hour: 2s, 4s, 8s, ... 1h.
        double seconds = Math.Min(3600, Math.Pow(2, Attempts));
        NextAttemptAtUtc = nowUtc.AddSeconds(seconds);
    }

    // EF materialization only.
    private OutboxMessage()
    {
        Type = null!;
        SubjectType = null!;
        PayloadJson = null!;
    }
}
