using Atlas.Application.Abstractions;
using Atlas.Domain.Access;
using Atlas.Domain.Webhooks;

namespace Atlas.Application.Webhooks;

/// <summary>
/// The delivery log for the org, optionally narrowed to one subscription — the "watch backoff, then
/// redeliver" view. Shows each attempt's status, attempt count, and last error.
/// </summary>
public sealed class ListWebhookDeliveries(IWebhookDeliveryRepository deliveries, ICurrentContext context)
{
    public async Task<IReadOnlyList<WebhookDelivery>> ExecuteAsync(
        Guid? subscriptionId, CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin);
        Guid orgId = context.RequireOrg();

        return await deliveries.ListForOrgAsync(orgId, subscriptionId, cancellationToken);
    }
}
