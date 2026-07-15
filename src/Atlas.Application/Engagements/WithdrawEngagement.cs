using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Events;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Engagements;

namespace Atlas.Application.Engagements;

/// <summary>Offer/Contract → Withdrawn: abandon the hire before it goes live.</summary>
public sealed class WithdrawEngagement(
    IEngagementRepository engagements,
    IAuditTrail audit,
    IEventSink events,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<Engagement> ExecuteAsync(
        Guid engagementId,
        string? reason,
        CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        Engagement engagement =
            await engagements.FindInOrgAsync(engagementId, orgId, cancellationToken)
            ?? throw new NotFoundException("engagement", engagementId);

        DateTimeOffset now = clock.GetUtcNow();

        // WHY: the guard is in the aggregate — Withdrawn is reachable only from Offer
        // and Contract (the transition table), so "withdraw an active hire" is a 422
        // with a message that names both states, not a silent no-op.
        engagement.Withdraw(now);

        string details = JsonSerializer.Serialize(new { Reason = reason });

        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "engagement.withdrawn", "engagement", engagement.Id, details, now));

        // Announce the withdrawal, then commit.
        await events.PublishAsync(
            new AtlasEvent("engagement.withdrawn", orgId, "engagement", engagement.Id, details, now),
            cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);

        return engagement;
    }
}
