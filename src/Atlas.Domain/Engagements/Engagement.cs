using Atlas.Domain.Access;
using Atlas.Domain.Countries;

namespace Atlas.Domain.Engagements;

/// <summary>
/// A hire in flight: the aggregate that walks a candidate from a priced quote to
/// an active worker. State changes are METHODS with guards
/// (<see cref="SendOffer"/>, <see cref="AcceptOffer"/>, …), never setters — the
/// only way to reach <see cref="EngagementState.Active"/> is to pass every gate
/// along the way. See <see cref="EngagementTransitions"/> and ADR-0006.
/// </summary>
public sealed class Engagement
{
    public const int MaxCandidateNameLength = 200;
    public const int MaxRoleTitleLength = 120;

    private readonly List<EngagementChecklistItem> _checklist = [];

    public Guid Id { get; }
    public Guid OrgId { get; }

    /// <summary>The S4 quote this hire was priced from — the link back to the estimate.</summary>
    public Guid QuoteId { get; }

    public EngagementState State { get; private set; }

    // Candidate identity is captured when the offer is sent, not at creation — you
    // create an engagement against a req/quote, then extend the offer to a person.
    public string? CandidateName { get; private set; }
    public EmailAddress? CandidateEmail { get; private set; }

    // WHY: the compensation is SNAPSHOT from the quote at creation (see
    // Engagement.FromQuote). Freezing it means a later re-run of the quote can't
    // silently change the terms this hire was made on — and activation can build
    // the EmploymentRecord without re-reading the quote module at all.
    public CountryCode Country { get; }
    public string RoleTitle { get; }
    public decimal AnnualSalary { get; }
    public string SalaryCurrency { get; }
    public decimal WeeklyHours { get; }

    /// <summary>Set once, at activation, to the Worker this engagement created.</summary>
    // WHY: a Guid?, not a Worker. The aggregate stays decoupled from the Workforce
    // aggregate — Activate RETURNS the data to hire, the use case creates the
    // Worker via the S3 flow, then records the id here. Two aggregates, one commit.
    public Guid? CreatedWorkerId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    // The timeline: one nullable timestamp per milestone. Read together they are the
    // engagement's memory — GET /{id} surfaces them as the state history.
    public DateTimeOffset? OfferSentAtUtc { get; private set; }
    public DateOnly? AcceptedOn { get; private set; }
    public DateTimeOffset? OnboardingStartedAtUtc { get; private set; }
    public DateTimeOffset? ActivatedAtUtc { get; private set; }
    public DateTimeOffset? WithdrawnAtUtc { get; private set; }

    /// <summary>The onboarding checklist (owned; loaded with the aggregate).</summary>
    public IReadOnlyList<EngagementChecklistItem> Checklist => _checklist;

