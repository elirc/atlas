using Atlas.Contracts.V1;
using Atlas.Domain.Engagements;

namespace Atlas.Api.Mapping;

// WHY: engagement projections live in their own mapping file rather than swelling
// DtoMapping — same layer, same hand-written style (ADR-0001), just kept local to
// the sprint that added them. Extension methods compose across both classes.
internal static class EngagementMapping
{
    public static ChecklistItemDto ToDto(this EngagementChecklistItem item) =>
        new(item.Name, item.IsComplete, item.CompletedAtUtc);

    public static EngagementDto ToDto(this Engagement e) =>
        new(
            e.Id,
            e.QuoteId,
            e.State.ToString(),
            e.CandidateName,
            e.CandidateEmail is { } email ? email.Value : null,
            e.Country.Value,
            e.RoleTitle,
            e.AnnualSalary,
            e.SalaryCurrency,
            e.WeeklyHours,
            e.CreatedWorkerId,
            e.CreatedAtUtc,
            e.OfferSentAtUtc,
            e.AcceptedOn,
            e.OnboardingStartedAtUtc,
            e.ActivatedAtUtc,
            e.WithdrawnAtUtc);

    public static EngagementDetailDto ToDetailDto(this Engagement e) =>
        new(e.ToDto(), [.. e.Checklist.Select(item => item.ToDto())]);
}
