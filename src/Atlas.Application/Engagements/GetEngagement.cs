using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Access;
using Atlas.Domain.Engagements;

namespace Atlas.Application.Engagements;

/// <summary>One engagement, org-scoped, with its onboarding checklist and timeline.</summary>
public sealed class GetEngagement(IEngagementRepository engagements, ICurrentContext context)
{
    public async Task<Engagement> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        // WHY: engagements name candidates and their pay — HR-manager-and-up, same as
        // the audit log. And FindInOrg means another tenant's engagement is a 404,
        // never a 403 (existence across tenants is need-to-know).
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        Engagement? engagement = await engagements.FindInOrgAsync(id, orgId, cancellationToken);

        return engagement ?? throw new NotFoundException("engagement", id);
    }
}
