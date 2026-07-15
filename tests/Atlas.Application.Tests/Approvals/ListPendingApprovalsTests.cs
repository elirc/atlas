using Atlas.Application.Approvals;
using Atlas.Application.Errors;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Expenses;
using Atlas.Domain.Leave;

namespace Atlas.Application.Tests.Approvals;

/// <summary>
/// The cross-type approval inbox — one worklist over BOTH leave and expenses. This test only makes
/// sense because the two aggregates share the approval lifecycle: it is the extraction's payoff.
/// </summary>
public class ListPendingApprovalsTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid WorkerId = Guid.NewGuid();

    private readonly InMemoryLeaveRequestRepository _leave = new();
    private readonly InMemoryExpenseClaimRepository _expenses = new();

    private ListPendingApprovals Inbox(StubContext? ctx = null) => new(
        _leave, _expenses, ctx ?? StubContext.OrgUser(OrgId, Role.HrManager));

    private void SeedSubmittedLeave(Guid org, DateTimeOffset submittedAt)
    {
        var request = LeaveRequest.Draft(
            WorkerId, org, new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11), null, Guid.NewGuid(), submittedAt);
        request.Submit(submittedAt);
        _leave.Rows.Add(request);
    }

    private void SeedSubmittedExpense(Guid org, DateTimeOffset submittedAt)
    {
        var claim = ExpenseClaim.Draft(
            WorkerId, org, Money.Of(240m, CurrencyCode.From("USD")), ExpenseCategory.Travel,
            null, null, null, Guid.NewGuid(), submittedAt);
        claim.Submit(submittedAt);
        _expenses.Rows.Add(claim);
    }

    [Fact]
    public async Task Merges_pending_leave_and_expenses_newest_first()
    {
        SeedSubmittedLeave(OrgId, Now);
        SeedSubmittedExpense(OrgId, Now.AddMinutes(5)); // submitted later → should sort first

        IReadOnlyList<PendingApproval> pending =
            await Inbox().ExecuteAsync(null, CancellationToken.None);

        Assert.Equal(2, pending.Count);
        Assert.Equal("expense", pending[0].Kind);
        Assert.Equal("leave", pending[1].Kind);
        Assert.Contains("240.00 USD", pending[0].Summary);
        Assert.Contains("working days", pending[1].Summary);
    }

    [Fact]
    public async Task Only_submitted_items_appear()
    {
        // An approved expense and a draft-then-approved leave request are NOT pending.
        var approved = ExpenseClaim.Draft(
            WorkerId, OrgId, Money.Of(50m, CurrencyCode.From("USD")), ExpenseCategory.Meals,
            null, null, null, Guid.NewGuid(), Now);
        approved.Submit(Now);
        approved.Approve(Guid.NewGuid(), Now);
        _expenses.Rows.Add(approved);

        SeedSubmittedExpense(OrgId, Now);

        IReadOnlyList<PendingApproval> pending =
            await Inbox().ExecuteAsync(null, CancellationToken.None);

        Assert.Single(pending);
    }

    [Fact]
    public async Task Is_org_scoped_across_both_types()
    {
        SeedSubmittedLeave(OrgId, Now);
        SeedSubmittedExpense(OrgId, Now);
        SeedSubmittedLeave(Guid.NewGuid(), Now);     // foreign org
        SeedSubmittedExpense(Guid.NewGuid(), Now);   // foreign org

        IReadOnlyList<PendingApproval> pending =
            await Inbox().ExecuteAsync(null, CancellationToken.None);

        Assert.Equal(2, pending.Count);
    }

    [Fact]
    public async Task Employees_cannot_read_the_inbox()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Inbox(StubContext.OrgUser(OrgId, Role.Employee)).ExecuteAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task Rejects_an_out_of_range_limit()
    {
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            Inbox().ExecuteAsync(0, CancellationToken.None));

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            Inbox().ExecuteAsync(101, CancellationToken.None));
    }
}
