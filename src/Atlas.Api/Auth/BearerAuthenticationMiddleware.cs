using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Access;

namespace Atlas.Api.Auth;

/// <summary>
/// Resolves `Authorization: Bearer &lt;token&gt;` into the request's ICurrentContext. Two credential
/// kinds share the one scheme: a partner API key (prefix <c>atk_</c>) or a user session token. No
/// header → anonymous (endpoints decide what that means). A PRESENTED but invalid/expired credential
/// → 401 immediately: garbage credentials are never silently downgraded to anonymous.
/// </summary>
// LEARN: that last rule matters. If an expired token quietly became "anonymous",
// a client with a stale session would see 404s and 403s instead of the one status
// (401) that tells it to refresh its login. Fail loud, fail specific.
public sealed class BearerAuthenticationMiddleware(RequestDelegate next)
{
    private const string Scheme = "Bearer ";

    public async Task InvokeAsync(
        HttpContext http,
        RequestContext context,
        ISessionTokens tokens,
        ISessionRepository sessions,
        IUserRepository users,
        IMembershipRepository memberships,
        IApiKeyRepository apiKeys,
        ISecretGenerator secrets,
        TimeProvider clock)
    {
        string? header = http.Request.Headers.Authorization;

        if (!string.IsNullOrEmpty(header))
        {
            if (!header.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthenticatedException("Unsupported authorization scheme.");
            }

            string token = header[Scheme.Length..].Trim();

            if (token.StartsWith(ApiKey.PlaintextPrefix, StringComparison.Ordinal))
            {
                await AuthenticateApiKeyAsync(http, context, apiKeys, secrets, token);
            }
            else
            {
                await AuthenticateSessionAsync(http, context, tokens, sessions, users, memberships, clock, token);
            }
        }

        await next(http);
    }

    private static async Task AuthenticateSessionAsync(
        HttpContext http, RequestContext context, ISessionTokens tokens, ISessionRepository sessions,
        IUserRepository users, IMembershipRepository memberships, TimeProvider clock, string token)
    {
        Session? session = await sessions.FindByTokenHashAsync(tokens.HashOf(token), http.RequestAborted);

        if (session is null || !session.IsActive(clock.GetUtcNow()))
        {
            throw new UnauthenticatedException("The session is invalid or has expired.");
        }

        User user = await users.FindAsync(session.UserId, http.RequestAborted)
            ?? throw new UnauthenticatedException();

        Membership? membership = await memberships.FindForUserAsync(user.Id, http.RequestAborted);

        context.Attach(user, membership, token);
    }

    private static async Task AuthenticateApiKeyAsync(
        HttpContext http, RequestContext context, IApiKeyRepository apiKeys, ISecretGenerator secrets, string token)
    {
        ApiKey? apiKey = await apiKeys.FindByHashAsync(secrets.HashOf(token), http.RequestAborted);

        if (apiKey is null || !apiKey.IsActive)
        {
            throw new UnauthenticatedException("The API key is invalid or has been revoked.");
        }

        // Scopes are enforced HERE, at the edge, for API keys only — a session user is governed by
        // roles instead. A key that lacks the verb:resource its request needs is a 403, not a 401:
        // it authenticated fine, it just isn't allowed to do THIS.
        if (RequiredScope(http.Request) is { } required && !apiKey.HasScope(required))
        {
            throw new ForbiddenException($"This API key lacks the '{required}' scope.");
        }

        context.AttachApiKey(apiKey.OrgId, apiKey.Id);
    }

    /// <summary>
    /// The scope a request needs: <c>read:{resource}</c> for GETs, <c>write:{resource}</c> otherwise,
    /// where the resource is the first path segment after <c>/api/v1/</c>. Null when no resource can
    /// be identified (e.g. non-API paths) — the endpoint's own authz still applies.
    /// </summary>
    private static string? RequiredScope(HttpRequest request)
    {
        string path = request.Path.Value ?? string.Empty;
        const string prefix = "/api/v1/";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string rest = path[prefix.Length..];
        int slash = rest.IndexOf('/');
        string resource = (slash < 0 ? rest : rest[..slash]).ToLowerInvariant();
        if (resource.Length == 0)
        {
            return null;
        }

        string verb = HttpMethods.IsGet(request.Method) ? "read" : "write";
        return $"{verb}:{resource}";
    }
}
