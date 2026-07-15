using Atlas.Application.Engagements;
using Atlas.Application.Errors;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Engagements;

namespace Atlas.Application.Tests.Engagements;

/// <summary>
/// Activation is the sprint's atomicity showcase: one commit spans the engagement's
/// state change, the new Worker, its initial EmploymentRecord, and the audit row.
/// Either an active hire with a worker exists afterwards, or nothing changed at all.
/// </summary>
public class ActivateEngagementTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrgId = Guid.NewGuid();

    private readonly InMemoryEngagementRepository _engagements = new();
    private readonly InMemoryWorkerRepository _workers = new();
    private readonly InMemoryEmploymentRecordRepository _records = new();
    private readonly RecordingAuditTrail _audit = new();
    private readonly RecordingEventSink _events = new();
    private readonly CountingUnitOfWork _uow = new();

    private ActivateEngagement UseCase(StubContext? ctx = null) => new(
        _engagements, _workers, _records, _audit, _events, _uow,
        ctx ?? StubContext.OrgUser(OrgId, Role.HrManager), new FixedClock(Now));

    private Engagement SeedReady()
    {
        var e = TestEngagements.FullyOnboarded(OrgId, Now);
        _engagements.Rows.Add(e);
        return e;
    }

    [Fact]
    public async Task Activation_creates_worker_and_terms_and_audit_in_one_commit()
    {
        var e = SeedReady();

        ActivationOutcome outcome = await UseCase().ExecuteAsync(e.Id, CancellationToken.None);

        // The engagement is live and linked to its worker.
        Assert.Equal(EngagementState.Active, e.State);
        Assert.Equal(outcome.Worker.Id, e.CreatedWorkerId);

        // Worker + initial terms exist, priced from the frozen quote data.
        Assert.Single(_workers.Rows);
        Assert.Single(_records.Rows);
        Assert.Equal(OrgId, outcome.Worker.OrgId);
        Assert.Equal("EUR", outcome.InitialTerms.SalaryCurrency);
        Assert.Equal(60_000m, outcome.InitialTerms.AnnualSalary);

        // One audit row, one event, ONE commit across all four writes.
        Assert.Single(_audit.Entries);
        Assert.Equal("engagement.activated", _audit.Entries[0].Action);
        Assert.Single(_events.Published);
        Assert.Equal("engagement.activated", _events.Published[0].Type);
        Assert.Equal(1, _uow.Commits);
    }

    [Fact]
    public async Task Activation_with_an_incomplete_checklist_creates_nothing()
    {
        // Onboarding started (first item done) but not finished — Activate must 422
        // and leave no worker, no terms, no audit row, no event, no commit.
        var e = TestEngagements.InState(EngagementState.Onboarding, OrgId, Now);
        _engagements.Rows.Add(e);

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            UseCase().ExecuteAsync(e.Id, CancellationToken.None));

        Assert.Equal(EngagementState.Onboarding, e.State);
        Assert.Empty(_workers.Rows);
        Assert.Empty(_records.Rows);
        Assert.Empty(_audit.Entries);
        Assert.Empty(_events.Published);
        Assert.Equal(0, _uow.Commits);
    }

    [Fact]
    public async Task Employees_may_not_activate()
    {
        var e = SeedReady();
        var employee = UseCase(StubContext.OrgUser(OrgId, Role.Employee));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            employee.ExecuteAsync(e.Id, CancellationToken.None));

        Assert.Empty(_workers.Rows);
        Assert.Equal(0, _uow.Commits);
    }
}
