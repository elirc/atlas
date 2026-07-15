using Atlas.Application.Abstractions;
using Atlas.Domain.Access;

namespace Atlas.Application.Reporting;

/// <summary>
/// Headcount by country as of a date — how many people, where, and how many still in probation. Reads
/// through <see cref="IReportQueries"/>, never the Worker aggregate: this is a COUNT, not a graph.
/// </summary>
// LEARN: notice how little this use case does — authz, one clock read, one port call. That is the
// read side earning its keep. The intelligence is in the projection (the adapter's SQL); the use
// case is just the org-scoped, role-gated door to it. A report is not a place for domain logic; if a
// number here drove a decision, it would move to the domain (the Reviewer's promise, ADR-0017).
public sealed class GetHeadcountReport(IReportQueries reports, ICurrentContext context, TimeProvider clock)
{
    public async Task<IReadOnlyList<HeadcountRow>> ExecuteAsync(
        DateOnly? asOf, CancellationToken cancellationToken)
    {
        // Reports name people and pay — the same HR-manager-and-up gate the audit log and compliance
        // views use. Cross-org rows are impossible: orgId scopes the projection at the source.
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        // "As of" is a calendar question; UTC is the least-wrong org-facing default (the GetWorkerTerms
        // stance). The caller may pin an explicit date to ask "who was in probation on X?".
        DateOnly on = asOf ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        return await reports.HeadcountByCountryAsync(orgId, on, cancellationToken);
    }
}
