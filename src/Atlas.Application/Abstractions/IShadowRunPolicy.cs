namespace Atlas.Application.Abstractions;

/// <summary>
/// The GATE on shadow running: is this org allowed to run a second, duplicate payroll
/// calculation right now? A port so the gate is configuration (an allowlist with a time-box),
/// not a hard-coded policy — and so the use case can be tested with a stub that says yes or no.
/// </summary>
// 📘 THIS PORT IS THE REVIEWER'S FIRST S12 WIN, MADE ENFORCEABLE (ADR-0014, the course's third
// Reviewer win). Shadow mode doubles payroll compute and would, unchecked, run for every org on
// every prepare forever. The Reviewer's point stood: gate it. So a shadow run is possible only
// when an org is explicitly ENROLLED and the enrolment has not EXPIRED — a deliberate, revocable,
// time-boxed opt-in, not an always-on cost. The second half of the gate ("is there actually a
// pending switch to compare against?") lives in the use case, computed from the effective-dated
// assignment log, so shadowing AUTO-EXPIRES the day the flip becomes effective — there is no
// pending vendor left to trial. Enrolment bounds WHO and UNTIL-WHEN; the pending switch bounds
// WHETHER-THERE-IS-ANYTHING-TO-DO. Both must be true, so an enrolled org whose flip already
// happened stops shadowing on its own, and an un-enrolled org never starts.
public interface IShadowRunPolicy
{
    /// <summary>
    /// True if <paramref name="orgId"/> is enrolled in the shadow-run programme and its
    /// enrolment is still open on <paramref name="asOf"/>. False for everyone else — including
    /// enrolled orgs whose time-box has closed.
    /// </summary>
    bool IsEnrolled(Guid orgId, DateOnly asOf);

    /// <summary>The divergence tolerance (in minor units) beyond which a worker's net delta is flagged.</summary>
    // WHY on the policy and not a magic number in the use case: the acceptable rounding-dialect
    // noise between two honest vendors is an operational decision (finance owns "how many cents
    // of pure-rounding drift do we ignore?"), and it belongs beside the enrolment it governs.
    int DivergenceToleranceMinorUnits { get; }
}
