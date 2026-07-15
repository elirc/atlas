using Atlas.Application.Errors;
using Atlas.Application.Invoicing;
using Atlas.Application.Pagination;
using Atlas.Application.Payments;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Invoicing;

namespace Atlas.Application.Tests.Invoicing;

public class InvoiceReadsTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly CurrencyCode Usd = CurrencyCode.From("USD");
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly InMemoryContractorInvoiceRepository _invoices = new();
    private readonly ScriptedPaymentProvider _payments = new();

    private StubContext Ctx => StubContext.OrgUser(OrgId, Role.OrgAdmin, UserId);

    private GetInvoice Get() => new(_invoices, Ctx);
    private ListInvoices List() => new(_invoices, Ctx);
    private ReconcileInvoicePayment Reconcile() => new(_invoices, _payments, Ctx);

    private ContractorInvoice Seed(InvoiceStatus upTo, DateTimeOffset createdAt, Guid? org = null)
    {
        var invoice = ContractorInvoice.Open(
            org ?? OrgId, Guid.NewGuid(), Usd,
            [InvoiceLine.For("Work", Money.Of(100m, Usd))], UserId, createdAt);

        if (upTo >= InvoiceStatus.Submitted)
        {
            invoice.Submit(createdAt);
        }

        if (upTo >= InvoiceStatus.HrApproved)
        {
            invoice.HrApprove(Guid.NewGuid(), createdAt);
        }

        if (upTo >= InvoiceStatus.Approved)
        {
            invoice.AdminApprove(Guid.NewGuid(), createdAt);
        }

        if (upTo >= InvoiceStatus.Paid)
        {
            invoice.MarkPaid("pg_seeded", createdAt);
        }

        _invoices.Rows.Add(invoice);
        return invoice;
    }

    [Fact]
    public async Task Get_returns_an_invoice_in_the_org()
    {
        ContractorInvoice seeded = Seed(InvoiceStatus.Submitted, Now);

        ContractorInvoice found = await Get().ExecuteAsync(seeded.Id, CancellationToken.None);

        Assert.Equal(seeded.Id, found.Id);
    }

    [Fact]
    public async Task Get_of_an_invoice_in_another_org_is_a_404()
    {
        ContractorInvoice foreign = Seed(InvoiceStatus.Submitted, Now, org: Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Get().ExecuteAsync(foreign.Id, CancellationToken.None));
    }

    [Fact]
    public async Task List_pages_newest_first_by_the_cursor()
    {
        Seed(InvoiceStatus.Submitted, Now.AddMinutes(1));
        Seed(InvoiceStatus.Submitted, Now.AddMinutes(2));
        ContractorInvoice newest = Seed(InvoiceStatus.Submitted, Now.AddMinutes(3));

        Page<ContractorInvoice> first = await List().ExecuteAsync(status: null, cursor: null, limit: 2, CancellationToken.None);

        Assert.Equal(2, first.Items.Count);
        Assert.Equal(newest.Id, first.Items[0].Id);
        Assert.NotNull(first.NextCursor);

        Page<ContractorInvoice> second = await List().ExecuteAsync(null, first.NextCursor, 2, CancellationToken.None);

        Assert.Single(second.Items);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task List_filters_by_status_the_reconciliation_query()
    {
        Seed(InvoiceStatus.Submitted, Now.AddMinutes(1));
        ContractorInvoice paid = Seed(InvoiceStatus.Paid, Now.AddMinutes(2));

        Page<ContractorInvoice> onlyPaid = await List().ExecuteAsync("Paid", cursor: null, limit: 25, CancellationToken.None);

        ContractorInvoice single = Assert.Single(onlyPaid.Items);
        Assert.Equal(paid.Id, single.Id);
    }

    [Fact]
    public async Task List_rejects_an_unknown_status()
    {
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            List().ExecuteAsync("Nonsense", cursor: null, limit: 25, CancellationToken.None));
    }

    [Fact]
    public async Task List_rejects_an_out_of_range_limit()
    {
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            List().ExecuteAsync(status: null, cursor: null, limit: 5000, CancellationToken.None));
    }

    [Fact]
    public async Task Reconcile_a_paid_invoice_surfaces_the_providers_settlement()
    {
        ContractorInvoice paid = Seed(InvoiceStatus.Paid, Now);
        _payments.NextSettlement = new PaymentSettlement(
            new VendorPaymentReference("pg_seeded"), SettlementStatus.Settled);

        InvoiceReconciliation result = await Reconcile().ExecuteAsync(paid.Id, CancellationToken.None);

        Assert.NotNull(result.Settlement);
        Assert.Equal(SettlementStatus.Settled, result.Settlement!.Status);
    }

    [Fact]
    public async Task Reconcile_an_unpaid_invoice_has_no_settlement_to_show()
    {
        ContractorInvoice submitted = Seed(InvoiceStatus.Submitted, Now);

        InvoiceReconciliation result = await Reconcile().ExecuteAsync(submitted.Id, CancellationToken.None);

        // No vendor reference yet → nothing to reconcile against; the provider is not called.
        Assert.Null(result.Settlement);
    }
}
