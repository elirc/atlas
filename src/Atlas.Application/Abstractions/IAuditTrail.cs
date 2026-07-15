using Atlas.Application.Pagination;
using Atlas.Domain.Auditing;

namespace Atlas.Application.Abstractions;

/// <summary>
/// Appends audit entries INTO the ambient unit of work — an entry becomes durable
/// with the change it describes, or not at all.
/// </summary>
// LEARN: Append is synchronous and unconditional; there is no "audit failed,
// continue anyway" branch, because an audit log with best-effort semantics is
// worse than none — it teaches people to trust records that can lie.
public interface IAuditTrail
{
    void Append(AuditEntry entry);

    /// <summary>Org-scoped, newest-first page over (AtUtc, Id) keyset.</summary>
    Task<IReadOnlyList<AuditEntry>> ListForOrgAsync(
        Guid orgId,
        TimelineCursor? after,
        int fetch,
        CancellationToken cancellationToken);
}
