using Atlas.Application.Abstractions;

namespace Atlas.Infrastructure.Shadow;

/// <summary>
/// The configured shadow-run gate: an org may shadow iff it is in the allowlist AND the asked-about
/// day is on or before its enrolment expiry. The Infrastructure adapter for <see cref="IShadowRunPolicy"/>.
/// </summary>
// LEARN: a port with a trivial, data-driven adapter — the whole policy is "look it up in the
// allowlist and check the date." Keeping it behind the port means the use case is testable with a
// stub that just says yes/no, and the real gate is a settings record the composition root builds
// (from config, in a real deployment). Enrolment is the WHO/UNTIL-WHEN; the use case adds the
// "is there a pending switch?" check for the auto-expiry-after-flip half. Both must hold.
internal sealed class ConfiguredShadowRunPolicy(ShadowRunSettings settings) : IShadowRunPolicy
{
    public bool IsEnrolled(Guid orgId, DateOnly asOf) =>
        settings.EnrolledUntil.TryGetValue(orgId, out DateOnly until) && asOf <= until;

    public int DivergenceToleranceMinorUnits => settings.DivergenceToleranceMinorUnits;
}
