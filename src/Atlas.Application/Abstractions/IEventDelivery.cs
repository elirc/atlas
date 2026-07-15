using Atlas.Application.Events;

namespace Atlas.Application.Abstractions;

/// <summary>
/// One outward destination for a domain event, driven by the outbox dispatcher — a log line,
/// a webhook fan-out, a partner bus. The dispatcher owns the durability and retry; a delivery
/// only has to try once and throw if it could not.
/// </summary>
// LEARN: note the split. <see cref="IEventSink"/> is what USE CASES call ("publish this") and its
// only adapter now enqueues an outbox row inside their transaction. IEventDelivery is what the
// DISPATCHER calls ("actually send this"), post-commit, once per adapter. Separating "record the
// intent to deliver" from "perform the delivery" is what lets the intent be transactional while the
// delivery is at-least-once. A throw here means "retry me later"; the dispatcher schedules backoff.
public interface IEventDelivery
{
    Task DeliverAsync(AtlasEvent evt, CancellationToken cancellationToken);
}
