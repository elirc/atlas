using Atlas.Application.Abstractions;
using Atlas.Application.Pagination;
using Atlas.Application.Payments;
using Atlas.Domain.Invoicing;

namespace Atlas.Application.Tests.Fakes;

public sealed class InMemoryContractorRepository : IContractorRepository
{
    public List<Contractor> Rows { get; } = [];

    public Task AddAsync(Contractor contractor, CancellationToken ct)
    {
        Rows.Add(contractor);
        return Task.CompletedTask;
    }

    public Task<Contractor?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken ct) =>
        Task.FromResult(Rows.FirstOrDefault(c => c.Id == id && c.OrgId == orgId));

    public Task<IReadOnlyList<Contractor>> ListForOrgAsync(Guid orgId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Contractor>>(
            [.. Rows.Where(c => c.OrgId == orgId).OrderByDescending(c => c.CreatedAtUtc).ThenByDescending(c => c.Id)]);
}

public sealed class InMemoryContractorInvoiceRepository : IContractorInvoiceRepository
{
    public List<ContractorInvoice> Rows { get; } = [];

    public Task AddAsync(ContractorInvoice invoice, CancellationToken ct)
    {
        Rows.Add(invoice);
        return Task.CompletedTask;
    }

    public Task<ContractorInvoice?> FindInOrgAsync(Guid id, Guid orgId, CancellationToken ct) =>
        Task.FromResult(Rows.FirstOrDefault(i => i.Id == id && i.OrgId == orgId));

    public Task<IReadOnlyList<ContractorInvoice>> ListForOrgAsync(
        Guid orgId, InvoiceStatus? status, TimelineCursor? after, int fetch, CancellationToken ct)
    {
        IEnumerable<ContractorInvoice> ordered = Rows
            .Where(i => i.OrgId == orgId)
            .Where(i => status is null || i.Status == status)
            .OrderByDescending(i => i.CreatedAtUtc)
            .ThenByDescending(i => i.Id);

        if (after is { } cursor)
        {
            ordered = ordered.Where(i =>
                i.CreatedAtUtc < cursor.CreatedAtUtc
                || (i.CreatedAtUtc == cursor.CreatedAtUtc
                    && i.Id.CompareTo(cursor.Id) < 0));
        }

        return Task.FromResult<IReadOnlyList<ContractorInvoice>>([.. ordered.Take(fetch)]);
    }
}

/// <summary>
/// A payment provider whose answer the test scripts. It ALSO counts how many times it was asked
/// to pay — the window a use-case test uses to observe the vendor call. (S10 will care about that
/// count; today it is just a knob.)
/// </summary>
public sealed class ScriptedPaymentProvider : IPaymentProvider
{
    public PaymentResult NextResult { get; set; } =
        new PaymentResult.Succeeded(new VendorPaymentReference("pg_scripted"), SettlementStatus.Pending);

    public PaymentSettlement? NextSettlement { get; set; }

    public Exception? ThrowOnGetSettlement { get; set; }

    public int PayCalls { get; private set; }

    public List<PaymentInstruction> Instructions { get; } = [];

    public Task<PaymentResult> PayAsync(PaymentInstruction instruction, CancellationToken ct)
    {
        PayCalls++;
        Instructions.Add(instruction);
        return Task.FromResult(NextResult);
    }

    public Task<PaymentSettlement> GetSettlementAsync(VendorPaymentReference reference, CancellationToken ct)
    {
        if (ThrowOnGetSettlement is not null)
        {
            throw ThrowOnGetSettlement;
        }

        return Task.FromResult(
            NextSettlement ?? new PaymentSettlement(reference, SettlementStatus.Settled));
    }
}
