namespace Atlas.Application.Errors;

/// <summary>
/// Authenticated, but not allowed — the Api maps this to 403.
/// </summary>
// WHY: 403 is reserved for "you may know this exists, but can't do that to it"
// (e.g. an Employee hitting an admin action). Cross-ORG access never throws this —
// it throws NotFoundException, because confirming existence across tenants is
// itself a leak. See docs/concepts/multi-tenancy.md.
public sealed class ForbiddenException(string message = "You do not have permission to do that.")
    : Exception(message);
