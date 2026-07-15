using Atlas.Application.Abstractions;
using Atlas.Domain.Orgs;

namespace Atlas.Application.Orgs;

/// <summary>
/// Platform-staff org creation (manual onboarding). Self-service signup lives in
/// Auth.RegisterOrg — the flow that also creates the first admin.
/// </summary>
public sealed class CreateClientOrg(
    IClientOrgRepository orgs,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<ClientOrg> ExecuteAsync(string? name, CancellationToken cancellationToken)
    {
        context.RequireAtlasOps();

        var org = ClientOrg.Create(name, clock.GetUtcNow());

        await orgs.AddAsync(org, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return org;
    }
}
