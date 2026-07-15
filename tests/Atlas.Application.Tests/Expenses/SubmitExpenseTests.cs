using Atlas.Application.Errors;
using Atlas.Application.Expenses;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Countries;
using Atlas.Domain.Expenses;
using Atlas.Domain.Workforce;

namespace Atlas.Application.Tests.Expenses;

public class SubmitExpenseTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrgId = Guid.NewGuid();

    private readonly InMemoryWorkerRepository _workers = new();
    private readonly InMemoryExpenseClaimRepository _claims = new();
    private readonly RecordingEventSink _events = new();
    private readonly RecordingAuditTrail _audit = new();
    private readonly CountingUnitOfWork _uow = new();
    private readonly Worker _worker;

    public SubmitExpenseTests()
    {
        _worker = Worker.Hire(OrgId, "Maria", EmailAddress.From("m@example.com"),
            CountryCode.From("USA"), new DateOnly(2026, 1, 1), Now);
        _workers.Rows.Add(_worker);
    }

    private SubmitExpense Submitter(StubContext? ctx = null) => new(
        _workers, _claims, _events, _audit, _uow,
        ctx ?? StubContext.OrgUser(OrgId, Role.HrManager),
        new FixedClock(Now));

    [Fact]
    public async Task Submits_a_claim_and_audits_and_publishes()
    {
        ExpenseClaim claim = await Submitter().ExecuteAsync(
            _worker.Id, 240m, "USD", "Travel", "https://receipts.example/a", "taxi.pdf", null,
            CancellationToken.None);

        Assert.Equal(ExpenseClaimStatus.Submitted, claim.Status);
        Assert.Equal(1, _uow.Commits);
        Assert.Single(_claims.Rows);
        Assert.Equal("expense.submitted", _audit.Entries[0].Action);
        Assert.Equal("expense.submitted", _events.Published[0].Type);
    }

    [Fact]
    public async Task Rejects_an_unknown_category_as_a_400()
    {
        await Assert.ThrowsAsync<RequestValidationException>(() => Submitter().ExecuteAsync(
            _worker.Id, 240m, "USD", "Sorcery", null, null, null, CancellationToken.None));

        Assert.Equal(0, _uow.Commits);
    }

    [Fact]
    public async Task Rejects_a_non_positive_amount_as_a_422()
    {
        await Assert.ThrowsAsync<DomainRuleException>(() => Submitter().ExecuteAsync(
            _worker.Id, 0m, "USD", "Travel", null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Rejects_a_malformed_currency_as_a_422()
    {
        await Assert.ThrowsAsync<DomainRuleException>(() => Submitter().ExecuteAsync(
            _worker.Id, 240m, "dollars", "Travel", null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task A_missing_worker_is_a_404()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Submitter().ExecuteAsync(
            Guid.NewGuid(), 240m, "USD", "Travel", null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Employees_cannot_submit()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Submitter(StubContext.OrgUser(OrgId, Role.Employee)).ExecuteAsync(
                _worker.Id, 240m, "USD", "Travel", null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Cannot_submit_for_a_worker_in_another_org()
    {
        var other = StubContext.OrgUser(Guid.NewGuid(), Role.OrgAdmin);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Submitter(other).ExecuteAsync(
                _worker.Id, 240m, "USD", "Travel", null, null, null, CancellationToken.None));
    }
}
