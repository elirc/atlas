using Atlas.Application.Abstractions;
using Atlas.Application.Events;
using Microsoft.Extensions.Logging;

namespace Atlas.Infrastructure.Outbox;

/// <summary>
/// The always-on delivery target: a structured log line. It is what the old LoggingEventSink used
/// to do inline — but now it runs from the DISPATCHER, after commit, so a rolled-back transaction
/// never produces one. Same output, honest timing.
/// </summary>
internal sealed class LoggingEventDelivery(ILogger<LoggingEventDelivery> logger) : IEventDelivery
{
    public Task DeliverAsync(AtlasEvent evt, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Atlas event {EventType} for {SubjectType} {SubjectId} in org {OrgId} at {AtUtc}",
            evt.Type, evt.SubjectType, evt.SubjectId, evt.OrgId, evt.AtUtc);

        return Task.CompletedTask;
    }
}
