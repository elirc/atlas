using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Access;
using Atlas.Domain.Workforce;

namespace Atlas.Application.Workforce;

/// <summary>Load one worker, org-scoped, for the HR/admin detail view.</summary>
public sealed class GetWorker(IWorkerRepository workers, ICurrentContext context)
{
    public async Task<Worker> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        // 🔍 [Reviewer] S13 audit finding AF-3 (harvest of ledger #5, planted S03 in commit
        // d5e4db3). Until this sprint the two lines below read:
        //
        //     context.RequireUser();
        //     Worker? worker = await workers.FindAsync(id, cancellationToken);
        //
        // `RequireUser()` proves you are SOMEBODY; `FindAsync(id)` loads a row by primary key
        // with NO org filter. Together they let any authenticated caller in org B read any
        // worker in org A by guessing (or leaking) an id — a textbook IDOR / cross-tenant leak,
        // the exact failure docs/concepts/multi-tenancy.md warned about ("which query could run
        // without a context?"). It slipped through because the HTTP endpoint pairs this call with
        // GetWorkerTerms, which IS org-scoped and 404s first — so the leak was invisible at the
        // API boundary and only exposed by testing the use case directly. That is why the audit
        // test lives in Atlas.Application.Tests, not the HTTP matrix. See NOTES / the audit doc.
        //
        // The fix is two lines, both already available and used by every sibling read
        // (GetQuote, GetEngagement): scope the query to the caller's org, and gate the detail
        // view to the roles that may see pay data — HR-manager-and-up, same as the audit log.
        // A cross-org id now comes back null → 404, indistinguishable from "never existed"
        // (ADR-0003: existence across tenants is need-to-know).
        context.RequireRole(Role.OrgAdmin, Role.HrManager);

        Worker? worker = await workers.FindInOrgAsync(id, context.RequireOrg(), cancellationToken);

        return worker ?? throw new NotFoundException("worker", id);
    }
}
