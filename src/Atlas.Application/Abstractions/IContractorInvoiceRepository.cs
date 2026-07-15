using Atlas.Application.Pagination;
using Atlas.Domain.Invoicing;

namespace Atlas.Application.Abstractions;

/// <summary>Persistence port for the <see cref="ContractorInvoice"/> aggregate (with its lines).</summary>
public interface IContractorInvoiceRepository
{
    Task AddAsync(ContractorInvoice invoice, CancellationToken cancellationToken);

    /// <summary>The invoice (lines included) with this id IN this org, or null.</summary>
    Task<ContractorInvoice?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken cancellationToken);

    /// <summary>
    /// Org-scoped newest-first page over the (CreatedAtUtc, Id) keyset, optionally filtered to a
    /// single <see cref="InvoiceStatus"/> — the reconciliation query ("show me every Paid one").
    /// </summary>
    Task<IReadOnlyList<ContractorInvoice>> ListForOrgAsync(
        Guid orgId,
        InvoiceStatus? status,
        TimelineCursor? after,
        int fetch,
        CancellationToken cancellationToken);
}
