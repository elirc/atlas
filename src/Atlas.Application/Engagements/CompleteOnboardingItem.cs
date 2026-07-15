using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Events;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Engagements;

namespace Atlas.Application.Engagements;

/// <summary>
/// Mark one onboarding checklist item complete. The first completion moves the
/// engagement Contract → Onboarding; the rest keep it there. Idempotent: completing
/// an already-done item is a successful no-op that writes nothing.
/// </summary>
public sealed class CompleteOnboardingItem(
    IEngagementRepository engagements,
    IAuditTrail audit,
    IEventSink events,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<Engagement> ExecuteAsync(
        Guid engagementId,
        string itemName,
        CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        Engagement engagement =
            await engagements.FindInOrgAsync(engagementId, orgId, cancellationToken)
            ?? throw new NotFoundException("engagement", engagementId);

        DateTimeOffset now = clock.GetUtcNow();
        bool changed = engagement.CompleteOnboardingItem(itemName, now);

        // WHY: idempotency lives here. The aggregate already refused to double-mark
        // the item (returned false); we honor that by writing NOTHING — no audit
        // row, no event, no commit. A client that retries the request gets the same
        // 200 and the same single activity row, not a duplicate timeline entry.
        if (!changed)
        {
            return engagement;
        }

        string details = JsonSerializer.Serialize(new
        {
            Item = itemName,
            ResultingState = engagement.State.ToString(),
        });

        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "engagement.onboarding_item_completed",
            "engagement", engagement.Id, details, now));

        // Announce the completed item, then commit.
        await events.PublishAsync(
            new AtlasEvent("engagement.onboarding_item_completed", orgId, "engagement", engagement.Id, details, now),
            cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);

        return engagement;
    }
}
