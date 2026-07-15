using System.Text.Json.Nodes;
using Atlas.Application.Abstractions;
using Atlas.Application.Events;
using Atlas.Domain.Webhooks;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Webhooks;

/// <summary>
/// The webhook fan-out, driven by the outbox dispatcher: for each event, find the org's active
/// subscriptions that want its type, sign a payload, POST it, and record a <see cref="WebhookDelivery"/>
/// row per subscription. The delivery ROWS are written through the dispatcher's DbContext, so they
/// commit together with the outbox message being marked dispatched.
/// </summary>
// LEARN: this delivery does NOT throw when a partner endpoint is down. If it did, the outbox would
// retry the whole event and re-POST to the subscriptions that already succeeded — duplicate storms
// from one broken endpoint. Instead each attempt is recorded on its own row (Delivered/Failed), and a
// failed row is redelivered on demand (the redelivery endpoint). At-least-once to the partner is
// therefore explicit and per-endpoint, not an accident of outbox retries. (ADR-0011 records the
// deferral of automatic per-endpoint backoff.)
internal sealed class WebhookEventDelivery(
    AtlasDbContext db,
    IWebhookTransport transport,
    TimeProvider clock) : IEventDelivery
{
    public async Task DeliverAsync(AtlasEvent evt, CancellationToken cancellationToken)
    {
        // The dispatcher's DbContext is the same instance the relay saves — query active subs directly.
        List<WebhookSubscription> subscriptions = await FindActiveSubscriptionsAsync(evt.OrgId, cancellationToken);

        foreach (WebhookSubscription subscription in subscriptions)
        {
            if (!subscription.WantsEvent(evt.Type))
            {
                continue;
            }

            string payload = BuildPayload(evt);
            WebhookDelivery delivery = WebhookDelivery.Queue(
                evt.OrgId, subscription.Id, evt.Type, payload, clock.GetUtcNow());
            db.Set<WebhookDelivery>().Add(delivery);

            await AttemptAsync(subscription, delivery, payload, cancellationToken);
        }
    }

    private Task<List<WebhookSubscription>> FindActiveSubscriptionsAsync(Guid orgId, CancellationToken ct)
    {
        return db.Set<WebhookSubscription>()
            .Where(s => s.OrgId == orgId && s.IsActive)
            .ToListAsync(ct);
    }

    private async Task AttemptAsync(
        WebhookSubscription subscription, WebhookDelivery delivery, string payload, CancellationToken ct)
    {
        WebhookSendResult result = await transport.SendAsync(
            subscription.Url, payload, subscription.Secret, ct);

        if (result.Success)
        {
            delivery.RecordSuccess(result.StatusCode ?? 200, clock.GetUtcNow());
        }
        else
        {
            delivery.RecordFailure(result.Error ?? "delivery failed", result.StatusCode, clock.GetUtcNow());
        }
    }

    private static string BuildPayload(AtlasEvent evt)
    {
        var envelope = new JsonObject
        {
            ["type"] = evt.Type,
            ["orgId"] = JsonValue.Create(evt.OrgId),
            ["subjectType"] = evt.SubjectType,
            ["subjectId"] = JsonValue.Create(evt.SubjectId),
            ["occurredAt"] = JsonValue.Create(evt.AtUtc),
            ["data"] = TryParse(evt.PayloadJson),
        };

        return envelope.ToJsonString();
    }

    private static JsonNode? TryParse(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch
        {
            return JsonValue.Create(json);
        }
    }
}
