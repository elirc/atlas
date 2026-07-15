using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Events;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;

namespace Atlas.Application.ApiKeys;

/// <summary>The key plus its one-time plaintext — shown once, never again.</summary>
public sealed record ApiKeyIssue(ApiKey ApiKey, string Plaintext);

/// <summary>
/// Mint a partner API key with an explicit scope set. The raw key is generated, hashed for storage,
/// and returned exactly once; only the hash and a display prefix survive. Admin-only — issuing a
/// credential is as sharp an authority as paying an invoice.
/// </summary>
public sealed class CreateApiKey(
    IApiKeyRepository apiKeys,
    ISecretGenerator secrets,
    IAuditTrail audit,
    IEventSink events,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    public async Task<ApiKeyIssue> ExecuteAsync(
        string? name, IReadOnlyList<string>? scopes, CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin);
        Guid orgId = context.RequireOrg();
        DateTimeOffset now = clock.GetUtcNow();

        string plaintext = secrets.NewSecret(ApiKey.PlaintextPrefix);
        string displayPrefix = plaintext[..Math.Min(12, plaintext.Length)];
        string keyHash = secrets.HashOf(plaintext);

        ApiKey apiKey = ApiKey.Issue(orgId, name, displayPrefix, keyHash, scopes, now);
        await apiKeys.AddAsync(apiKey, cancellationToken);

        string details = JsonSerializer.Serialize(new { apiKey.Name, apiKey.ScopeCsv, apiKey.DisplayPrefix });
        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "api_key.created", "api_key", apiKey.Id, details, now));

        await events.PublishAsync(
            new AtlasEvent("api_key.created", orgId, "api_key", apiKey.Id, details, now),
            cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);

        return new ApiKeyIssue(apiKey, plaintext);
    }
}
