using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Orgs;

namespace Atlas.Application.Orgs;

public sealed class GetClientOrg(IClientOrgRepository orgs, ICurrentContext context)
{
    public async Task<ClientOrg> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        context.RequireUser();

        // LEARN: cross-ORG access is 404, not 403. A 403 confirms the resource
        // exists — existence across tenant boundaries is need-to-know. Same status
        // an actually-missing org gets: indistinguishable on purpose.
        if (!context.IsAtlasOps && context.OrgId != id)
        {
            throw new NotFoundException("client org", id);
        }

        ClientOrg? org = await orgs.FindAsync(id, cancellationToken);

        return org ?? throw new NotFoundException("client org", id);
    }
}
