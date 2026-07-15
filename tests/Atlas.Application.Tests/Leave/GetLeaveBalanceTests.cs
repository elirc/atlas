using Atlas.Application.Errors;
using Atlas.Application.Leave;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain.Access;
using Atlas.Domain.Countries;
using Atlas.Domain.Leave;
using Atlas.Domain.Workforce;

namespace Atlas.Application.Tests.Leave;

public class GetLeaveBalanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrgId = Guid.NewGuid();

    private readonly InMemoryWorkerRepository _workers = new();
    private readonly InMemoryLeavePolicyRepository _policies = new();
    private readonly InMemoryLeaveRequestRepository _requests = new();
    private readonly Worker _worker;

    public GetLeaveBalanceTests()
    {
        _worker = Worker.Hire(OrgId, "Maria", EmailAddress.From("m@example.com"),
            CountryCode.From("USA"), new DateOnly(2026, 1, 1), Now);
        _workers.Rows.Add(_worker);
        _policies.Rows.Add(LeavePolicy.Define(
            OrgId, CountryCode.From("USA"), 24m, 10m, statutoryMinimumDays: 0m,
            LeaveAccrualCadence.Monthly, Guid.NewGuid(), Now));
    }

    // Any org member may read a worker's balance (same stance as GetWorkerTerms).
    private GetLeaveBalance UseCase(StubContext? ctx = null) => new(
        _workers, _policies, _requests,
        ctx ?? StubContext.OrgUser(OrgId, Role.Employee),
        new FixedClock(Now));

    private void SeedApproved(string from, string to)
    {
        var request = LeaveRequest.Draft(
            _worker.Id, OrgId, DateOnly.Parse(from), DateOnly.Parse(to), null, Guid.NewGuid(), Now);
        request.Submit(Now);
        request.Approve(Guid.NewGuid(), Now);
        _requests.Rows.Add(request);
    }

    [Fact]
    public async Task Computes_available_as_accrued_minus_approved()
    {
        SeedApproved("2026-09-07", "2026-09-11"); // 5 approved working days

        LeaveBalanceView view = await UseCase().ExecuteAsync(
            _worker.Id, new DateOnly(2026, 7, 1), CancellationToken.None);

        Assert.Equal(14m, view.AccruedDays);   // Jan..Jul × 2 days/month
        Assert.Equal(5m, view.SpentDays);
        Assert.Equal(9m, view.AvailableDays);
        Assert.Equal("USA", view.Country);
    }

    [Fact]
    public async Task Defaults_asOf_to_todays_utc_date()
    {
        LeaveBalanceView view = await UseCase().ExecuteAsync(_worker.Id, null, CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 7, 15), view.AsOf);
        Assert.Equal(14m, view.AccruedDays);   // Jan..Jul × 2
        Assert.Equal(0m, view.SpentDays);
    }

    [Fact]
    public async Task Missing_policy_is_404()
    {
        _policies.Rows.Clear();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            UseCase().ExecuteAsync(_worker.Id, null, CancellationToken.None));
    }

    [Fact]
    public async Task A_worker_in_another_org_is_404()
    {
        var other = StubContext.OrgUser(Guid.NewGuid(), Role.OrgAdmin);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            UseCase(other).ExecuteAsync(_worker.Id, null, CancellationToken.None));
    }
}
