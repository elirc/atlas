using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Events;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Webhooks;

namespace Atlas.Application.Webhooks;

/// <summary>
/// Pause a subscription — it stops receiving deliveries but its history stays queryable. We PAUSE
/// rather than hard-delete so the delivery log (and any in-flight investigation) survives; a resume
/// path is a trivial follow-up. Idempotent: pausing a paused subscription is a no-op.
/// </summary>
public sealed class PauseWebhook(
    IWebhookSubscriptionRepository subscriptions,
    IAuditTrail audit,
    IEventSink events,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<WebhookSubscription> ExecuteAsync(Guid subscriptionId, CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin);
        Guid orgId = context.RequireOrg();

        WebhookSubscription subscription =
            await subscriptions.FindInOrgAsync(subscriptionId, orgId, cancellationToken)
            ?? throw new NotFoundException("webhook_subscription", subscriptionId);

        DateTimeOffset now = clock.GetUtcNow();
        subscription.Pause(now);

        string details = JsonSerializer.Serialize(new { subscription.Url });
        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "webhook.subscription.paused", "webhook_subscription",
            subscription.Id, details, now));

        await events.PublishAsync(
            new AtlasEvent("webhook.subscription.paused", orgId, "webhook_subscription",
                subscription.Id, details, now),
            cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);

        return subscription;
    }
}
