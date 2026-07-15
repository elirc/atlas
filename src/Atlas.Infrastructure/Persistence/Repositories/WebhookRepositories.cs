using Atlas.Application.Abstractions;
using Atlas.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Persistence.Repositories;

internal sealed class WebhookSubscriptionRepository(AtlasDbContext db) : IWebhookSubscriptionRepository
{
    public async Task AddAsync(WebhookSubscription subscription, CancellationToken ct) =>
        await db.Set<WebhookSubscription>().AddAsync(subscription, ct);

    public Task<WebhookSubscription?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken ct) =>
        db.Set<WebhookSubscription>().SingleOrDefaultAsync(s => s.Id == id && s.OrgId == orgId, ct);

    public async Task<IReadOnlyList<WebhookSubscription>> ListForOrgAsync(Guid orgId, CancellationToken ct) =>
        await db.Set<WebhookSubscription>().AsNoTracking()
            .Where(s => s.OrgId == orgId)
            .OrderByDescending(s => s.CreatedAtUtc).ThenByDescending(s => s.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<WebhookSubscription>> ListActiveForOrgAsync(Guid orgId, CancellationToken ct) =>
        await db.Set<WebhookSubscription>().AsNoTracking()
            .Where(s => s.OrgId == orgId && s.IsActive)
            .ToListAsync(ct);
}

internal sealed class WebhookDeliveryRepository(AtlasDbContext db) : IWebhookDeliveryRepository
{
    public async Task AddAsync(WebhookDelivery delivery, CancellationToken ct) =>
        await db.Set<WebhookDelivery>().AddAsync(delivery, ct);

    public Task<WebhookDelivery?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken ct) =>
        db.Set<WebhookDelivery>().SingleOrDefaultAsync(d => d.Id == id && d.OrgId == orgId, ct);

    public async Task<IReadOnlyList<WebhookDelivery>> ListForOrgAsync(
        Guid orgId, Guid? subscriptionId, CancellationToken ct)
    {
        IQueryable<WebhookDelivery> query = db.Set<WebhookDelivery>().AsNoTracking()
            .Where(d => d.OrgId == orgId);

        if (subscriptionId is { } subId)
        {
            query = query.Where(d => d.SubscriptionId == subId);
        }

        return await query
            .OrderByDescending(d => d.CreatedAtUtc).ThenByDescending(d => d.Id)
            .Take(200)
            .ToListAsync(ct);
    }
}
