using Atlas.Application.Events;

namespace Atlas.Application.Abstractions;

/// <summary>
/// Where domain events go when they leave a use case. One method, one direction.
/// </summary>
// LEARN: this is a PORT for volatility (ADR-0001). Today the only adapter is a
// LoggingEventSink that writes an info line; nothing about the use cases changes
// the day a second adapter fans these events out to a partner system or a webhook
// dispatcher. That is the whole value of naming the seam now, while it is trivial —
// "today a log line; one day, someone else's system." The S10 outbox slots in here.
public interface IEventSink
{
    Task PublishAsync(AtlasEvent evt, CancellationToken cancellationToken);
}
