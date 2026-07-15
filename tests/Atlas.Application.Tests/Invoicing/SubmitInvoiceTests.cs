using Atlas.Application.Errors;
using Atlas.Application.Invoicing;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Countries;
using Atlas.Domain.Invoicing;

namespace Atlas.Application.Tests.Invoicing;

public class SubmitInvoiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly InMemoryContractorRepository _contractors = new();
    private readonly InMemoryContractorInvoiceRepository _invoices = new();
    private readonly RecordingAuditTrail _audit = new();
    private readonly RecordingEventSink _events = new();
    private readonly CountingUnitOfWork _uow = new();

    private SubmitInvoice UseCase(StubContext? ctx = null) => new(
        _contractors, _invoices, _audit, _events, _uow,
        ctx ?? StubContext.OrgUser(OrgId, Role.HrManager, UserId),
        new FixedClock(Now));

    private Contractor SeedContractor(string currency = "USD", Guid? org = null)
    {
        var contractor = Contractor.Create(
            org ?? OrgId, "Dana", EmailAddress.From("dana@example.com"),
            CountryCode.From("USA"), CurrencyCode.From(currency), Now);
        _contractors.Rows.Add(contractor);
        return contractor;
    }

    private static IReadOnlyList<NewInvoiceLine> Lines(params NewInvoiceLine[] lines) => lines;

    [Fact]
    public async Task Submits_an_invoice_totals_its_lines_and_announces_it()
    {
        Contractor contractor = SeedContractor();

        ContractorInvoice invoice = await UseCase().ExecuteAsync(
            contractor.Id,
            Lines(new NewInvoiceLine("Design", 100m, "USD"), new NewInvoiceLine("Build", 50m, "USD")),
            CancellationToken.None);

        Assert.Equal(InvoiceStatus.Submitted, invoice.Status);
        Assert.Equal(Money.Of(150m, CurrencyCode.From("USD")), invoice.Total());
        Assert.Single(_invoices.Rows);
        Assert.Equal(1, _uow.Commits);
        Assert.Contains(_audit.Entries, e => e.Action == "invoice.submitted");
        Assert.Contains(_events.Published, e => e.Type == "invoice.submitted");
    }

    [Fact]
    public async Task A_missing_contractor_is_a_404()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => UseCase().ExecuteAsync(
            Guid.NewGuid(), Lines(new NewInvoiceLine("x", 10m, "USD")), CancellationToken.None));
    }

    [Fact]
    public async Task A_contractor_in_another_org_is_a_404()
    {
        Contractor foreign = SeedContractor(org: Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(() => UseCase().ExecuteAsync(
            foreign.Id, Lines(new NewInvoiceLine("x", 10m, "USD")), CancellationToken.None));
    }

    [Fact]
    public async Task An_invoice_with_no_lines_is_a_400()
    {
        Contractor contractor = SeedContractor();

        await Assert.ThrowsAsync<RequestValidationException>(() => UseCase().ExecuteAsync(
            contractor.Id, Lines(), CancellationToken.None));
        Assert.Equal(0, _uow.Commits);
    }

    [Fact]
    public async Task A_line_not_in_the_contractors_payout_currency_is_a_422()
    {
        Contractor contractor = SeedContractor(currency: "USD");

        // The contractor is paid in USD, but a line claims EUR — mixed-currency, rejected.
        await Assert.ThrowsAsync<DomainRuleException>(() => UseCase().ExecuteAsync(
            contractor.Id,
            Lines(new NewInvoiceLine("Design", 100m, "USD"), new NewInvoiceLine("Build", 50m, "EUR")),
            CancellationToken.None));
    }

    [Fact]
    public async Task An_employee_may_not_submit_on_a_contractors_behalf()
    {
        Contractor contractor = SeedContractor();

        var useCase = UseCase(StubContext.OrgUser(OrgId, Role.Employee, UserId));

        await Assert.ThrowsAsync<ForbiddenException>(() => useCase.ExecuteAsync(
            contractor.Id, Lines(new NewInvoiceLine("x", 10m, "USD")), CancellationToken.None));
    }
}
