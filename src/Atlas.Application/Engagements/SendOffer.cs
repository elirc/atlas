using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Events;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Engagements;

namespace Atlas.Application.Engagements;

/// <summary>Quote → Offer: extend the offer to a named candidate.</summary>
public sealed class SendOffer(
    IEngagementRepository engagements,
    IAuditTrail audit,
    IEventSink events,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<Engagement> ExecuteAsync(
        Guid engagementId,
        string? candidateName,
        string? candidateEmail,
        CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        Engagement engagement =
            await engagements.FindInOrgAsync(engagementId, orgId, cancellationToken)
            ?? throw new NotFoundException("engagement", engagementId);

        // WHY: the email is normalized/validated at the boundary (value object), so
        // the aggregate receives a shape it can trust. A malformed address is a 422
        // from the domain, not a persistence surprise.
        var email = EmailAddress.From(candidateEmail);

        DateTimeOffset now = clock.GetUtcNow();
        engagement.SendOffer(candidateName, email, now);

        string details = JsonSerializer.Serialize(new
        {
            engagement.CandidateName,
            CandidateEmail = email.Value,
        });

        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "engagement.offer_sent", "engagement", engagement.Id, details, now));

        // The transition is done; announce it, then commit.
        await events.PublishAsync(
            new AtlasEvent("engagement.offer_sent", orgId, "engagement", engagement.Id, details, now),
            cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);

        return engagement;
    }
}
