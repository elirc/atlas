using Atlas.Application.Abstractions;
using Atlas.Application.Reporting;
using Atlas.Domain;
using Atlas.Domain.Compliance;
using Atlas.Domain.Countries;
using Atlas.Domain.Payroll;
using Atlas.Domain.Workforce;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Persistence.Reporting;

/// <summary>
/// The read-model adapter: every report is a SQL projection over the existing tables, NEVER an
/// aggregate load. No <c>Include</c>, no change tracking, no entity graph — just the columns a
/// question needs, shaped in the database and folded (only where SQLite forces it) in memory.
/// </summary>
// 📘 WHY RAW-ish PROJECTIONS AND NOT REPOSITORIES. The repositories return aggregates because the
// write side needs whole, valid objects. The read side needs the opposite: the narrowest row that
// answers the question. Reusing a repository here would load graphs to throw most of them away — the
// exact cost this sprint measures and removes. So reporting gets its own adapter, behind its own
// port, tested against SQLite AND audited for Postgres dialect (ADR-0017). The type safety we keep is
// DOMAIN safety; these are read-only views where the schema is the contract.
internal sealed class ReportQueries(AtlasDbContext db) : IReportQueries
{
    public async Task<IReadOnlyList<HeadcountRow>> HeadcountByCountryAsync(
        Guid orgId, DateOnly asOf, CancellationToken cancellationToken)
    {
        // Pull the two columns headcount needs, nothing more — not the Worker aggregate. The COUNT
        // could be pushed into a SQL GROUP BY, but "in probation as of X" needs the per-country
        // statutory window (a DOMAIN rule, CountryEmploymentRules), which does not belong in SQL. So
        // we group in memory over a minimal projection — a deliberate query-shape choice, not laziness.
        List<HeadcountProjection> rows = await db.Set<Worker>().AsNoTracking()
            .Where(w => w.OrgId == orgId)
            .Select(w => new HeadcountProjection(w.Country, w.HiredOn))
            .ToListAsync(cancellationToken);

        return
        [
            .. rows
                .GroupBy(r => r.Country)
                .Select(g =>
                {
                    int total = g.Count();
                    int inProbation = g.Count(r =>
                        asOf < r.HiredOn.AddMonths(CountryEmploymentRules.ProbationMonthsFor(g.Key)));
                    return new HeadcountRow(g.Key.Value, total, inProbation, total - inProbation);
                })
                .OrderBy(r => r.Country)
        ];
    }

    public async Task<IReadOnlyList<EmploymentCostRow>> EmploymentCostByCountryMonthAsync(
        Guid orgId, int year, int month, CancellationToken cancellationToken)
    {
        // Month as a half-open DateOnly range — DateOnly compares cleanly on SQLite (unlike
        // DateTimeOffset), so the period filter runs IN the database, not after a full scan.
        var monthStart = new DateOnly(year, month, 1);
        DateOnly nextMonth = monthStart.AddMonths(1);

        // Join payslip → run → worker: EXECUTED runs only (immutable, trustworthy — see the use case),
        // the worker for its country. Project the owned-Money AMOUNT columns; do NOT SUM them here.
        List<CostProjection> rows = await (
            from p in db.Set<Payslip>().AsNoTracking()
            join r in db.Set<PayrollRun>().AsNoTracking() on p.RunId equals r.Id
            join w in db.Set<Worker>().AsNoTracking() on p.WorkerId equals w.Id
            where p.OrgId == orgId
                && r.State == PayrollRunState.Executed
                && p.PeriodStart >= monthStart
                && p.PeriodStart < nextMonth
            select new CostProjection(
                w.Country,
                p.Gross.Currency,
                p.WorkerId,
                p.Gross.Amount,
                p.EmployerCost.Amount,
                p.Net.Amount))
            .ToListAsync(cancellationToken);

        // ⚠️ THE DECIMAL FOLD IS IN MEMORY, ON PURPOSE. SQLite stores decimal as TEXT and cannot SUM
        // it — the same wall PayrollRun.Total*() hit in S7. So the SQL fetches the amounts and the
        // sum happens here, in C#, in full decimal precision. Grouping by (country, currency) because
        // Money refuses to add across currencies; a mixed-currency org yields one row per currency.
        return
        [
            .. rows
                .GroupBy(x => new { x.Country, x.Currency })
                .Select(g => new EmploymentCostRow(
                    g.Key.Country.Value,
                    g.Key.Currency.Value,
                    g.Select(x => x.WorkerId).Distinct().Count(),
                    g.Sum(x => x.Gross),
                    g.Sum(x => x.EmployerBurden),
                    g.Sum(x => x.Net)))
                .OrderBy(r => r.Country).ThenBy(r => r.Currency)
        ];
    }

