using Atlas.Domain.Access;
using Atlas.Domain.Countries;
using Atlas.Domain.Engagements;

namespace Atlas.Application.Tests.Fakes;

/// <summary>Builds engagements in a chosen state via the REAL domain transitions.</summary>
// WHY: tests seed state by driving the aggregate, not by reflecting private setters —
// so if a transition's rules change, these builders feel it too.
public static class TestEngagements
{
    public static Engagement New(Guid orgId, DateTimeOffset now, string country = "DEU") =>
        Engagement.FromQuote(
            orgId, Guid.NewGuid(), CountryCode.From(country),
            "Engineer", 60_000m, "EUR", 40m,
            OnboardingChecklistTemplate.For(CountryCode.From(country)), now);

    public static Engagement InState(
        EngagementState target, Guid orgId, DateTimeOffset now, string country = "DEU")
    {
        var e = New(orgId, now, country);
        if (target == EngagementState.Quote)
        {
            return e;
        }

        e.SendOffer("Maria Santos", EmailAddress.From("maria@example.com"), now);
        if (target == EngagementState.Offer)
        {
            return e;
        }

        if (target == EngagementState.Withdrawn)
        {
            e.Withdraw(now);
            return e;
        }

        e.AcceptOffer(new DateOnly(2026, 8, 1));
        if (target == EngagementState.Contract)
        {
            return e;
        }

        e.CompleteOnboardingItem(e.Checklist[0].Name, now);
        if (target == EngagementState.Onboarding)
        {
            return e;
        }

        foreach (var item in e.Checklist.Where(i => !i.IsComplete).ToList())
        {
            e.CompleteOnboardingItem(item.Name, now);
        }

        e.Activate(now);
        return e;
    }

    /// <summary>In Onboarding with EVERY item complete — one step from activation.</summary>
    public static Engagement FullyOnboarded(Guid orgId, DateTimeOffset now, string country = "DEU")
    {
        var e = New(orgId, now, country);
        e.SendOffer("Maria Santos", EmailAddress.From("maria@example.com"), now);
        e.AcceptOffer(new DateOnly(2026, 8, 1));
        foreach (var item in e.Checklist.ToList())
        {
            e.CompleteOnboardingItem(item.Name, now);
        }

        return e;
    }
}
