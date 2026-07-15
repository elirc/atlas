using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Events;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;

namespace Atlas.Application.ApiKeys;

/// <summary>
/// Revoke a key immediately — the next request presenting it fails auth. Idempotent: revoking a
/// revoked key is a no-op, so a nervous admin can click twice.
/// </summary>
public sealed class RevokeApiKey(
    IApiKeyRepository apiKeys,
    IAuditTrail audit,
    IEventSink events,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task ExecuteAsync(Guid apiKeyId, CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin);
        Guid orgId = context.RequireOrg();

        ApiKey apiKey = await apiKeys.FindInOrgAsync(apiKeyId, orgId, cancellationToken)
            ?? throw new NotFoundException("api_key", apiKeyId);

        DateTimeOffset now = clock.GetUtcNow();
        apiKey.Revoke(now);

        string details = JsonSerializer.Serialize(new { apiKey.Name, apiKey.DisplayPrefix });
        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "api_key.revoked", "api_key", apiKey.Id, details, now));

        await events.PublishAsync(
            new AtlasEvent("api_key.revoked", orgId, "api_key", apiKey.Id, details, now),
            cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);
    }
}
