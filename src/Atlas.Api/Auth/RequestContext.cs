using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Access;

namespace Atlas.Api.Auth;

/// <summary>
/// The per-request implementation of ICurrentContext. Populated exactly once by
/// BearerAuthenticationMiddleware, read-only to everyone downstream.
/// </summary>
public sealed class RequestContext : ICurrentContext
{
    public bool IsAuthenticated { get; private set; }
    public Guid UserId { get; private set; }
    public bool IsAtlasOps { get; private set; }
    public Guid? OrgId { get; private set; }
    public Role? Role { get; private set; }

    /// <summary>The presented bearer token (needed by logout to revoke itself).</summary>
    public string? RawToken { get; private set; }

    internal void Attach(User user, Membership? membership, string rawToken)
    {
        IsAuthenticated = true;
        UserId = user.Id;
        IsAtlasOps = user.IsAtlasOps;
        OrgId = membership?.OrgId;
        Role = membership?.Role;
        RawToken = rawToken;
    }

    public void RequireUser()
    {
        if (!IsAuthenticated)
        {
            throw new UnauthenticatedException();
        }
    }

    public Guid RequireOrg()
    {
        RequireUser();
        return OrgId ?? throw new ForbiddenException("An org context is required.");
    }

    public void RequireRole(params Role[] anyOf)
    {
        RequireUser();

        // WHY: platform staff pass role checks — ops acting inside a client org is
        // a support workflow, and it is AUDITED (S3's audit log records the user id,
        // which identifies staff). Silent god-mode without an audit trail would be
        // the wrong version of this rule.
        if (IsAtlasOps)
        {
            return;
        }

        if (Role is not { } role || !anyOf.Contains(role))
        {
            throw new ForbiddenException();
        }
    }

    public void RequireAtlasOps()
    {
        RequireUser();
        if (!IsAtlasOps)
        {
            throw new ForbiddenException("Platform staff only.");
        }
    }
}
