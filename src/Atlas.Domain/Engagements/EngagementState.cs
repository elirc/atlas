namespace Atlas.Domain.Engagements;

/// <summary>
/// The stage a hire is at, from a priced quote to an active worker. The lifecycle
/// is <c>quote → offer → contract → onboarding → active</c>, with <c>withdrawn</c>
/// as an early exit. This enum names the states; <see cref="EngagementTransitions"/>
/// owns which moves between them are legal.
/// </summary>
// LEARN: an enum is the states, NOT the state machine. The temptation is to add a
// `Status` column and scatter `if (status == Contract)` checks across use cases —
// which spreads one rule (what may follow what) across dozens of call sites that
// can each get it wrong independently. We keep the enum dumb and put the rules in
// one pure table. See docs/concepts/state-machines.md.
public enum EngagementState
{
    /// <summary>Created from a quote; no offer extended yet.</summary>
    Quote = 1,

    /// <summary>An offer has been sent to a named candidate.</summary>
    Offer = 2,

    /// <summary>The candidate accepted; onboarding has not started.</summary>
    Contract = 3,

    /// <summary>Onboarding is underway — at least one checklist item is done.</summary>
    Onboarding = 4,

    /// <summary>The worker is live; a Worker + initial EmploymentRecord exist.</summary>
    Active = 5,

    /// <summary>The hire was abandoned before it went live (from Offer or Contract).</summary>
    Withdrawn = 6,
}
