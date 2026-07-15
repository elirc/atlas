using Atlas.Application.Abstractions;
using Atlas.Domain.Access;

namespace Atlas.Application.ApiKeys;

/// <summary>Every API key in the caller's org, newest first. Admin only; hashes never leave here.</summary>
public sealed class ListApiKeys(IApiKeyRepository apiKeys, ICurrentContext context)
{
    public async Task<IReadOnlyList<ApiKey>> ExecuteAsync(CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin);
        Guid orgId = context.RequireOrg();

        return await apiKeys.ListForOrgAsync(orgId, cancellationToken);
    }
}
