using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Access;
using Atlas.Domain.Webhooks;

namespace Atlas.Application.Webhooks;

/// <summary>
/// Re-attempt one recorded delivery — the human's answer to a partner endpoint that was briefly down.
/// It re-signs and re-sends the ORIGINAL payload (so signatures stay valid against the stored body),
/// then updates the same row's status and attempt count. Because the payload is replayed verbatim, a
/// partner that already processed it can dedupe on the event — at-least-once, honestly.
/// </summary>
public sealed class RedeliverWebhook(
    IWebhookDeliveryRepository deliveries,
    IWebhookSubscriptionRepository subscriptions,
    IWebhookTransport transport,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<WebhookDelivery> ExecuteAsync(Guid deliveryId, CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin);
        Guid orgId = context.RequireOrg();

        WebhookDelivery delivery = await deliveries.FindInOrgAsync(deliveryId, orgId, cancellationToken)
            ?? throw new NotFoundException("webhook_delivery", deliveryId);

        WebhookSubscription subscription =
            await subscriptions.FindInOrgAsync(delivery.SubscriptionId, orgId, cancellationToken)
            ?? throw new NotFoundException("webhook_subscription", delivery.SubscriptionId);

        WebhookSendResult result = await transport.SendAsync(
            subscription.Url, delivery.PayloadJson, subscription.Secret, cancellationToken);

        DateTimeOffset now = clock.GetUtcNow();
        if (result.Success)
        {
            delivery.RecordSuccess(result.StatusCode ?? 200, now);
        }
        else
        {
            delivery.RecordFailure(result.Error ?? "delivery failed", result.StatusCode, now);
        }

        await unitOfWork.CommitAsync(cancellationToken);

        return delivery;
    }
}
