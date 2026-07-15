using Atlas.Domain.Engagements;

namespace Atlas.Domain.Tests.Engagements;

/// <summary>
/// The exhaustive transition matrix. Every one of the 6×6 = 36 ordered state pairs
/// is asserted legal or illegal — because the ILLEGAL half is the valuable half:
/// "activate a withdrawn hire", "skip straight from quote to active" are exactly the
/// moves a status-column-and-ifs design forgets to forbid, and forgets to test.
/// </summary>
public class EngagementTransitionsTests
{
    // The lifecycle, written as a specification independent of the table under test.
    // quote → offer → contract → onboarding → active, with withdraw from offer/contract.
    private static bool LifecycleAllows(EngagementState from, EngagementState to) => (from, to) switch
    {
        (EngagementState.Quote, EngagementState.Offer) => true,
        (EngagementState.Offer, EngagementState.Contract) => true,
        (EngagementState.Contract, EngagementState.Onboarding) => true,
        (EngagementState.Onboarding, EngagementState.Active) => true,
        (EngagementState.Offer, EngagementState.Withdrawn) => true,
        (EngagementState.Contract, EngagementState.Withdrawn) => true,
        _ => false,
    };

    public static IEnumerable<object[]> AllOrderedPairs() =>
        from f in EngagementTransitions.AllStates
        from t in EngagementTransitions.AllStates
        select new object[] { f, t };

    [Theory]
    [MemberData(nameof(AllOrderedPairs))]
    public void Every_pair_is_legal_exactly_when_the_lifecycle_allows_it(
        EngagementState from, EngagementState to)
    {
        Assert.Equal(LifecycleAllows(from, to), EngagementTransitions.IsLegal(from, to));
    }

    [Fact]
    public void There_are_exactly_six_legal_transitions()
    {
        // If someone adds or removes a legal move, this count changes and the diff
        // shows up front — a state machine's shape is a reviewable number.
        Assert.Equal(6, EngagementTransitions.LegalTransitions.Count);
    }

    // ── the legal half ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(EngagementState.Quote, EngagementState.Offer)]
    [InlineData(EngagementState.Offer, EngagementState.Contract)]
    [InlineData(EngagementState.Contract, EngagementState.Onboarding)]
    [InlineData(EngagementState.Onboarding, EngagementState.Active)]
    [InlineData(EngagementState.Offer, EngagementState.Withdrawn)]
    [InlineData(EngagementState.Contract, EngagementState.Withdrawn)]
    public void Legal_moves_are_legal(EngagementState from, EngagementState to)
    {
        Assert.True(EngagementTransitions.IsLegal(from, to));
    }

    // ── the illegal half (the valuable half) ────────────────────────────────────

    [Theory]
    [InlineData(EngagementState.Quote, EngagementState.Contract)]   // skip the offer
    [InlineData(EngagementState.Quote, EngagementState.Active)]     // skip everything
    [InlineData(EngagementState.Quote, EngagementState.Withdrawn)]  // nothing to withdraw yet
    [InlineData(EngagementState.Offer, EngagementState.Onboarding)] // skip acceptance
    [InlineData(EngagementState.Offer, EngagementState.Active)]     // skip contract+onboarding
    [InlineData(EngagementState.Contract, EngagementState.Active)]  // skip onboarding
    [InlineData(EngagementState.Onboarding, EngagementState.Withdrawn)] // too late to withdraw
    [InlineData(EngagementState.Contract, EngagementState.Offer)]   // no going back
    [InlineData(EngagementState.Onboarding, EngagementState.Contract)] // no going back
    public void Illegal_moves_are_illegal(EngagementState from, EngagementState to)
    {
        Assert.False(EngagementTransitions.IsLegal(from, to));
    }

    [Theory]
    [InlineData(EngagementState.Quote)]
    [InlineData(EngagementState.Offer)]
    [InlineData(EngagementState.Contract)]
    [InlineData(EngagementState.Onboarding)]
    [InlineData(EngagementState.Active)]
    [InlineData(EngagementState.Withdrawn)]
    public void No_state_transitions_to_itself(EngagementState state)
    {
        Assert.False(EngagementTransitions.IsLegal(state, state));
    }

    [Theory]
    [InlineData(EngagementState.Active)]
    [InlineData(EngagementState.Withdrawn)]
    public void Terminal_states_have_no_legal_targets(EngagementState terminal)
    {
        Assert.Empty(EngagementTransitions.LegalTargetsFrom(terminal));
    }
}
