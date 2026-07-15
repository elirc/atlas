using Atlas.Application.Errors;
using Atlas.Application.Leave;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Countries;
using Atlas.Domain.Leave;
using Atlas.Domain.Workforce;

namespace Atlas.Application.Tests.Leave;

public class ApproveLeaveRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrgId = Guid.NewGuid();

    private readonly InMemoryWorkerRepository _workers = new();
    private readonly InMemoryLeavePolicyRepository _policies = new();
    private readonly InMemoryLeaveRequestRepository _requests = new();
    private readonly RecordingAuditTrail _audit = new();
    private readonly CountingUnitOfWork _uow = new();
    private readonly Worker _worker;

    public ApproveLeaveRequestTests()
    {
        // USA, hired Jan 1, 24 days/year → 2 days accrued per month.
        _worker = Worker.Hire(OrgId, "Maria", EmailAddress.From("m@example.com"),
            CountryCode.From("USA"), new DateOnly(2026, 1, 1), Now);
        _workers.Rows.Add(_worker);
        _policies.Rows.Add(LeavePolicy.Define(
            OrgId, CountryCode.From("USA"), 24m, 10m, statutoryMinimumDays: 0m,
            LeaveAccrualCadence.Monthly, Guid.NewGuid(), Now));
    }

    private ApproveLeaveRequest Approver(StubContext? ctx = null) => new(
        _requests, _policies, _workers, _audit, _uow,
        ctx ?? StubContext.OrgUser(OrgId, Role.HrManager),
        new FixedClock(Now));

    private LeaveRequest Seed(string from, string to, bool approved = false)
    {
        var request = LeaveRequest.Draft(
            _worker.Id, OrgId, DateOnly.Parse(from), DateOnly.Parse(to), null, Guid.NewGuid(), Now);
        request.Submit(Now);
        if (approved)
        {
            request.Approve(Guid.NewGuid(), Now);
        }

        _requests.Rows.Add(request);
        return request;
    }

    [Fact]
    public async Task Approves_when_balance_is_sufficient()
    {
        // Requested for September → asOf its start, Jan..Sep ≈ 18 days accrued, plenty for 5.
        LeaveRequest request = Seed("2026-09-07", "2026-09-11");

        LeaveRequest result = await Approver().ExecuteAsync(request.Id, CancellationToken.None);

        Assert.Equal(LeaveRequestStatus.Approved, result.Status);
        Assert.Equal(1, _uow.Commits);
        Assert.Single(_audit.Entries);
        Assert.Equal("leave.approved", _audit.Entries[0].Action);
    }

    [Fact]
    public async Task Rejects_when_the_balance_is_insufficient()
    {
        // Requested Jan 5–9 → asOf Jan 5 only ~2 days accrued; 5 working days overdraws.
        LeaveRequest request = Seed("2026-01-05", "2026-01-09");

        var ex = await Assert.ThrowsAsync<DomainRuleException>(() =>
            Approver().ExecuteAsync(request.Id, CancellationToken.None));

        Assert.Contains("Insufficient", ex.Message);
        Assert.Equal(0, _uow.Commits);
        Assert.Equal(LeaveRequestStatus.Submitted, request.Status);
    }

    [Fact]
    public async Task Rejects_when_it_overlaps_an_already_approved_request()
    {
        Seed("2026-09-07", "2026-09-11", approved: true);   // already approved
        LeaveRequest clash = Seed("2026-09-09", "2026-09-15"); // overlaps 9th–11th

        var ex = await Assert.ThrowsAsync<DomainRuleException>(() =>
            Approver().ExecuteAsync(clash.Id, CancellationToken.None));

        Assert.Contains("overlaps", ex.Message);
        Assert.Equal(0, _uow.Commits);
    }

    [Fact]
    public async Task Blocks_approval_when_no_policy_exists_for_the_country()
    {
        _policies.Rows.Clear();
        LeaveRequest request = Seed("2026-09-07", "2026-09-11");

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            Approver().ExecuteAsync(request.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Employees_cannot_approve()
    {
        LeaveRequest request = Seed("2026-09-07", "2026-09-11");

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Approver(StubContext.OrgUser(OrgId, Role.Employee))
                .ExecuteAsync(request.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Cannot_approve_a_request_in_another_org()
    {
        LeaveRequest request = Seed("2026-09-07", "2026-09-11");
        var other = StubContext.OrgUser(Guid.NewGuid(), Role.OrgAdmin);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Approver(other).ExecuteAsync(request.Id, CancellationToken.None));
    }
}
