using Atlas.Domain.Compliance;
using Atlas.Domain.Countries;

namespace Atlas.Application.Reporting;

// 📘 THESE ARE PROJECTIONS, NOT ENTITIES. Every record here is a read model: a shape chosen for a
// question, with no identity, no invariants, and no behaviour. A HeadcountRow cannot be "invalid" —
// it is a count that already happened. Contrast the Worker aggregate, which guards its own creation
// and refuses illegal states. The read side does not need those guards because it never writes, and
// paying for them (loading the aggregate, running its rules) is the N+1 tax this sprint hunts. Rows
// carry domain VALUE types where they help the next fold (CountryCode to pick a rule pack), but never
// domain ENTITIES that would drag a graph along — except WorkerComplianceInput, whose whole job is to
// hand the evaluator the documents it must fold, loaded in bulk rather than per worker.

/// <summary>Headcount in one country: the total, and how many are still inside statutory probation.</summary>
public sealed record HeadcountRow(string Country, int Total, int InProbation, int Active);

/// <summary>
/// Employment cost for one country+currency in one month: worker count and the folded gross, employer
/// burden, and net totals. Currency is part of the grouping key because two currencies never sum.
/// </summary>
public sealed record EmploymentCostRow(
    string Country,
    string Currency,
    int WorkerCount,
    decimal Gross,
    decimal EmployerBurden,
    decimal Net);

/// <summary>One document approaching its expiry — a work permit, a contract, a mandate about to lapse.</summary>
public sealed record UpcomingRenewalRow(
    Guid WorkerId,
    string WorkerName,
    CountryCode Country,
    DocumentKind Kind,
    string Reference,
    DateOnly? ExpiresOn);

/// <summary>
/// One active worker and every compliance document they hold — the input the org roster folds through
/// <see cref="ComplianceEvaluator"/>. Carries real <see cref="WorkerDocument"/> entities because the
/// evaluator consumes them directly; the adapter loads them for the WHOLE org in one query and groups
/// in memory, so this list exists without a per-worker round-trip.
/// </summary>
public sealed record WorkerComplianceInput(
    Guid WorkerId,
    string FullName,
    CountryCode Country,
    IReadOnlyList<WorkerDocument> Documents);

/// <summary>
/// One worker's line in the org compliance roster: the verdict plus how much is outstanding
/// (missing or expired) and how much is merely expiring soon (a warning, not a breach).
/// </summary>
public sealed record ComplianceRosterRow(
    Guid WorkerId,
    string WorkerName,
    string Country,
    bool IsCompliant,
    int Outstanding,
    int ExpiringSoon);
