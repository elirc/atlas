namespace Atlas.Contracts.V1;

/// <summary>Body of POST /api/v1/auth/register-org — self-service signup.</summary>
public sealed record RegisterOrgRequest(
    string? OrgName,
    string? Email,
    string? Password,
    string? DisplayName);

/// <summary>Body of POST /api/v1/auth/login.</summary>
public sealed record LoginRequest(string? Email, string? Password);

/// <summary>A live session. The token appears here and nowhere else — store it client-side.</summary>
public sealed record AuthSessionDto(
    string Token,
    DateTimeOffset ExpiresAtUtc,
    MeDto User);

/// <summary>The authenticated caller, as seen by GET /api/v1/auth/me.</summary>
public sealed record MeDto(
    Guid UserId,
    string Email,
    string DisplayName,
    bool IsAtlasOps,
    Guid? OrgId,
    string? Role);
