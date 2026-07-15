using Atlas.Application.Errors;
using Atlas.Application.Expenses;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Expenses;

namespace Atlas.Application.Tests.Expenses;

public class ApproveExpenseTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid WorkerId = Guid.NewGuid();

    private readonly InMemoryExpenseClaimRepository _claims = new();
    private readonly RecordingEventSink _events = new();
    private readonly RecordingAuditTrail _audit = new();
    private readonly CountingUnitOfWork _uow = new();

    private ApproveExpense Approver(StubContext? ctx = null) => new(
        _claims, _events, _audit, _uow,
        ctx ?? StubContext.OrgUser(OrgId, Role.HrManager),
        new FixedClock(Now));

    private ExpenseClaim Seed(bool submitted = true)
    {
        var claim = ExpenseClaim.Draft(
            WorkerId, OrgId, Money.Of(240m, CurrencyCode.From("USD")), ExpenseCategory.Travel,
            null, null, null, Guid.NewGuid(), Now);
        if (submitted)
        {
            claim.Submit(Now);
        }

        _claims.Rows.Add(claim);
        return claim;
    }

    [Fact]
    public async Task Approves_a_submitted_claim()
    {
        ExpenseClaim claim = Seed();

        ExpenseClaim result = await Approver().ExecuteAsync(claim.Id, CancellationToken.None);

        Assert.Equal(ExpenseClaimStatus.Approved, result.Status);
        Assert.Equal(1, _uow.Commits);
        Assert.Equal("expense.approved", _audit.Entries[0].Action);
        Assert.Equal("expense.approved", _events.Published[0].Type);
    }

    [Fact]
    public async Task Cannot_approve_a_claim_that_was_not_submitted()
    {
        ExpenseClaim claim = Seed(submitted: false);

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            Approver().ExecuteAsync(claim.Id, CancellationToken.None));
        Assert.Equal(0, _uow.Commits);
    }

    [Fact]
    public async Task Employees_cannot_approve()
    {
        ExpenseClaim claim = Seed();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Approver(StubContext.OrgUser(OrgId, Role.Employee))
                .ExecuteAsync(claim.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Cannot_approve_a_claim_in_another_org()
    {
        ExpenseClaim claim = Seed();
        var other = StubContext.OrgUser(Guid.NewGuid(), Role.OrgAdmin);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Approver(other).ExecuteAsync(claim.Id, CancellationToken.None));
    }
}
