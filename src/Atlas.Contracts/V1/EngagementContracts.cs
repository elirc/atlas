namespace Atlas.Contracts.V1;

/// <summary>
/// Body of POST /api/v1/engagements — start a hire from an existing quote.
/// </summary>
// WHY: country and pay are NOT here — they come from the quote, so a client can't
// contradict the estimate the hire is priced on. The role title and weekly hours
// are the hire's own facts and are supplied here.
public sealed record CreateEngagementRequest(Guid QuoteId, string? RoleTitle, decimal WeeklyHours);

/// <summary>Body of POST /api/v1/engagements/{id}/send-offer.</summary>
public sealed record SendOfferRequest(string? CandidateName, string? CandidateEmail);

/// <summary>Body of POST /api/v1/engagements/{id}/accept-offer — a calendar date.</summary>
public sealed record AcceptOfferRequest(DateOnly AcceptedOn);

/// <summary>Body of POST /api/v1/engagements/{id}/withdraw — an optional reason for the timeline.</summary>
public sealed record WithdrawEngagementRequest(string? Reason);

/// <summary>One onboarding checklist item, as served.</summary>
public sealed record ChecklistItemDto(string Name, bool Completed, DateTimeOffset? CompletedAtUtc);

/// <summary>
/// An engagement's state and timeline. Each nullable timestamp is a milestone; read
/// together they ARE the history GET /{id} surfaces.
/// </summary>
public sealed record EngagementDto(
    Guid Id,
    Guid QuoteId,
    string State,
    string? CandidateName,
    string? CandidateEmail,
    string Country,
    string RoleTitle,
    decimal AnnualSalary,
    string SalaryCurrency,
    decimal WeeklyHours,
    Guid? CreatedWorkerId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? OfferSentAtUtc,
    DateOnly? AcceptedOn,
    DateTimeOffset? OnboardingStartedAtUtc,
    DateTimeOffset? ActivatedAtUtc,
    DateTimeOffset? WithdrawnAtUtc);

/// <summary>GET /api/v1/engagements/{id} — the engagement plus its onboarding checklist.</summary>
public sealed record EngagementDetailDto(EngagementDto Engagement, IReadOnlyList<ChecklistItemDto> Checklist);
