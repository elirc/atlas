namespace Atlas.Infrastructure.Shadow;

/// <summary>
/// The shadow-run allowlist, time-boxed. Which orgs may run a second, duplicate payroll
/// calculation — and until which day. The empty default means NO org shadows: the expensive,
/// sensitive path is off unless someone deliberately enrols a client for a bounded window.
/// </summary>
// 📘 THE GATE AS CONFIGURATION (the Reviewer's S12 gating win, ADR-0014). Shadow mode exists to
// de-risk a real migration, and a real migration is a scheduled, per-client, time-boxed project —
// not a permanent doubling of everyone's compute. So enrolment is an explicit map of org → last
// allowed day: an org is in the programme only while its window is open, and the window closes on a
// date finance chose. Combined with the "is there a pending switch?" check in the use case, this
// makes shadowing self-limiting from both ends — you cannot start it without enrolling, and it
// stops on its own the day either the window closes or the flip lands.
public sealed record ShadowRunSettings
{
    /// <summary>Org id → the last calendar day (inclusive) that org may generate shadow comparisons.</summary>
    public IReadOnlyDictionary<Guid, DateOnly> EnrolledUntil { get; init; } =
        new Dictionary<Guid, DateOnly>();

    /// <summary>
    /// Net deltas at or below this many minor units are treated as rounding-dialect noise, not
    /// divergence. Default 2: absorbs the at-most-one-minor-unit-per-line drift between banker's and
    /// away-from-zero rounding across gross + a bundled deduction, and flags anything larger (a real
    /// model mismatch, like LedgerPay ignoring a wage cap).
    /// </summary>
    public int DivergenceToleranceMinorUnits { get; init; } = 2;

    /// <summary>App default: no org enrolled, tolerance 2. Shadow mode is OFF until configured.</summary>
    public static ShadowRunSettings Default { get; } = new();
}
