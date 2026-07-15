using Atlas.Application.Errors;
using Atlas.Application.Leave;
using Atlas.Application.Pagination;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Leave;

namespace Atlas.Application.Tests.Leave;

public class LeaveLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid WorkerId = Guid.NewGuid();

    private readonly InMemoryLeaveRequestRepository _requests = new();
    private readonly RecordingAuditTrail _audit = new();
    private readonly CountingUnitOfWork _uow = new();

    private LeaveRequest SeedSubmitted(string from = "2026-09-07", string to = "2026-09-11")
    {
        var request = LeaveRequest.Draft(
            WorkerId, OrgId, DateOnly.Parse(from), DateOnly.Parse(to), null, Guid.NewGuid(), Now);
        request.Submit(Now);
        _requests.Rows.Add(request);
        return request;
    }

    private DenyLeaveRequest Denier(StubContext? ctx = null) => new(
        _requests, _audit, _uow, ctx ?? StubContext.OrgUser(OrgId, Role.HrManager), new FixedClock(Now));

    private CancelLeaveRequest Canceller(StubContext? ctx = null) => new(
        _requests, _audit, _uow, ctx ?? StubContext.OrgUser(OrgId, Role.HrManager), new FixedClock(Now));

    private ListLeaveRequests Lister(StubContext? ctx = null) => new(
        _requests, ctx ?? StubContext.OrgUser(OrgId, Role.HrManager));

    [Fact]
    public async Task Deny_moves_to_denied_and_audits()
    {
        LeaveRequest request = SeedSubmitted();

        LeaveRequest result = await Denier().ExecuteAsync(request.Id, CancellationToken.None);

        Assert.Equal(LeaveRequestStatus.Denied, result.Status);
        Assert.Equal(1, _uow.Commits);
        Assert.Equal("leave.denied", _audit.Entries[0].Action);
    }

    [Fact]
    public async Task Cancel_moves_to_cancelled_and_audits()
    {
        LeaveRequest request = SeedSubmitted();

        LeaveRequest result = await Canceller().ExecuteAsync(request.Id, CancellationToken.None);

        Assert.Equal(LeaveRequestStatus.Cancelled, result.Status);
        Assert.Equal("leave.cancelled", _audit.Entries[0].Action);
    }

    [Fact]
    public async Task Cannot_cancel_a_denied_request()
    {
        LeaveRequest request = SeedSubmitted();
        await Denier().ExecuteAsync(request.Id, CancellationToken.None);

        // The entity's terminal-state guard surfaces as a DomainRuleException (422).
        await Assert.ThrowsAsync<DomainRuleException>(() =>
            Canceller().ExecuteAsync(request.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Deny_in_another_org_is_404()
    {
        LeaveRequest request = SeedSubmitted();
        var other = StubContext.OrgUser(Guid.NewGuid(), Role.OrgAdmin);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Denier(other).ExecuteAsync(request.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Employees_cannot_deny()
    {
        LeaveRequest request = SeedSubmitted();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Denier(StubContext.OrgUser(OrgId, Role.Employee))
                .ExecuteAsync(request.Id, CancellationToken.None));
    }

    [Fact]
    public async Task List_is_org_scoped()
    {
        SeedSubmitted();
        // A request belonging to a different org must not appear.
        var foreign = LeaveRequest.Draft(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11),
            null, Guid.NewGuid(), Now);
        foreign.Submit(Now);
        _requests.Rows.Add(foreign);

        Page<LeaveRequest> page = await Lister().ExecuteAsync(null, null, CancellationToken.None);

        Assert.Single(page.Items);
        Assert.All(page.Items, r => Assert.Equal(OrgId, r.OrgId));
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
