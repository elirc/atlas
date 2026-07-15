using Atlas.Application.Reporting;

namespace Atlas.Application.Abstractions;

/// <summary>
/// The reporting READ side: org-scoped questions executives ask (headcount, cost, renewals,
/// compliance) answered as flat projections, not by traversing aggregates. A PORT — the use cases
/// know this shape; the adapter owns the SQL.
/// </summary>
// 📘 READ SHAPE ≠ WRITE SHAPE — the spine of this sprint. For thirteen sprints the object model
// (aggregates: Worker, PayrollRun, Engagement) has been optimised for WRITES: invariants, state
// machines, one-thing-changes-at-a-time. That shape is exactly wrong for a report. "Headcount by
// country" does not want a Worker aggregate with its terms history and probation rule loaded per
// row — it wants a COUNT grouped by a column. So the read side gets its own port with its own
// result records, and the aggregates are never loaded here at all. This is CQRS-lite: we split the
// READ MODEL from the WRITE MODEL without a message bus, an eventual-consistency story, or a second
// database — just a second port whose return types are projections. The tax of full CQRS buys
// things (independent scaling, denormalised stores) this system does not yet need; the split of the
// query path from the command path buys clarity it needs today. We take the half that pays.
// 🔗 ADR-0017 records the boundary the Reviewer extracted: any figure used to make a DECISION INSIDE
// the system (a gate, a guard, a calculation) must live in the DOMAIN, not here. These are views for
// humans to read, not inputs the system acts on — the schema is their contract, and read-only is why
// raw projection is safe where raw SQL in a command path would not be.
public interface IReportQueries
{
    /// <summary>Live headcount grouped by country, with the in-probation split as of a date.</summary>
    Task<IReadOnlyList<HeadcountRow>> HeadcountByCountryAsync(
        Guid orgId, DateOnly asOf, CancellationToken cancellationToken);

    /// <summary>Employment cost for one month, grouped by country and currency, from EXECUTED runs only.</summary>
    Task<IReadOnlyList<EmploymentCostRow>> EmploymentCostByCountryMonthAsync(
        Guid orgId, int year, int month, CancellationToken cancellationToken);

    /// <summary>Expiry-dated documents (contracts, permits) lapsing on or before <paramref name="through"/>.</summary>
    Task<IReadOnlyList<UpcomingRenewalRow>> UpcomingRenewalsAsync(
        Guid orgId, DateOnly through, CancellationToken cancellationToken);

    /// <summary>
    /// The same renewals query as a STREAM — rows arrive one at a time, never fully buffered, so a
    /// CSV export of a very large org stays memory-bounded. The endpoint writes each row as it lands.
    /// </summary>
    IAsyncEnumerable<UpcomingRenewalRow> StreamUpcomingRenewalsAsync(
        Guid orgId, DateOnly through, CancellationToken cancellationToken);

    /// <summary>
    /// Every active worker of the org (hired on or before <paramref name="asOf"/>) paired with the
    /// documents they hold — in a FIXED number of round-trips, not one per worker. The org compliance
    /// roster folds this through the domain <c>ComplianceEvaluator</c>; the whole point of loading it
    /// this way is that the count does not scale with the number of workers.
    /// </summary>
    Task<IReadOnlyList<WorkerComplianceInput>> ActiveWorkersWithDocumentsAsync(
        Guid orgId, DateOnly asOf, CancellationToken cancellationToken);
}
