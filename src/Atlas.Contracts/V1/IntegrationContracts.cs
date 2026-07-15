namespace Atlas.Contracts.V1;

// ---- Webhooks ----------------------------------------------------------------------------------

/// <summary>Body of POST /api/v1/webhooks. Omit <c>events</c> (or pass empty) to receive all types.</summary>
public sealed record RegisterWebhookRequest(string? Url, IReadOnlyList<string>? Events);

/// <summary>A webhook subscription as served by /api/v1/webhooks. The secret is NOT here — see below.</summary>
public sealed record WebhookSubscriptionDto(
    Guid Id,
    string Url,
    string Events,
    bool Active,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PausedAtUtc);

/// <summary>
/// The create response — the ONLY place the signing secret appears. Store it now; the API will never
/// show it again (every later view is a plain <see cref="WebhookSubscriptionDto"/>).
/// </summary>
public sealed record WebhookRegisteredDto(WebhookSubscriptionDto Subscription, string Secret);

/// <summary>One row of the delivery log.</summary>
public sealed record WebhookDeliveryDto(
    Guid Id,
    Guid SubscriptionId,
    string EventType,
    string Status,
    int Attempts,
    int? ResponseStatusCode,
    string? LastError,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? DeliveredAtUtc);

// ---- API keys ----------------------------------------------------------------------------------

/// <summary>Body of POST /api/v1/api-keys.</summary>
public sealed record CreateApiKeyRequest(string? Name, IReadOnlyList<string>? Scopes);

/// <summary>An API key as served by /api/v1/api-keys. Only a display prefix — never the key or hash.</summary>
public sealed record ApiKeyDto(
    Guid Id,
    string Name,
    string DisplayPrefix,
    string Scopes,
    bool Active,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? RevokedAtUtc);

/// <summary>The create response — the ONLY place the raw key appears. Store it now.</summary>
public sealed record ApiKeyCreatedDto(ApiKeyDto ApiKey, string Key);
