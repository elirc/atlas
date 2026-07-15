using Atlas.Application.ApiKeys;
using Atlas.Application.Webhooks;
using Atlas.Contracts.V1;
using Atlas.Domain.Access;
using Atlas.Domain.Webhooks;

namespace Atlas.Api.Mapping;

internal static class IntegrationMapping
{
    public static WebhookSubscriptionDto ToDto(this WebhookSubscription s) =>
        new(s.Id, s.Url, s.EventFilter, s.IsActive, s.CreatedAtUtc, s.PausedAtUtc);

    public static WebhookRegisteredDto ToDto(this WebhookRegistration registration) =>
        new(registration.Subscription.ToDto(), registration.Secret);

    public static WebhookDeliveryDto ToDto(this WebhookDelivery d) =>
        new(d.Id, d.SubscriptionId, d.EventType, d.Status.ToString(), d.Attempts,
            d.ResponseStatusCode, d.LastError, d.CreatedAtUtc, d.DeliveredAtUtc);

    public static ApiKeyDto ToDto(this ApiKey k) =>
        new(k.Id, k.Name, k.DisplayPrefix, k.ScopeCsv, k.IsActive, k.CreatedAtUtc, k.RevokedAtUtc);

    public static ApiKeyCreatedDto ToDto(this ApiKeyIssue issue) =>
        new(issue.ApiKey.ToDto(), issue.Plaintext);
}
