using Atlas.Domain.Access;

namespace Atlas.Application.Abstractions;

/// <summary>
/// Who is making this request, in which org, with what role. Built once per
/// request by the Api's token filter; consumed by use cases.
/// </summary>
// LEARN: this is a PORT, not a static. Use cases declare "I need to know the
// caller" in their constructor, tests hand them a stub caller, and nothing in
// Application ever touches HttpContext. Ambient statics (HttpContext.Current
// style) are how authorization becomes untestable.
public interface ICurrentContext
{
    bool IsAuthenticated { get; }
    Guid UserId { get; }
    bool IsAtlasOps { get; }

    /// <summary>The caller's org, or null for platform staff acting org-lessly.</summary>
    Guid? OrgId { get; }

    Role? Role { get; }

    /// <summary>Throws UnauthenticatedException when anonymous.</summary>
    void RequireUser();

    /// <summary>The caller's org id, or throws (401 if anonymous, 403 if org-less ops).</summary>
    Guid RequireOrg();

    /// <summary>Throws ForbiddenException unless the caller holds one of the roles (ops always passes).</summary>
    void RequireRole(params Role[] anyOf);

    /// <summary>Throws ForbiddenException unless the caller is platform staff.</summary>
    void RequireAtlasOps();
}