    public async Task<IReadOnlyList<UpcomingRenewalRow>> UpcomingRenewalsAsync(
        Guid orgId, DateOnly through, CancellationToken cancellationToken) =>
        await RenewalQuery(orgId, through).ToListAsync(cancellationToken);

    public IAsyncEnumerable<UpcomingRenewalRow> StreamUpcomingRenewalsAsync(
        Guid orgId, DateOnly through, CancellationToken cancellationToken) =>
        // AsAsyncEnumerable streams rows off the reader one at a time — no ToList, no full buffer in
        // memory. The endpoint writes each row to the response as it arrives; a million-row export
        // costs one row of memory, not a million. Cancellation flows via the caller's await-foreach.
        RenewalQuery(orgId, through).AsAsyncEnumerable();

    public async Task<IReadOnlyList<WorkerComplianceInput>> ActiveWorkersWithDocumentsAsync(
        Guid orgId, DateOnly asOf, CancellationToken cancellationToken)
    {
        // 🔍 TWO QUERIES, NOT 1+N. Query one: the active workers (id, name, country) as a projection.
        // Query two: EVERY document in the org, once. We then group the documents by worker in memory
        // and stitch. The naive alternative — load workers, then one document query per worker — is
        // the N+1 the roster use case explains and the query-count test forbids. The number of
        // round-trips here is fixed at two regardless of how many workers the org employs.
        var workers = await db.Set<Worker>().AsNoTracking()
            .Where(w => w.OrgId == orgId && w.HiredOn <= asOf)
            .Select(w => new { w.Id, w.FullName, w.Country })
            .ToListAsync(cancellationToken);

        List<WorkerDocument> documents = await db.Set<WorkerDocument>().AsNoTracking()
            .Where(d => d.OrgId == orgId)
            .ToListAsync(cancellationToken);

        Dictionary<Guid, IReadOnlyList<WorkerDocument>> byWorker = documents
            .GroupBy(d => d.WorkerId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<WorkerDocument>)[.. g]);

        return
        [
            .. workers.Select(w => new WorkerComplianceInput(
                w.Id,
                w.FullName,
                w.Country,
                byWorker.TryGetValue(w.Id, out IReadOnlyList<WorkerDocument>? held) ? held : []))
        ];
    }

    private IQueryable<UpcomingRenewalRow> RenewalQuery(Guid orgId, DateOnly through) =>
        from d in db.Set<WorkerDocument>().AsNoTracking()
        join w in db.Set<Worker>().AsNoTracking() on d.WorkerId equals w.Id
        where d.OrgId == orgId && d.ExpiresOn != null && d.ExpiresOn <= through
        orderby d.ExpiresOn, w.FullName
        select new UpcomingRenewalRow(d.WorkerId, w.FullName, w.Country, d.Kind, d.Reference, d.ExpiresOn);

    // Private projection shapes — never leave the adapter. They exist only to carry the minimal
    // columns from SQL into the in-memory fold.
    private sealed record HeadcountProjection(CountryCode Country, DateOnly HiredOn);

    private sealed record CostProjection(
        CountryCode Country,
        CurrencyCode Currency,
        Guid WorkerId,
        decimal Gross,
        decimal EmployerBurden,
        decimal Net);
}
