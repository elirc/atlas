using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Events;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Webhooks;

namespace Atlas.Application.Webhooks;

/// <summary>The subscription plus its signing secret — the ONLY moment the secret is revealed.</summary>
public sealed record WebhookRegistration(WebhookSubscription Subscription, string Secret);

/// <summary>
/// Register a partner endpoint to receive signed event deliveries. The signing secret is minted here,
/// stored on the subscription, and returned exactly once — the caller must save it, because the API
/// never shows it again (the list view omits it).
/// </summary>
public sealed class RegisterWebhook(
    IWebhookSubscriptionRepository subscriptions,
    ISecretGenerator secrets,
    IAuditTrail audit,
    IEventSink events,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<WebhookRegistration> ExecuteAsync(
        string? url, IReadOnlyList<string>? eventTypes, CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin);
        Guid orgId = context.RequireOrg();
        DateTimeOffset now = clock.GetUtcNow();

        string secret = secrets.NewSecret("whsec_");
        WebhookSubscription subscription = WebhookSubscription.Create(orgId, url, secret, eventTypes, now);

        await subscriptions.AddAsync(subscription, cancellationToken);

        string details = JsonSerializer.Serialize(new { subscription.Url, subscription.EventFilter });
        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "webhook.subscription.created", "webhook_subscription",
            subscription.Id, details, now));

        await events.PublishAsync(
            new AtlasEvent("webhook.subscription.created", orgId, "webhook_subscription",
                subscription.Id, details, now),
            cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);

        return new WebhookRegistration(subscription, secret);
    }
}
