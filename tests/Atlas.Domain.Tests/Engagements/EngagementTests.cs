using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Countries;
using Atlas.Domain.Engagements;

namespace Atlas.Domain.Tests.Engagements;

public class EngagementTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);

    private static Engagement NewEngagement(string country = "DEU") =>
        Engagement.FromQuote(
            Guid.NewGuid(), Guid.NewGuid(), CountryCode.From(country),
            "Engineer", 60_000m, "EUR", 40m,
            OnboardingChecklistTemplate.For(CountryCode.From(country)), Now);

    /// <summary>Drive a fresh engagement to <paramref name="target"/> via the real transitions.</summary>
    private static Engagement InState(EngagementState target, string country = "DEU")
    {
        var e = NewEngagement(country);
        if (target == EngagementState.Quote)
        {
            return e;
        }

        e.SendOffer("Maria Santos", EmailAddress.From("maria@example.com"), Now);
        if (target == EngagementState.Offer)
        {
            return e;
        }

        if (target == EngagementState.Withdrawn)
        {
            e.Withdraw(Now);
            return e;
        }

        e.AcceptOffer(new DateOnly(2026, 8, 1));
        if (target == EngagementState.Contract)
        {
            return e;
        }

        e.CompleteOnboardingItem(e.Checklist[0].Name, Now);
        if (target == EngagementState.Onboarding)
        {
            return e;
        }

        foreach (var item in e.Checklist.Where(i => !i.IsComplete).ToList())
        {
            e.CompleteOnboardingItem(item.Name, Now);
        }

        e.Activate(Now);
        return e;
    }

    // ── creation ─────────────────────────────────────────────────────────────────

    [Fact]
    public void FromQuote_opens_in_Quote_state_with_the_country_checklist()
    {
        var e = NewEngagement("DEU");

        Assert.Equal(EngagementState.Quote, e.State);
        // Germany adds a works-council item on top of the three universal ones.
        Assert.Equal(4, e.Checklist.Count);
        Assert.Contains(e.Checklist, i => i.Name == "works-council-notification");
        Assert.All(e.Checklist, i => Assert.False(i.IsComplete));
    }

    [Theory]
    [InlineData(null, 60_000, 40)]     // no role title
    [InlineData("", 60_000, 40)]       // blank role title
    [InlineData("Engineer", 0, 40)]    // zero salary
    [InlineData("Engineer", 60_000, 0)] // zero hours
    [InlineData("Engineer", 60_000, 81)] // too many hours
    public void FromQuote_rejects_bad_terms(string? title, decimal salary, decimal hours)
    {
        Assert.Throws<DomainRuleException>(() => Engagement.FromQuote(
            Guid.NewGuid(), Guid.NewGuid(), CountryCode.From("USA"),
            title, salary, "USD", hours, OnboardingChecklistTemplate.UniversalItems, Now));
    }

    [Fact]
    public void FromQuote_requires_an_org_and_a_quote()
    {
        Assert.Throws<DomainRuleException>(() => Engagement.FromQuote(
            Guid.Empty, Guid.NewGuid(), CountryCode.From("USA"), "Eng", 1m, "USD", 40m,
            OnboardingChecklistTemplate.UniversalItems, Now));

        Assert.Throws<DomainRuleException>(() => Engagement.FromQuote(
            Guid.NewGuid(), Guid.Empty, CountryCode.From("USA"), "Eng", 1m, "USD", 40m,
            OnboardingChecklistTemplate.UniversalItems, Now));
    }

    // ── send offer ───────────────────────────────────────────────────────────────

    [Fact]
    public void SendOffer_moves_to_Offer_and_records_the_candidate()
    {
        var e = NewEngagement();

        e.SendOffer("  Maria Santos ", EmailAddress.From("Maria@Example.com"), Now);

        Assert.Equal(EngagementState.Offer, e.State);
        Assert.Equal("Maria Santos", e.CandidateName);
        Assert.Equal("maria@example.com", e.CandidateEmail?.Value);
        Assert.Equal(Now, e.OfferSentAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SendOffer_requires_a_candidate_name(string? name)
    {
        var e = NewEngagement();

        Assert.Throws<DomainRuleException>(() =>
            e.SendOffer(name, EmailAddress.From("x@example.com"), Now));
        Assert.Equal(EngagementState.Quote, e.State);
    }

    [Fact]
    public void SendOffer_is_illegal_once_past_Quote()
    {
        var e = InState(EngagementState.Offer);

        var ex = Assert.Throws<DomainRuleException>(() =>
            e.SendOffer("Someone", EmailAddress.From("s@example.com"), Now));
        Assert.Contains("Offer", ex.Message);
    }

    // ── accept offer ─────────────────────────────────────────────────────────────

    [Fact]
    public void AcceptOffer_moves_to_Contract_and_records_the_date()
    {
        var e = InState(EngagementState.Offer);

        e.AcceptOffer(new DateOnly(2026, 8, 1));

        Assert.Equal(EngagementState.Contract, e.State);
        Assert.Equal(new DateOnly(2026, 8, 1), e.AcceptedOn);
    }

    [Fact]
    public void AcceptOffer_is_illegal_before_an_offer_exists()
    {
        var e = NewEngagement();

        Assert.Throws<DomainRuleException>(() => e.AcceptOffer(new DateOnly(2026, 8, 1)));
    }

    // ── onboarding checklist ─────────────────────────────────────────────────────

    [Fact]
    public void Completing_the_first_item_starts_onboarding()
    {
        var e = InState(EngagementState.Contract);

        bool changed = e.CompleteOnboardingItem(e.Checklist[0].Name, Now);

        Assert.True(changed);
        Assert.Equal(EngagementState.Onboarding, e.State);
        Assert.Equal(Now, e.OnboardingStartedAtUtc);
        Assert.True(e.Checklist[0].IsComplete);
    }

    [Fact]
    public void Completing_an_already_done_item_is_an_idempotent_no_op()
    {
        var e = InState(EngagementState.Contract);
        string first = e.Checklist[0].Name;
        e.CompleteOnboardingItem(first, Now);

        var later = Now.AddHours(1);
        bool changed = e.CompleteOnboardingItem(first, later);

        Assert.False(changed);
        // The timestamp does not move — the retry left no trace.
        Assert.Equal(Now, e.Checklist[0].CompletedAtUtc);
    }

    [Fact]
    public void Completing_an_unknown_item_is_rejected()
    {
        var e = InState(EngagementState.Contract);

        Assert.Throws<DomainRuleException>(() =>
            e.CompleteOnboardingItem("not-a-real-item", Now));
    }

    [Theory]
    [InlineData(EngagementState.Quote)]
    [InlineData(EngagementState.Offer)]
    public void Onboarding_items_cannot_be_completed_before_a_contract(EngagementState state)
    {
        var e = InState(state);

        Assert.Throws<DomainRuleException>(() =>
            e.CompleteOnboardingItem(e.Checklist[0].Name, Now));
    }

    // ── activation ───────────────────────────────────────────────────────────────

    [Fact]
    public void Activate_requires_every_item_complete()
    {
        var e = InState(EngagementState.Onboarding); // first item done, rest pending

        var ex = Assert.Throws<DomainRuleException>(() => e.Activate(Now));
        Assert.Contains("Onboarding is not complete", ex.Message);
        Assert.Equal(EngagementState.Onboarding, e.State);
    }

    [Fact]
    public void Activate_from_full_onboarding_returns_the_hire_data()
    {
        var e = NewEngagement("DEU");
        e.SendOffer("Maria Santos", EmailAddress.From("maria@example.com"), Now);
        e.AcceptOffer(new DateOnly(2026, 8, 1));
        foreach (var item in e.Checklist.ToList())
        {
            e.CompleteOnboardingItem(item.Name, Now);
        }

        EngagementActivation activation = e.Activate(Now);

        Assert.Equal(EngagementState.Active, e.State);
        Assert.Equal(Now, e.ActivatedAtUtc);
        Assert.Equal("Maria Santos", activation.CandidateName);
        Assert.Equal("EUR", activation.SalaryCurrency);
        Assert.Equal(60_000m, activation.AnnualSalary);
        Assert.Equal("DEU", activation.Country.Value);
    }

    [Fact]
    public void Activate_is_illegal_straight_from_Contract()
    {
        var e = InState(EngagementState.Contract);

        Assert.Throws<DomainRuleException>(() => e.Activate(Now));
    }

    [Fact]
    public void RecordCreatedWorker_links_once_and_only_once()
    {
        var e = InState(EngagementState.Active);
        var workerId = Guid.NewGuid();

        e.RecordCreatedWorker(workerId);
        Assert.Equal(workerId, e.CreatedWorkerId);

        Assert.Throws<DomainRuleException>(() => e.RecordCreatedWorker(Guid.NewGuid()));
    }

    [Fact]
    public void RecordCreatedWorker_before_activation_is_rejected()
    {
        var e = InState(EngagementState.Contract);

        Assert.Throws<DomainRuleException>(() => e.RecordCreatedWorker(Guid.NewGuid()));
    }

    // ── withdrawal ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(EngagementState.Offer)]
    [InlineData(EngagementState.Contract)]
    public void Withdraw_is_legal_from_offer_and_contract(EngagementState state)
    {
        var e = InState(state);

        e.Withdraw(Now);

        Assert.Equal(EngagementState.Withdrawn, e.State);
        Assert.Equal(Now, e.WithdrawnAtUtc);
    }

    [Theory]
    [InlineData(EngagementState.Quote)]
    [InlineData(EngagementState.Onboarding)]
    [InlineData(EngagementState.Active)]
    public void Withdraw_is_illegal_elsewhere(EngagementState state)
    {
        var e = InState(state);

        Assert.Throws<DomainRuleException>(() => e.Withdraw(Now));
    }
}
