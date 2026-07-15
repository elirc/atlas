using Atlas.Application.Errors;
using Atlas.Application.Invoicing;
using Atlas.Application.Payments;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Countries;
using Atlas.Domain.Invoicing;

namespace Atlas.Application.Tests.Invoicing;

/// <summary>
/// The money-mover, driven through the CLOSED payment-result taxonomy — the whole point of the
/// ACL is that this use case reacts to Succeeded / AlreadyPaid / PaymentRejected / VendorUnavailable
/// and never to a vendor status string.
/// </summary>
public class PayInvoiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly CurrencyCode Usd = CurrencyCode.From("USD");
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly InMemoryContractorRepository _contractors = new();
    private readonly InMemoryContractorInvoiceRepository _invoices = new();
    private readonly ScriptedPaymentProvider _payments = new();
    private readonly RecordingAuditTrail _audit = new();
    private readonly RecordingEventSink _events = new();
    private readonly CountingUnitOfWork _uow = new();

    private PayInvoice UseCase(StubContext? ctx = null) => new(
        _invoices, _contractors, _payments, _audit, _events, _uow,
        ctx ?? StubContext.OrgUser(OrgId, Role.OrgAdmin, UserId), new FixedClock(Now));

    private ContractorInvoice SeedInvoice(InvoiceStatus upTo, Guid? org = null)
    {
        Guid orgId = org ?? OrgId;
        var contractor = Contractor.Create(
            orgId, "Dana", EmailAddress.From("dana@example.com"),
            CountryCode.From("USA"), Usd, Now);
        _contractors.Rows.Add(contractor);

        var invoice = ContractorInvoice.Open(
            orgId, contractor.Id, Usd, [InvoiceLine.For("Work", Money.Of(200m, Usd))], UserId, Now);
        invoice.Submit(Now);

        if (upTo >= InvoiceStatus.HrApproved)
        {
            invoice.HrApprove(Guid.NewGuid(), Now);
        }

        if (upTo >= InvoiceStatus.Approved)
        {
            invoice.AdminApprove(Guid.NewGuid(), Now);
        }

        _invoices.Rows.Add(invoice);
        return invoice;
    }

    [Fact]
    public async Task A_succeeded_result_marks_the_invoice_paid_and_records_the_reference()
    {
        ContractorInvoice invoice = SeedInvoice(InvoiceStatus.Approved);
        _payments.NextResult = new PaymentResult.Succeeded(
            new VendorPaymentReference("pg_ref_9"), SettlementStatus.Pending);

        ContractorInvoice result = await UseCase().ExecuteAsync(invoice.Id, CancellationToken.None);

        Assert.Equal(InvoiceStatus.Paid, result.Status);
        Assert.Equal("pg_ref_9", result.VendorReference);
        Assert.Equal(1, _uow.Commits);
        Assert.Contains(_audit.Entries, e => e.Action == "invoice.paid");
        Assert.Contains(_events.Published, e => e.Type == "invoice.paid");

        // The provider was asked to move exactly the invoice total.
        PaymentInstruction sent = Assert.Single(_payments.Instructions);
        Assert.Equal(Money.Of(200m, Usd), sent.Amount);
    }

    [Fact]
    public async Task An_already_paid_result_reconciles_the_invoice_to_the_existing_reference()
    {
        ContractorInvoice invoice = SeedInvoice(InvoiceStatus.Approved);
        _payments.NextResult = new PaymentResult.AlreadyPaid(new VendorPaymentReference("pg_existing"));

        ContractorInvoice result = await UseCase().ExecuteAsync(invoice.Id, CancellationToken.None);

        Assert.Equal(InvoiceStatus.Paid, result.Status);
        Assert.Equal("pg_existing", result.VendorReference);
    }

    [Fact]
    public async Task A_rejected_result_marks_the_invoice_failed_with_a_neutral_reason()
    {
        ContractorInvoice invoice = SeedInvoice(InvoiceStatus.Approved);
        _payments.NextResult = new PaymentResult.PaymentRejected(
            "Declined by the payment provider (beneficiary blocked).", "REJECTED_07");

        ContractorInvoice result = await UseCase().ExecuteAsync(invoice.Id, CancellationToken.None);

        Assert.Equal(InvoiceStatus.Failed, result.Status);
        Assert.Contains("Declined", result.FailureReason!);
        Assert.Contains(_events.Published, e => e.Type == "invoice.payment_failed");
        Assert.Equal(1, _uow.Commits);
    }

    [Fact]
    public async Task A_vendor_unavailable_result_leaves_the_invoice_untouched_and_is_retryable()
    {
        ContractorInvoice invoice = SeedInvoice(InvoiceStatus.Approved);
        _payments.NextResult = new PaymentResult.VendorUnavailable("rate limited");

        await Assert.ThrowsAsync<PaymentProviderUnavailableException>(() =>
            UseCase().ExecuteAsync(invoice.Id, CancellationToken.None));

        // Nothing was recorded — the invoice is still payable, and the caller may retry later.
        Assert.Equal(InvoiceStatus.Approved, invoice.Status);
        Assert.Equal(0, _uow.Commits);
        Assert.Empty(_events.Published);
    }

    [Fact]
    public async Task An_unapproved_invoice_is_a_422_and_the_provider_is_never_called()
    {
        ContractorInvoice invoice = SeedInvoice(InvoiceStatus.HrApproved); // one approval short

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            UseCase().ExecuteAsync(invoice.Id, CancellationToken.None));

        Assert.Equal(0, _payments.PayCalls);
        Assert.Equal(0, _uow.Commits);
    }

    [Fact]
    public async Task Only_an_admin_may_pay()
    {
        ContractorInvoice invoice = SeedInvoice(InvoiceStatus.Approved);

        var asHr = UseCase(StubContext.OrgUser(OrgId, Role.HrManager, UserId));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            asHr.ExecuteAsync(invoice.Id, CancellationToken.None));
        Assert.Equal(0, _payments.PayCalls);
    }

    [Fact]
    public async Task Paying_an_invoice_in_another_org_is_a_404()
    {
        ContractorInvoice foreign = SeedInvoice(InvoiceStatus.Approved, org: Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(() =>
            UseCase().ExecuteAsync(foreign.Id, CancellationToken.None));
    }
}
