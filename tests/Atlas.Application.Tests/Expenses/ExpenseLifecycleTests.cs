using Atlas.Application.Errors;
using Atlas.Application.Expenses;
using Atlas.Application.Pagination;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Expenses;

namespace Atlas.Application.Tests.Expenses;

public class ExpenseLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid WorkerId = Guid.NewGuid();

    private readonly InMemoryExpenseClaimRepository _claims = new();
    private readonly RecordingEventSink _events = new();
    private readonly RecordingAuditTrail _audit = new();
    private readonly CountingUnitOfWork _uow = new();

    private DenyExpense Denier(StubContext? ctx = null) => new(
        _claims, _events, _audit, _uow, ctx ?? StubContext.OrgUser(OrgId, Role.HrManager), new FixedClock(Now));

    private CancelExpense Canceller(StubContext? ctx = null) => new(
        _claims, _events, _audit, _uow, ctx ?? StubContext.OrgUser(OrgId, Role.HrManager), new FixedClock(Now));

    private ListExpenses Lister(StubContext? ctx = null) => new(
        _claims, ctx ?? StubContext.OrgUser(OrgId, Role.HrManager));

    private ExpenseClaim SeedSubmitted(Guid? orgId = null)
    {
        var claim = ExpenseClaim.Draft(
            WorkerId, orgId ?? OrgId, Money.Of(120m, CurrencyCode.From("USD")),
            ExpenseCategory.Meals, null, null, null, Guid.NewGuid(), Now);
        claim.Submit(Now);
        _claims.Rows.Add(claim);
        return claim;
    }

    [Fact]
    public async Task Deny_records_the_reason_and_audits_and_publishes()
    {
        ExpenseClaim claim = SeedSubmitted();

        ExpenseClaim result = await Denier().ExecuteAsync(
            claim.Id, "Missing itemised receipt.", CancellationToken.None);

        Assert.Equal(ExpenseClaimStatus.Denied, result.Status);
        Assert.Equal("Missing itemised receipt.", result.DecisionReason);
        Assert.Equal("expense.denied", _audit.Entries[0].Action);
        Assert.Equal("expense.denied", _events.Published[0].Type);
    }

    [Fact]
    public async Task Cancel_moves_to_cancelled()
    {
        ExpenseClaim claim = SeedSubmitted();

        ExpenseClaim result = await Canceller().ExecuteAsync(claim.Id, CancellationToken.None);

        Assert.Equal(ExpenseClaimStatus.Cancelled, result.Status);
        Assert.Equal("expense.cancelled", _audit.Entries[0].Action);
    }

    [Fact]
    public async Task Cannot_cancel_a_denied_claim()
    {
        ExpenseClaim claim = SeedSubmitted();
        await Denier().ExecuteAsync(claim.Id, null, CancellationToken.None);

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            Canceller().ExecuteAsync(claim.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Deny_in_another_org_is_404()
    {
        ExpenseClaim claim = SeedSubmitted();
        var other = StubContext.OrgUser(Guid.NewGuid(), Role.OrgAdmin);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Denier(other).ExecuteAsync(claim.Id, null, CancellationToken.None));
    }

    [Fact]
    public async Task List_is_org_scoped()
    {
        SeedSubmitted();
        SeedSubmitted(orgId: Guid.NewGuid()); // a foreign org's claim must not appear

        Page<ExpenseClaim> page = await Lister().ExecuteAsync(null, null, CancellationToken.None);

        Assert.Single(page.Items);
        Assert.All(page.Items, c => Assert.Equal(OrgId, c.OrgId));
    }

    [Fact]
    public async Task List_rejects_an_out_of_range_limit()
    {
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            Lister().ExecuteAsync(null, 0, CancellationToken.None));

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            Lister().ExecuteAsync(null, 101, CancellationToken.None));
    }
}
