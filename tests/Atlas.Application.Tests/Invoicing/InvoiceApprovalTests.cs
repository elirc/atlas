using Atlas.Application.Errors;
using Atlas.Application.Invoicing;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Invoicing;

namespace Atlas.Application.Tests.Invoicing;

/// <summary>The two-step approval, at the use-case level: who may do each step, and in what order.</summary>
public class InvoiceApprovalTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly CurrencyCode Usd = CurrencyCode.From("USD");
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly InMemoryContractorInvoiceRepository _invoices = new();
    private readonly RecordingAuditTrail _audit = new();
    private readonly RecordingEventSink _events = new();
    private readonly CountingUnitOfWork _uow = new();

    private HrApproveInvoice Hr(StubContext? ctx = null) => new(
        _invoices, _audit, _events, _uow,
        ctx ?? StubContext.OrgUser(OrgId, Role.HrManager, UserId), new FixedClock(Now));

    private AdminApproveInvoice Admin(StubContext? ctx = null) => new(
        _invoices, _audit, _events, _uow,
        ctx ?? StubContext.OrgUser(OrgId, Role.OrgAdmin, UserId), new FixedClock(Now));

    private ContractorInvoice Seed(InvoiceStatus upTo, Guid? org = null)
    {
        var invoice = ContractorInvoice.Open(
            org ?? OrgId, Guid.NewGuid(), Usd,
            [InvoiceLine.For("Work", Money.Of(100m, Usd))], UserId, Now);

        if (upTo >= InvoiceStatus.Submitted)
        {
            invoice.Submit(Now);
        }

        if (upTo >= InvoiceStatus.HrApproved)
        {
            invoice.HrApprove(Guid.NewGuid(), Now);
        }

        _invoices.Rows.Add(invoice);
        return invoice;
    }

    [Fact]
    public async Task Hr_approve_moves_submitted_to_hr_approved_and_records_the_approver()
    {
        ContractorInvoice submitted = Seed(InvoiceStatus.Submitted);

        ContractorInvoice result = await Hr().ExecuteAsync(submitted.Id, CancellationToken.None);

        Assert.Equal(InvoiceStatus.HrApproved, result.Status);
        Assert.Equal(UserId, result.HrApprovedByUserId);
        Assert.Contains(_audit.Entries, e => e.Action == "invoice.hr_approved");
        Assert.Equal(1, _uow.Commits);
    }

    [Fact]
    public async Task Admin_approve_moves_hr_approved_to_approved()
    {
        ContractorInvoice hrApproved = Seed(InvoiceStatus.HrApproved);

        ContractorInvoice result = await Admin().ExecuteAsync(hrApproved.Id, CancellationToken.None);

        Assert.Equal(InvoiceStatus.Approved, result.Status);
        Assert.Equal(UserId, result.AdminApprovedByUserId);
        Assert.Contains(_events.Published, e => e.Type == "invoice.approved");
    }

    [Fact]
    public async Task Admin_approve_before_hr_approve_is_a_422()
    {
        ContractorInvoice submitted = Seed(InvoiceStatus.Submitted);

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            Admin().ExecuteAsync(submitted.Id, CancellationToken.None));
    }

    [Fact]
    public async Task An_hr_manager_may_not_do_the_admin_step()
    {
        ContractorInvoice hrApproved = Seed(InvoiceStatus.HrApproved);

        var asHr = Admin(StubContext.OrgUser(OrgId, Role.HrManager, UserId));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            asHr.ExecuteAsync(hrApproved.Id, CancellationToken.None));
    }

    [Fact]
    public async Task An_employee_may_not_hr_approve()
    {
        ContractorInvoice submitted = Seed(InvoiceStatus.Submitted);

        var asEmployee = Hr(StubContext.OrgUser(OrgId, Role.Employee, UserId));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            asEmployee.ExecuteAsync(submitted.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Approving_an_invoice_in_another_org_is_a_404()
    {
        ContractorInvoice foreign = Seed(InvoiceStatus.Submitted, org: Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Hr().ExecuteAsync(foreign.Id, CancellationToken.None));
    }
}
