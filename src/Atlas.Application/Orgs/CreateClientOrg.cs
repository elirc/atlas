using Atlas.Application.Abstractions;
using Atlas.Domain.Orgs;

namespace Atlas.Application.Orgs;

public sealed class CreateClientOrg(
    IClientOrgRepository orgs,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<ClientOrg> ExecuteAsync(string? name, CancellationToken cancellationToken)
    {
        // LEARN: the entity validates itself (ClientOrg.Create throws on bad names);
        // the use case orchestrates: construct → persist → commit. No business rule
        // lives here — and none lives in the endpoint either.
        var org = ClientOrg.Create(name, clock.GetUtcNow());

        await orgs.AddAsync(org, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return org;
    }
}
