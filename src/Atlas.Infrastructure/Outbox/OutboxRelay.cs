using Atlas.Application.Abstractions;
using Atlas.Application.Events;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Atlas.Infrastructure.Outbox;

/// <summary>
/// The dispatcher's engine: read a batch of due, undelivered <see cref="OutboxMessage"/> rows and
/// push each through every <see cref="IEventDelivery"/>. A message is dispatched only when ALL of
/// its deliveries succeed; any failure schedules a backed-off retry and leaves the row for the next
/// drain. Idempotent to run: nothing to do is a no-op.
/// </summary>
// 🔍 Reviewer's lens for any background worker: read it for its FAILURE modes first, not its happy
// path. Here they are, named on purpose:
//   * Poison message — a delivery that never succeeds is capped at OutboxMessage.MaxAttempts, then
//     parked (not looped) so it stops starving healthy traffic.
//   * At-least-once — we deliver, THEN mark dispatched. A crash in that gap redelivers on the next
//     drain. We never mark-then-deliver (that would be at-most-once: a lost event on crash).
//   * Ordering — best-effort by OccurredAtUtc; NOT a total order guarantee. Consumers must not
//     assume it, which is why events carry their own timestamp and subject.
//   * Batch bound — a fixed page per drain, so a backlog cannot allocate the world in one pass.
public sealed class OutboxRelay(
    AtlasDbContext db,
    IEnumerable<IEventDelivery> deliveries,
    TimeProvider clock,
    ILogger<OutboxRelay> logger)
{
    public const int BatchSize = 100;

    /// <summary>
    /// Drain one batch. Returns the number of messages successfully dispatched — tests assert on it,
    /// and the hosted service uses it to decide whether to poll again immediately or sleep.
    /// </summary>
    public async Task<int> DrainOnceAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();

        List<OutboxMessage> due = await db.Set<OutboxMessage>()
            .Where(m => m.DispatchedAtUtc == null && m.NextAttemptAtUtc <= now)
            .OrderBy(m => m.OccurredAtUtc)
            .ThenBy(m => m.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        int dispatched = 0;

        foreach (OutboxMessage message in due)
        {
            AtlasEvent evt = message.ToEvent();
            try
            {
                foreach (IEventDelivery delivery in deliveries)
                {
                    await delivery.DeliverAsync(evt, cancellationToken);
                }

                message.MarkDispatched(clock.GetUtcNow());
                dispatched++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failed delivery is not a crash — it is expected weather. Record it, back off,
                // and move to the next message so one broken endpoint cannot stall the whole batch.
                message.MarkFailed(ex.Message, clock.GetUtcNow());
                logger.LogWarning(ex,
                    "Outbox delivery failed for {EventType} {MessageId}; attempt {Attempts}",
                    message.Type, message.Id, message.Attempts);
            }
        }

        if (due.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return dispatched;
    }
}
