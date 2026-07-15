namespace Atlas.Domain.Engagements;

/// <summary>
/// The hiring state machine, expressed as DATA: the set of legal
/// <c>(from → to)</c> moves, and one pure predicate over it. No timestamps, no
/// side effects, no persistence — just "may an engagement in state X become Y?".
/// </summary>
// LEARN: transitions-as-data is the whole idea of this sprint. The alternative —
// a `Status` column plus `if`s sprinkled through the use cases — encodes the same
// rules, but as behaviour spread across files, where the illegal moves are the
// ones NOBODY writes down (and therefore nobody tests). Here the illegal moves are
// simply every pair NOT in this set, and the matrix test asserts all 36 of them.
// WHY it lives in the Domain and is pure: the transition rules are business truth
// ("you can't activate a hire nobody accepted"), independent of HTTP, EF, or the
// aggregate's other state. Purity is also what makes the exhaustive test cheap.
public static class EngagementTransitions
{
    /// <summary>Every state, for exhaustive iteration (the matrix test walks these).</summary>
    public static readonly IReadOnlyList<EngagementState> AllStates =
        Enum.GetValues<EngagementState>();

    // WHY: the ONE place the shape of the lifecycle is written down. Adding
    // `withdrawn` reachable from onboarding, say, is a one-line edit here plus the
    // matrix test turning red until it agrees — the diff a reviewer wants.
    private static readonly HashSet<(EngagementState From, EngagementState To)> Legal =
    [
        (EngagementState.Quote, EngagementState.Offer),
        (EngagementState.Offer, EngagementState.Contract),
        (EngagementState.Offer, EngagementState.Withdrawn),
        (EngagementState.Contract, EngagementState.Onboarding),
        (EngagementState.Contract, EngagementState.Withdrawn),
        (EngagementState.Onboarding, EngagementState.Active),
    ];

    /// <summary>May an engagement in <paramref name="from"/> move to <paramref name="to"/>?</summary>
    public static bool IsLegal(EngagementState from, EngagementState to) =>
        Legal.Contains((from, to));

    /// <summary>The full legal set — exposed so tests assert against data, not a mirror of it.</summary>
    // GOTCHA: the matrix test must NOT rebuild the legal set by copying these pairs
    // (that tests the copy, not the machine). It rebuilds the EXPECTATION from the
    // lifecycle narrative and checks IsLegal against it — see EngagementTransitionsTests.
    public static IReadOnlyCollection<(EngagementState From, EngagementState To)> LegalTransitions =>
        Legal;

    /// <summary>Legal next states from <paramref name="from"/> (empty for terminal states).</summary>
    public static IReadOnlyList<EngagementState> LegalTargetsFrom(EngagementState from) =>
        [.. AllStates.Where(to => IsLegal(from, to))];
}
