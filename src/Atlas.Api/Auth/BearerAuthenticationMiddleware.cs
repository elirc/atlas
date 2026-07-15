using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Access;

namespace Atlas.Api.Auth;

/// <summary>
/// Resolves `Authorization: Bearer &lt;token&gt;` into the request's ICurrentContext.
/// No header → anonymous (endpoints decide what that means). A PRESENTED but
/// invalid/expired token → 401 immediately: garbage credentials are never
/// silently downgraded to anonymous.
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

            Session? session = await sessions.FindByTokenHashAsync(
                tokens.HashOf(token), http.RequestAborted);

            if (session is null || !session.IsActive(clock.GetUtcNow()))
            {
                throw new UnauthenticatedException("The session is invalid or has expired.");
            }

            User user = await users.FindAsync(session.UserId, http.RequestAborted)
                ?? throw new UnauthenticatedException();

            Membership? membership =
                await memberships.FindForUserAsync(user.Id, http.RequestAborted);

            context.Attach(user, membership, token);
        }

        await next(http);
    }
}
