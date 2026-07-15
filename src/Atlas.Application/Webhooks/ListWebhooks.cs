using Atlas.Application.Abstractions;
using Atlas.Domain.Access;
using Atlas.Domain.Webhooks;

namespace Atlas.Application.Webhooks;

/// <summary>Every webhook subscription in the caller's org, newest first. Admin only; no secrets.</summary>
public sealed class ListWebhooks(IWebhookSubscriptionRepository subscriptions, ICurrentContext context)
{
    public async Task<IReadOnlyList<WebhookSubscription>> ExecuteAsync(CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin);
        Guid orgId = context.RequireOrg();

        return await subscriptions.ListForOrgAsync(orgId, cancellationToken);
    }
}
