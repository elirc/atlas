using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Pagination;
using Atlas.Domain.Access;
using Atlas.Domain.Invoicing;

namespace Atlas.Application.Invoicing;

/// <summary>
/// The org's invoices, newest first, cursor-paged — optionally filtered to a single status. The
/// status filter IS the reconciliation query: "give me every Paid invoice" is what finance runs
/// to check Atlas's books against the provider's statement.
/// </summary>
public sealed class ListInvoices(IContractorInvoiceRepository invoices, ICurrentContext context)
{
    public async Task<Page<ContractorInvoice>> ExecuteAsync(
        string? status,
        string? cursor,
        int? limit,
        CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        // WHY parse-and-reject, not ignore: an unknown ?status= is a client mistake worth a 400,
        // not a silent "show everything" that hides the typo until someone trusts an empty page.
        InvoiceStatus? filter = null;
        if (status is not null)
        {
            if (!Enum.TryParse(status, ignoreCase: true, out InvoiceStatus parsed))
            {
                throw new RequestValidationException(
                    $"'{status}' is not a known invoice status.");
            }

            filter = parsed;
        }

        int pageSize = limit ?? Page<ContractorInvoice>.DefaultLimit;

        // Reject, don't clamp — the ListClientOrgs stance. A silently-clamped limit teaches the
        // client that an oversized page "works" until the data grows past it.
        if (pageSize < 1 || pageSize > Page<ContractorInvoice>.MaxLimit)
        {
            throw new RequestValidationException(
                $"limit must be between 1 and {Page<ContractorInvoice>.MaxLimit}.");
        }

        TimelineCursor? after = cursor is null ? null : TimelineCursor.Decode(cursor);

        IReadOnlyList<ContractorInvoice> fetched =
            await invoices.ListForOrgAsync(orgId, filter, after, pageSize + 1, cancellationToken);

        bool hasMore = fetched.Count > pageSize;
        IReadOnlyList<ContractorInvoice> items = hasMore ? [.. fetched.Take(pageSize)] : fetched;

        string? next = hasMore
            ? new TimelineCursor(items[^1].CreatedAtUtc, items[^1].Id).Encode()
            : null;

        return new Page<ContractorInvoice>(items, next);
    }
}
