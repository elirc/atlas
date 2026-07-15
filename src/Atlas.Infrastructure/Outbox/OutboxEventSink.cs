using Atlas.Application.Abstractions;
using Atlas.Application.Events;
using Atlas.Infrastructure.Persistence;

namespace Atlas.Infrastructure.Outbox;

/// <summary>
/// The <see cref="IEventSink"/> adapter that replaces the log-line sink: publishing an event
/// now means ENQUEUEING an <see cref="OutboxMessage"/> through the very same
/// <see cref="AtlasDbContext"/> the use case is about to commit. It does NOT call SaveChanges —
/// the use case's <c>IUnitOfWork.CommitAsync</c> does, and that is the entire point: the event
/// and the state change land (or fail) in one transaction.
/// </summary>
// LEARN: look at what did NOT change to make the phantom-event bug go away — the use cases. Every
// one of them still calls `events.PublishAsync(...)` right before `unitOfWork.CommitAsync(...)`.
// That ordering used to be the bug (publish-before-commit). It is now correct, because "publish"
// means "stage a row in this transaction", not "hit the outside world". The fix was to change what
// the seam DOES, not to touch twenty call sites — the payoff of naming the port back in S05.
internal sealed class OutboxEventSink(AtlasDbContext db) : IEventSink
{
    public Task PublishAsync(AtlasEvent evt, CancellationToken cancellationToken)
    {
        db.Set<OutboxMessage>().Add(OutboxMessage.Enqueue(evt));
        return Task.CompletedTask;
    }
}
