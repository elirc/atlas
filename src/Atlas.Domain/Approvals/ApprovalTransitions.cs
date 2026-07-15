namespace Atlas.Domain.Approvals;

/// <summary>
/// The approval lifecycle expressed as DATA: the set of legal <c>(from → to)</c> moves and one
/// pure predicate over it. No timestamps, no side effects, no persistence — just "may a thing in
/// state X become Y?". The compile-time twin of the S05 <c>EngagementTransitions</c> pattern
/// (ADR-0006 predicted this: "the S8 approval flow follows this shape — table + guarded methods").
/// </summary>
// 🔗 The PR debate is about THIS being a table. The Reviewer wanted a *runtime* workflow table —
// rows in a database an admin edits. This is a table too, but a CODE one: the moves are fixed at
// compile time, the matrix test asserts all of them, and there is no engine to interpret rows.
// The buy/build line (see ApprovalWorkflow's tripwire) is exactly the jump from this static set to
// that runtime engine — and we do not cross it until a customer needs designer-editable approvals.
public static class ApprovalTransitions
{
    /// <summary>Every state, for exhaustive iteration (the matrix test walks these).</summary>
    public static readonly IReadOnlyList<ApprovalState> AllStates = Enum.GetValues<ApprovalState>();

    // WHY: the ONE place the shape of the approval lifecycle is written down. Draft submits;
    // a submitted item is approved or denied; a submitted OR approved item can be cancelled;
    // denied and cancelled are terminal. Adding a move is a one-line edit here plus the matrix
    // test turning red until it agrees — the diff a reviewer wants.
    private static readonly HashSet<(ApprovalState From, ApprovalState To)> Legal =
    [
        (ApprovalState.Draft, ApprovalState.Submitted),
        (ApprovalState.Submitted, ApprovalState.Approved),
        (ApprovalState.Submitted, ApprovalState.Denied),
        (ApprovalState.Submitted, ApprovalState.Cancelled),
        (ApprovalState.Approved, ApprovalState.Cancelled),
    ];

    /// <summary>May a thing in <paramref name="from"/> move to <paramref name="to"/>?</summary>
    public static bool IsLegal(ApprovalState from, ApprovalState to) => Legal.Contains((from, to));

    /// <summary>The full legal set — exposed so tests assert against data, not a mirror of it.</summary>
    // GOTCHA: the matrix test must NOT rebuild the legal set by copying these pairs (that tests
    // the copy, not the machine). It rebuilds the EXPECTATION from the lifecycle narrative and
    // checks IsLegal against it — see ApprovalWorkflowTests.
    public static IReadOnlyCollection<(ApprovalState From, ApprovalState To)> LegalTransitions => Legal;

    /// <summary>Legal next states from <paramref name="from"/> (empty for terminal states).</summary>
    public static IReadOnlyList<ApprovalState> LegalTargetsFrom(ApprovalState from) =>
        [.. AllStates.Where(to => IsLegal(from, to))];
}
