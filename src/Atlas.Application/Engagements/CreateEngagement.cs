using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Events;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Engagements;

namespace Atlas.Application.Engagements;

/// <summary>
/// Open a hire from an existing, org-scoped quote. Snapshots the quote's country
/// and gross pay onto the engagement, seeds the country's onboarding checklist,
/// and opens in the <see cref="EngagementState.Quote"/> state.
/// </summary>
public sealed class CreateEngagement(
    IEngagementRepository engagements,
    IQuoteReader quotes,
    IAuditTrail audit,
    IEventSink events,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<Engagement> ExecuteAsync(
        Guid quoteId,
        string? roleTitle,
        decimal weeklyHours,
        CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        // WHY: FindInOrg, not Find — a quote from another tenant must be
        // indistinguishable from one that doesn't exist (404, never 403). The org
        // filter comes from context, so you cannot start a hire off someone else's
        // estimate by guessing its id.
        QuoteSnapshot? quote = await quotes.FindInOrgAsync(quoteId, orgId, cancellationToken)
            ?? throw new NotFoundException("quote", quoteId);

        DateTimeOffset now = clock.GetUtcNow();

        var engagement = Engagement.FromQuote(
            orgId,
            quoteId,
            quote.Country,
            roleTitle,
            quote.GrossAnnualSalary,
            quote.Currency,
            weeklyHours,
            OnboardingChecklistTemplate.For(quote.Country),
            now);

        string details = JsonSerializer.Serialize(new
        {
            engagement.QuoteId,
            Country = quote.Country.Value,
            engagement.RoleTitle,
            engagement.AnnualSalary,
            engagement.SalaryCurrency,
            ChecklistItems = engagement.Checklist.Count,
        });

        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "engagement.created", "engagement", engagement.Id, details, now));

        await engagements.AddAsync(engagement, cancellationToken);

        // Announce the new engagement through the event sink, then commit the writes.
        await events.PublishAsync(
            new AtlasEvent("engagement.created", orgId, "engagement", engagement.Id, details, now),
            cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);

        return engagement;
    }
}
