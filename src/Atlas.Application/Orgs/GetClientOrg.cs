using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Orgs;

namespace Atlas.Application.Orgs;

public sealed class GetClientOrg(IClientOrgRepository orgs)
{
    public async Task<ClientOrg> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        ClientOrg? org = await orgs.FindAsync(id, cancellationToken);

        return org ?? throw new NotFoundException("client org", id);
    }
}
