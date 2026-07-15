using Atlas.Domain;
using Atlas.Domain.Invoicing;

namespace Atlas.Domain.Tests.Invoicing;

public class ContractorInvoiceTests
{
    private static readonly DateTimeOffset When = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly CurrencyCode Usd = CurrencyCode.From("USD");
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ContractorId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    private static InvoiceLine Line(decimal amount, string currency = "USD") =>
        InvoiceLine.For("Consulting", Money.Of(amount, CurrencyCode.From(currency)));

    private static ContractorInvoice DraftInvoice(params InvoiceLine[] lines) =>
        ContractorInvoice.Open(OrgId, ContractorId, Usd, lines, ActorId, When);

    private static ContractorInvoice ApprovedInvoice()
    {
        ContractorInvoice invoice = DraftInvoice(Line(100m), Line(50m));
        invoice.Submit(When);
        invoice.HrApprove(Guid.NewGuid(), When);
        invoice.AdminApprove(Guid.NewGuid(), When);
        return invoice;
    }

    [Fact]
    public void Open_starts_in_draft_and_totals_its_lines()
    {
        ContractorInvoice invoice = DraftInvoice(Line(100.25m), Line(50m));

        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Equal(2, invoice.Lines.Count);
        Assert.Equal(Money.Of(150.25m, Usd), invoice.Total());
    }

    [Fact]
    public void Open_rejects_an_invoice_with_no_lines()
    {
        Assert.Throws<DomainRuleException>(() => DraftInvoice());
    }

    [Fact]
    public void Open_rejects_a_line_in_a_different_currency_than_the_invoice()
    {
        // A EUR line on a USD invoice — a mixed-currency total has no meaning without a rate.
        DomainRuleException ex = Assert.Throws<DomainRuleException>(() =>
            DraftInvoice(Line(100m, "USD"), Line(50m, "EUR")));

        Assert.Contains("currency", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_happy_path_walks_submit_hr_admin_pay()
    {
        ContractorInvoice invoice = DraftInvoice(Line(200m));
        Guid hr = Guid.NewGuid();
        Guid admin = Guid.NewGuid();

        invoice.Submit(When);
        Assert.Equal(InvoiceStatus.Submitted, invoice.Status);

        invoice.HrApprove(hr, When);
        Assert.Equal(InvoiceStatus.HrApproved, invoice.Status);
        Assert.Equal(hr, invoice.HrApprovedByUserId);

        invoice.AdminApprove(admin, When);
        Assert.Equal(InvoiceStatus.Approved, invoice.Status);
        Assert.Equal(admin, invoice.AdminApprovedByUserId);

        invoice.MarkPaid("pg_abc123", When);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal("pg_abc123", invoice.VendorReference);
        Assert.Equal(When, invoice.PaidAtUtc);
    }

    [Fact]
    public void Admin_approve_before_hr_approve_is_illegal()
    {
        ContractorInvoice invoice = DraftInvoice(Line(200m));
        invoice.Submit(When);

        // The order is structural: AdminApprove is only legal from HrApproved.
        Assert.Throws<DomainRuleException>(() => invoice.AdminApprove(Guid.NewGuid(), When));
    }

    [Fact]
    public void An_unapproved_invoice_cannot_be_paid()
    {
        ContractorInvoice invoice = DraftInvoice(Line(200m));
        invoice.Submit(When);

        Assert.Throws<DomainRuleException>(() => invoice.MarkPaid("pg_x", When));
    }

    [Fact]
    public void A_paid_invoice_is_immutable()
    {
        ContractorInvoice invoice = ApprovedInvoice();
        invoice.MarkPaid("pg_first", When);

        // Paid has NO outgoing transition — every further move is a 422.
        Assert.Throws<DomainRuleException>(() => invoice.MarkPaid("pg_second", When));
        Assert.Throws<DomainRuleException>(() => invoice.MarkPaymentFailed("late", When));
        Assert.Throws<DomainRuleException>(() => invoice.HrApprove(Guid.NewGuid(), When));

        // The record of the first payment is intact.
        Assert.Equal("pg_first", invoice.VendorReference);
    }

    [Fact]
    public void A_failed_payment_can_be_retried()
    {
        ContractorInvoice invoice = ApprovedInvoice();

        invoice.MarkPaymentFailed("declined", When);
        Assert.Equal(InvoiceStatus.Failed, invoice.Status);
        Assert.Equal("declined", invoice.FailureReason);

        // Failed keeps its Pay key — a corrected attempt can still succeed.
        invoice.MarkPaid("pg_retry", When.AddMinutes(5));
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal("pg_retry", invoice.VendorReference);
        Assert.Null(invoice.FailureReason);
    }

    [Fact]
    public void MarkPaid_requires_a_vendor_reference()
    {
        ContractorInvoice invoice = ApprovedInvoice();

        Assert.Throws<DomainRuleException>(() => invoice.MarkPaid("  ", When));
    }
}
