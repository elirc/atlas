using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Access;
using Atlas.Domain.Compliance;

namespace Atlas.Application.Compliance;

/// <summary>
/// The org-wide expiring-documents view: every expiry-dated document expiring on or before
/// today + <c>withinDays</c>. This is what a compliance officer runs on Monday morning to see what
/// lapses this month. HR/admin only.
/// </summary>
public sealed class ListExpiringDocuments(
    IWorkerDocumentRepository documents,
    ICurrentContext context,
    TimeProvider clock)
{
    public const int DefaultWindowDays = 30;
    public const int MaxWindowDays = 365;

    public async Task<IReadOnlyList<WorkerDocument>> ExecuteAsync(int? withinDays, CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        int window = withinDays ?? DefaultWindowDays;
        if (window < 1 || window > MaxWindowDays)
        {
            throw new RequestValidationException($"withinDays must be between 1 and {MaxWindowDays}.");
        }

        DateOnly cutoff = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(window);
        return await documents.ListExpiringForOrgAsync(orgId, cutoff, cancellationToken);
    }
}
