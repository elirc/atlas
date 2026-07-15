using Atlas.Application.Abstractions;
using Atlas.Domain.Access;
using Atlas.Domain.Compliance;
using Atlas.Domain.Countries;

namespace Atlas.Application.Reporting;

/// <summary>
/// The org-wide compliance roster: every active worker with a compliant/non-compliant verdict and the
/// counts behind it. The rollup S11 promised at org scope (it shipped only the per-worker view).
/// </summary>
// 🔍 THIS IS THE N+1 FIX, MADE ORDINARY. The tempting implementation loops the workers and asks the
// per-worker document repository once per worker — correct, readable, and 1+N database round-trips
// that grows without bound as the org hires. The fix is not cleverer per-row code; it is a different
// SHAPE of read: ActiveWorkersWithDocumentsAsync loads the whole org's documents in ONE query and
// hands them back grouped, so this fold touches the database a fixed two times whether the org has
// ten workers or ten thousand. The domain fold is UNCHANGED — the same pure ComplianceEvaluator that
// the single-worker view uses. We changed the query shape, not the compliance logic. The query-count
// test (ComplianceRosterQueryCountTests) pins both halves: it proves the naive loop is 1+N and this
// path is a constant 2, so a future edit that reintroduces the per-row read goes red in CI.
public sealed class GetComplianceRoster(IReportQueries reports, ICurrentContext context, TimeProvider clock)
{
    public async Task<IReadOnlyList<ComplianceRosterRow>> ExecuteAsync(
        DateOnly? asOf, CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        DateOnly on = asOf ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        IReadOnlyList<WorkerComplianceInput> inputs =
            await reports.ActiveWorkersWithDocumentsAsync(orgId, on, cancellationToken);

        var roster = new List<ComplianceRosterRow>(inputs.Count);
        foreach (WorkerComplianceInput input in inputs)
        {
            // The country's REQUIRED set comes from the plugin (the S11 thesis: a JPN or CAN worker is
            // evaluated the instant their pack file exists). The fold is the same pure function the
            // per-worker endpoint runs — no I/O, no clock, exact verdicts.
            ICountryRulePack pack = CountryRulePacks.For(input.Country);
            WorkerComplianceReport report =
                ComplianceEvaluator.Evaluate(pack.RequiredDocuments, input.Documents, on);

            int outstanding = report.Lines.Count(l =>
                l.State is DocumentComplianceState.Missing or DocumentComplianceState.Expired);
            int expiringSoon = report.Lines.Count(l =>
                l.State is DocumentComplianceState.ExpiringSoon);

            roster.Add(new ComplianceRosterRow(
                input.WorkerId, input.FullName, input.Country.Value,
                report.IsCompliant, outstanding, expiringSoon));
        }

        // Non-compliant first (the worklist a compliance officer acts on), then most-outstanding.
        return
        [
            .. roster
                .OrderBy(r => r.IsCompliant)
                .ThenByDescending(r => r.Outstanding)
                .ThenBy(r => r.WorkerName)
        ];
    }
}
