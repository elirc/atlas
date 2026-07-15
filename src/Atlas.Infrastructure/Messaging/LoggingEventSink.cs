using Atlas.Application.Abstractions;
using Atlas.Application.Events;
using Microsoft.Extensions.Logging;

namespace Atlas.Infrastructure.Messaging;

/// <summary>
/// The only <see cref="IEventSink"/> adapter for now: it writes an info line. That is
/// the honest MVP of "events leave the system" — visible, zero-infrastructure, and a
/// placeholder that names the seam without pretending to be delivery.
/// </summary>
// LEARN: nothing about the use cases knows this is a log line rather than a webhook
// dispatcher or a partner integration. That's the port earning its keep before it
// has any rent to pay. S10 replaces this adapter with a transactional outbox — the
// use cases don't change, only what sits behind IEventSink does.
internal sealed class LoggingEventSink(ILogger<LoggingEventSink> logger) : IEventSink
{
    public Task PublishAsync(AtlasEvent evt, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Atlas event {EventType} for {SubjectType} {SubjectId} in org {OrgId} at {AtUtc}",
            evt.Type, evt.SubjectType, evt.SubjectId, evt.OrgId, evt.AtUtc);

        return Task.CompletedTask;
    }
}
