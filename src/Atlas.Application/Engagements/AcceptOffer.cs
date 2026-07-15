using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Events;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Engagements;

namespace Atlas.Application.Engagements;

/// <summary>Offer → Contract: the candidate accepted, on a given calendar date.</summary>
public sealed class AcceptOffer(
    IEngagementRepository engagements,
    IAuditTrail audit,
    IEventSink events,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<Engagement> ExecuteAsync(
        Guid engagementId,
        DateOnly acceptedOn,
        CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        Engagement engagement =
            await engagements.FindInOrgAsync(engagementId, orgId, cancellationToken)
            ?? throw new NotFoundException("engagement", engagementId);

        DateTimeOffset now = clock.GetUtcNow();
        engagement.AcceptOffer(acceptedOn);

        string details = JsonSerializer.Serialize(new { AcceptedOn = acceptedOn });

        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "engagement.offer_accepted", "engagement", engagement.Id, details, now));

        // Announce the acceptance, then commit.
        await events.PublishAsync(
            new AtlasEvent("engagement.offer_accepted", orgId, "engagement", engagement.Id, details, now),
            cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);

        return engagement;
    }
}
