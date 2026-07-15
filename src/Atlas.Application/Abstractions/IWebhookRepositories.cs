using Atlas.Domain.Webhooks;

namespace Atlas.Application.Abstractions;

public interface IWebhookSubscriptionRepository
{
    Task AddAsync(WebhookSubscription subscription, CancellationToken cancellationToken);

    Task<WebhookSubscription?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookSubscription>> ListForOrgAsync(Guid orgId, CancellationToken cancellationToken);

    /// <summary>Active subscriptions for an org — the fan-out set the dispatcher delivers into.</summary>
    Task<IReadOnlyList<WebhookSubscription>> ListActiveForOrgAsync(Guid orgId, CancellationToken cancellationToken);
}

public interface IWebhookDeliveryRepository
{
    Task AddAsync(WebhookDelivery delivery, CancellationToken cancellationToken);

    Task<WebhookDelivery?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookDelivery>> ListForOrgAsync(
        Guid orgId, Guid? subscriptionId, CancellationToken cancellationToken);
}
