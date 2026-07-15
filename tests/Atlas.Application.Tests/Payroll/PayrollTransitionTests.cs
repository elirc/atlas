using Atlas.Application.Errors;
using Atlas.Application.Payroll;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Payroll;

namespace Atlas.Application.Tests.Payroll;

public class PayrollTransitionTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrgId = Guid.NewGuid();

    private readonly InMemoryPayrollRunRepository _runs = new();
    private readonly RecordingEventSink _events = new();
    private readonly RecordingAuditTrail _audit = new();
    private readonly CountingUnitOfWork _uow = new();

    private static StubContext Admin => StubContext.OrgUser(OrgId, Role.OrgAdmin);

    private PayrollRun SeedRun(Action<PayrollRun>? advance = null)
    {
        var run = PayrollRun.Create(
            OrgId, Guid.NewGuid(), PayPeriod.ForMonth(2026, 3), Guid.NewGuid(), PayrollVendor.AtlasNative, Now);
        advance?.Invoke(run);
        _runs.Rows.Add(run);
        return run;
    }

    private SubmitPayrollRun Submit(StubContext? c = null) =>
        new(_runs, _audit, _uow, c ?? Admin, new FixedClock(Now));

    private ApprovePayrollRun ApproveUseCase(StubContext? c = null) =>
        new(_runs, _audit, _uow, c ?? Admin, new FixedClock(Now));

    private RejectPayrollRun RejectUseCase(StubContext? c = null) =>
        new(_runs, _audit, _uow, c ?? Admin, new FixedClock(Now));

    private ExecutePayrollRun ExecuteUseCase(StubContext? c = null) =>
        new(_runs, _events, _audit, _uow, c ?? Admin, new FixedClock(Now));

    [Fact]
    public async Task Submit_moves_draft_to_in_review_and_audits()
    {
        PayrollRun run = SeedRun();

        await Submit().ExecuteAsync(run.Id, CancellationToken.None);

        Assert.Equal(PayrollRunState.InReview, run.State);
        Assert.Equal(1, _uow.Commits);
        Assert.Contains(_audit.Entries, e => e.Action == "payroll.run_submitted");
    }

    [Fact]
    public async Task Reject_returns_a_reviewed_run_to_draft()
    {
        PayrollRun run = SeedRun(r => r.SubmitForReview());

        await RejectUseCase().ExecuteAsync(run.Id, CancellationToken.None);

        Assert.Equal(PayrollRunState.Draft, run.State);
        Assert.Contains(_audit.Entries, e => e.Action == "payroll.run_rejected");
    }

    [Fact]
    public async Task Execute_finalises_an_approved_run_and_publishes_the_event()
    {
        PayrollRun run = SeedRun(r => { r.SubmitForReview(); r.Approve(); });

        await ExecuteUseCase().ExecuteAsync(run.Id, CancellationToken.None);

        Assert.Equal(PayrollRunState.Executed, run.State);
        Assert.Equal(Now, run.ExecutedAtUtc);
        Assert.Single(_events.Published);
        Assert.Equal("payroll.run.executed", _events.Published[0].Type);
        Assert.Contains(_audit.Entries, e => e.Action == "payroll.run_executed");
    }

    [Fact]
    public async Task Execute_is_idempotent()
    {
        PayrollRun run = SeedRun(r => { r.SubmitForReview(); r.Approve(); });

        await ExecuteUseCase().ExecuteAsync(run.Id, CancellationToken.None);
        PayrollRun again = await ExecuteUseCase().ExecuteAsync(run.Id, CancellationToken.None);

        // Re-executing is a no-op that succeeds: one event, one commit, one audit row.
        Assert.Equal(run.Id, again.Id);
        Assert.Single(_events.Published);
        Assert.Equal(1, _uow.Commits);
        Assert.Single(_audit.Entries);
    }

    [Fact]
    public async Task Executing_a_draft_run_is_a_422_and_writes_nothing()
    {
        PayrollRun run = SeedRun(); // still Draft

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            ExecuteUseCase().ExecuteAsync(run.Id, CancellationToken.None));

        Assert.Equal(PayrollRunState.Draft, run.State);
        Assert.Empty(_events.Published);
        Assert.Equal(0, _uow.Commits);
    }

    [Fact]
    public async Task Approving_a_draft_run_is_a_422()
    {
        PayrollRun run = SeedRun(); // Draft, never submitted

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            ApproveUseCase().ExecuteAsync(run.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Transitions_require_an_org_admin()
    {
        PayrollRun run = SeedRun();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Submit(StubContext.OrgUser(OrgId, Role.Employee)).ExecuteAsync(run.Id, CancellationToken.None));
    }

    [Fact]
    public async Task A_run_in_another_org_is_a_404()
    {
        PayrollRun run = SeedRun();

        var other = Submit(StubContext.OrgUser(Guid.NewGuid(), Role.OrgAdmin));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            other.ExecuteAsync(run.Id, CancellationToken.None));
    }
}
