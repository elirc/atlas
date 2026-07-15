namespace Atlas.Domain.Payroll;

/// <summary>Where a payroll run is in its life. Persisted as text (see PayrollConfigurations).</summary>
public enum PayrollRunState
{
    /// <summary>Prepared and computed, still editable-by-recompute; nothing is committed to pay.</summary>
    Draft = 1,

    /// <summary>Submitted for a human to check the numbers before money is authorised.</summary>
    InReview = 2,

    /// <summary>A reviewer approved the figures; the run may now be executed.</summary>
    Approved = 3,

    /// <summary>Terminal. The run is recorded as run; disbursement follows (S09). No edits.</summary>
    Executed = 4,
}

/// <summary>A named move between two <see cref="PayrollRunState"/>s.</summary>
public enum PayrollTransition
{
    SubmitForReview,
    Approve,
    Reject,
    Execute,
}

/// <summary>
/// The payroll run's legal moves, as DATA. The guard methods on <see cref="PayrollRun"/>
/// consult this table; the exhaustive matrix test consults the same table — so the code
/// and its test can never disagree about what "legal" means.
/// </summary>
// 📘 This is the S05 lesson applied a second time: a state machine is a transition TABLE
// plus guard methods, not a status column sprinkled with `if (status == ...)` across the
// codebase. The table is the single source of truth for legality; illegal moves aren't
// forgotten branches, they're simply absent keys. (Compare with the alternative in
// docs/concepts/state-machines.md.)
public static class PayrollRunStateMachine
{
    // from-state × transition → to-state. Every LEGAL move is a key here; every move NOT
    // present is illegal by definition, which is why the illegal half of the matrix needs
    // no enumeration — it is the complement.
    private static readonly IReadOnlyDictionary<(PayrollRunState From, PayrollTransition Move), PayrollRunState> Table =
        new Dictionary<(PayrollRunState, PayrollTransition), PayrollRunState>
        {
            [(PayrollRunState.Draft, PayrollTransition.SubmitForReview)] = PayrollRunState.InReview,
            [(PayrollRunState.InReview, PayrollTransition.Approve)] = PayrollRunState.Approved,
            // WHY: Reject lands back in Draft, not a distinct "Rejected" state. A rejected
            // run is not a dead end — it goes back to be fixed and re-submitted. Modelling
            // rejection as a state would multiply terminal cases for a workflow that only
            // ever wants "try again". The audit log preserves that a rejection happened.
            [(PayrollRunState.InReview, PayrollTransition.Reject)] = PayrollRunState.Draft,
            [(PayrollRunState.Approved, PayrollTransition.Execute)] = PayrollRunState.Executed,
        };

    /// <summary>True if <paramref name="move"/> is legal from <paramref name="from"/>.</summary>
    public static bool TryNext(PayrollRunState from, PayrollTransition move, out PayrollRunState to) =>
        Table.TryGetValue((from, move), out to);

    /// <summary>The state after <paramref name="move"/>, or a DomainRuleException (422) if illegal.</summary>
    public static PayrollRunState Next(PayrollRunState from, PayrollTransition move) =>
        TryNext(from, move, out PayrollRunState to)
            ? to
            : throw new DomainRuleException(
                $"A payroll run in {from} cannot {move}.");

    /// <summary>Every legal (from, move, to) triple — the test enumerates against this.</summary>
    public static IReadOnlyCollection<(PayrollRunState From, PayrollTransition Move, PayrollRunState To)> LegalMoves =>
        [.. Table.Select(kv => (kv.Key.From, kv.Key.Move, kv.Value))];
}
