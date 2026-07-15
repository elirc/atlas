using Atlas.Application.Errors;
using Atlas.Application.Leave;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Countries;
using Atlas.Domain.Leave;
using Atlas.Domain.Workforce;

namespace Atlas.Application.Tests.Leave;

public class SubmitLeaveRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid HrUserId = Guid.NewGuid();

    private readonly InMemoryWorkerRepository _workers = new();
    private readonly InMemoryLeaveRequestRepository _requests = new();
    private readonly RecordingAuditTrail _audit = new();
    private readonly CountingUnitOfWork _uow = new();
    private readonly Worker _worker;

    public SubmitLeaveRequestTests()
    {
        _worker = Worker.Hire(OrgId, "Maria", EmailAddress.From("m@example.com"),
            CountryCode.From("USA"), new DateOnly(2026, 1, 1), Now);
        _workers.Rows.Add(_worker);
    }

    private SubmitLeaveRequest UseCase(StubContext? ctx = null) => new(
        _workers, _requests, _audit, _uow,
        ctx ?? StubContext.OrgUser(OrgId, Role.HrManager, HrUserId),
        new FixedClock(Now));

    [Fact]
    public async Task Submits_on_behalf_records_the_requester_and_audits()
    {
        var request = await UseCase().ExecuteAsync(
            _worker.Id, new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11), "Family visit",
            CancellationToken.None);

        Assert.Equal(LeaveRequestStatus.Submitted, request.Status);
        Assert.Equal(HrUserId, request.RequestedByUserId);
        Assert.Equal(5m, request.WorkingDays);
        Assert.Single(_requests.Rows);
        Assert.Equal(1, _uow.Commits);
        Assert.Equal("leave.submitted", _audit.Entries[0].Action);
    }

    [Fact]
    public async Task Employees_cannot_submit_on_behalf()
    {
        // v1: leave is submitted by HR/admin only — worker self-service is deferred.
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            UseCase(StubContext.OrgUser(OrgId, Role.Employee)).ExecuteAsync(
                _worker.Id, new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11), null,
                CancellationToken.None));

        Assert.Empty(_requests.Rows);
    }

    [Fact]
    public async Task Unknown_worker_is_404()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => UseCase().ExecuteAsync(
            Guid.NewGuid(), new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11), null,
            CancellationToken.None));
    }

    [Fact]
    public async Task A_worker_in_another_org_is_404()
    {
        var other = StubContext.OrgUser(Guid.NewGuid(), Role.HrManager);

        await Assert.ThrowsAsync<NotFoundException>(() => UseCase(other).ExecuteAsync(
            _worker.Id, new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11), null,
            CancellationToken.None));
    }

    [Fact]
    public async Task A_weekend_only_range_is_rejected()
    {
        await Assert.ThrowsAsync<DomainRuleException>(() => UseCase().ExecuteAsync(
            _worker.Id, new DateOnly(2026, 8, 8), new DateOnly(2026, 8, 9), null,
            CancellationToken.None));

        Assert.Equal(0, _uow.Commits);
    }
}
