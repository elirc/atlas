using Atlas.Domain.Countries;
using Atlas.Domain.Leave;

namespace Atlas.Application.Abstractions;

public interface ILeavePolicyRepository
{
    Task AddAsync(LeavePolicy policy, CancellationToken cancellationToken);

    /// <summary>The org's policy for one country, or null — the uniqueness + balance lookup.</summary>
    Task<LeavePolicy?> FindForAsync(Guid orgId, CountryCode country, CancellationToken cancellationToken);

    /// <summary>All of an org's policies. Bounded by supported countries, so no cursor.</summary>
    Task<IReadOnlyList<LeavePolicy>> ListForOrgAsync(Guid orgId, CancellationToken cancellationToken);
}