    private Engagement(
        Guid id,
        Guid orgId,
        Guid quoteId,
        CountryCode country,
        string roleTitle,
        decimal annualSalary,
        string salaryCurrency,
        decimal weeklyHours,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrgId = orgId;
        QuoteId = quoteId;
        Country = country;
        RoleTitle = roleTitle;
        AnnualSalary = annualSalary;
        SalaryCurrency = salaryCurrency;
        WeeklyHours = weeklyHours;
        State = EngagementState.Quote;
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>
    /// Start a hire from a quote: freezes the country + compensation, seeds the
    /// country-specific onboarding checklist, and opens in <see cref="EngagementState.Quote"/>.
    /// </summary>
    public static Engagement FromQuote(
        Guid orgId,
        Guid quoteId,
        CountryCode country,
        string? roleTitle,
        decimal annualSalary,
        string salaryCurrency,
        decimal weeklyHours,
        IReadOnlyList<string> checklistItems,
        DateTimeOffset createdAtUtc)
    {
        if (orgId == Guid.Empty)
        {
            throw new DomainRuleException("An engagement must belong to an org.");
        }

        if (quoteId == Guid.Empty)
        {
            throw new DomainRuleException("An engagement must reference a quote.");
        }

        string title = (roleTitle ?? string.Empty).Trim();
        if (title.Length is 0 or > MaxRoleTitleLength)
        {
            throw new DomainRuleException(
                $"Role title is required (max {MaxRoleTitleLength} characters).");
        }

        // GOTCHA: the compensation is snapshot from a quote, but the aggregate still
        // guards it — a use case is not a trusted caller. These are the same rules
        // EmploymentRecord.Create enforces at activation; failing fast here turns a
        // bad quote link into a 422 at create time, not a surprise five states later.
        if (annualSalary <= 0)
        {
            throw new DomainRuleException("Annual salary must be positive.");
        }

        if (weeklyHours is <= 0 or > 80)
        {
            throw new DomainRuleException("Weekly hours must be between 1 and 80.");
        }

        var engagement = new Engagement(
            Guid.NewGuid(), orgId, quoteId, country, title, annualSalary,
            salaryCurrency, weeklyHours, createdAtUtc);

        foreach (string item in checklistItems)
        {
            engagement._checklist.Add(EngagementChecklistItem.Pending(item));
        }

        return engagement;
    }

    /// <summary>Quote → Offer. Records the candidate the offer is addressed to.</summary>
    public void SendOffer(string? candidateName, EmailAddress candidateEmail, DateTimeOffset now)
    {
        RequireTransitionTo(EngagementState.Offer);

        string name = (candidateName ?? string.Empty).Trim();
        if (name.Length is 0 or > MaxCandidateNameLength)
        {
            throw new DomainRuleException(
                $"A candidate name is required to send an offer (max {MaxCandidateNameLength} characters).");
        }

        CandidateName = name;
        CandidateEmail = candidateEmail;
        OfferSentAtUtc = now;
        State = EngagementState.Offer;
    }

    /// <summary>Offer → Contract. Records the calendar date the candidate accepted.</summary>
    // WHY the DateOnly: "accepted on the 3rd" is a calendar fact, not an instant. The
    // system-time of the API call is the use case's `now` (it stamps the audit row);
    // the business-time the candidate committed is this DateOnly. Two clocks (S3 lesson).
    public void AcceptOffer(DateOnly acceptedOn)
    {
        RequireTransitionTo(EngagementState.Contract);

        AcceptedOn = acceptedOn;
        State = EngagementState.Contract;
    }

    /// <summary>
    /// Mark one onboarding item done. Legal in Contract or Onboarding; the FIRST
    /// completion advances Contract → Onboarding (onboarding has begun). Returns
    /// <c>true</c> only when something changed — a retry of an already-done item is
    /// an idempotent no-op, so the use case writes no second activity row.
    /// </summary>
    public bool CompleteOnboardingItem(string itemName, DateTimeOffset now)
    {
        if (State is not (EngagementState.Contract or EngagementState.Onboarding))
        {
            throw new DomainRuleException(
                $"Onboarding items can only be completed while the engagement is in Contract " +
                $"or Onboarding (current state: {State}).");
        }

        EngagementChecklistItem item =
            _checklist.FirstOrDefault(i => string.Equals(i.Name, itemName, StringComparison.OrdinalIgnoreCase))
            ?? throw new DomainRuleException($"'{itemName}' is not an onboarding item for this engagement.");

        bool changed = item.MarkComplete(now);
        if (!changed)
        {
            return false;
        }

        // WHY: the first completed task is what "onboarding has started" MEANS —
        // and crossing that line is exactly when withdrawal stops being allowed
        // (see the transition table: Withdrawn is reachable from Contract, not
        // Onboarding). The state models a real boundary, not bookkeeping.
        if (State == EngagementState.Contract)
        {
            RequireTransitionTo(EngagementState.Onboarding);
            State = EngagementState.Onboarding;
            OnboardingStartedAtUtc = now;
        }

        return true;
    }

    /// <summary>
    /// Onboarding → Active. Requires every checklist item complete. Returns the data
    /// the use case needs to hire the Worker; the use case then calls
    /// <see cref="RecordCreatedWorker"/> to close the loop — all in one commit.
    /// </summary>
    public EngagementActivation Activate(DateTimeOffset now)
    {
        RequireTransitionTo(EngagementState.Active);

        int done = _checklist.Count(i => i.IsComplete);
        if (done < _checklist.Count)
        {
            throw new DomainRuleException(
                $"Onboarding is not complete ({done} of {_checklist.Count} items done); " +
                "the engagement cannot be activated.");
        }

        // Reachable only via SendOffer (sets candidate) → AcceptOffer → onboarding,
        // so both are set; the guard makes that a checked fact, not a comment.
        if (CandidateName is null || CandidateEmail is null)
        {
            throw new DomainRuleException("Candidate details are required before activation.");
        }

        State = EngagementState.Active;
        ActivatedAtUtc = now;

        return new EngagementActivation(
            CandidateName, CandidateEmail.Value, Country, RoleTitle, AnnualSalary, SalaryCurrency, WeeklyHours);
    }

    /// <summary>Link the Worker created at activation. Called once, after Activate.</summary>
    public void RecordCreatedWorker(Guid workerId)
    {
        if (State != EngagementState.Active)
        {
            throw new DomainRuleException("Only an active engagement has a worker to record.");
        }

        if (CreatedWorkerId is not null)
        {
            throw new DomainRuleException("This engagement already created a worker.");
        }

        if (workerId == Guid.Empty)
        {
            throw new DomainRuleException("A created worker id is required.");
        }

        CreatedWorkerId = workerId;
    }

    /// <summary>Offer/Contract → Withdrawn. The early exit; illegal once onboarding starts.</summary>
    public void Withdraw(DateTimeOffset now)
    {
        RequireTransitionTo(EngagementState.Withdrawn);

        WithdrawnAtUtc = now;
        State = EngagementState.Withdrawn;
    }

    // WHY: one guard, every transition. Each method asks the pure table whether its
    // move is legal from the CURRENT state and throws a precise 422 if not — so
    // "activate a withdrawn hire" fails the same way everywhere, with a message that
    // names both states instead of a bare "409 conflict".
    private void RequireTransitionTo(EngagementState target)
    {
        if (!EngagementTransitions.IsLegal(State, target))
        {
            throw new DomainRuleException(
                $"An engagement in state '{State}' cannot transition to '{target}'.");
        }
    }

    // WHY: EF Core materialization only.
    private Engagement()
    {
        RoleTitle = null!;
        SalaryCurrency = null!;
    }
}

/// <summary>
/// The data <see cref="Engagement.Activate"/> hands back so a use case can hire the
/// Worker without the aggregate ever referencing the Workforce module.
/// </summary>
public sealed record EngagementActivation(
    string CandidateName,
    EmailAddress CandidateEmail,
    CountryCode Country,
    string RoleTitle,
    decimal AnnualSalary,
    string SalaryCurrency,
    decimal WeeklyHours);
