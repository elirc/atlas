using Atlas.Application.Engagements;
using Atlas.Application.Errors;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Engagements;

namespace Atlas.Application.Tests.Engagements;

/// <summary>
/// The non-activation transitions: send-offer, accept-offer, complete-item, withdraw.
/// Each asserts the same contract — one state change, one audit row, one event,
/// one commit — plus the idempotency of a retried item completion.
/// </summary>
public class EngagementTransitionUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrgId = Guid.NewGuid();

    private readonly InMemoryEngagementRepository _engagements = new();
    private readonly RecordingAuditTrail _audit = new();
    private readonly RecordingEventSink _events = new();
    private readonly CountingUnitOfWork _uow = new();

    private Engagement Seed(EngagementState state)
    {
        var engagement = TestEngagements.InState(state, OrgId, Now);
        _engagements.Rows.Add(engagement);
        return engagement;
    }

    private StubContext Hr => StubContext.OrgUser(OrgId, Role.HrManager);

    private SendOffer SendOffer(StubContext? ctx = null) =>
        new(_engagements, _audit, _events, _uow, ctx ?? Hr, new FixedClock(Now));

    private AcceptOffer AcceptOffer() =>
        new(_engagements, _audit, _events, _uow, Hr, new FixedClock(Now));

    private CompleteOnboardingItem CompleteItem() =>
        new(_engagements, _audit, _events, _uow, Hr, new FixedClock(Now));

    private WithdrawEngagement Withdraw() =>
        new(_engagements, _audit, _events, _uow, Hr, new FixedClock(Now));

    // ── send offer ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Send_offer_transitions_and_writes_one_audit_and_one_event()
    {
        var e = Seed(EngagementState.Quote);

        await SendOffer().ExecuteAsync(e.Id, "Maria Santos", "maria@example.com", CancellationToken.None);

        Assert.Equal(EngagementState.Offer, e.State);
        Assert.Single(_audit.Entries);
        Assert.Equal("engagement.offer_sent", _audit.Entries[0].Action);
        Assert.Single(_events.Published);
        Assert.Equal("engagement.offer_sent", _events.Published[0].Type);
        Assert.Equal(1, _uow.Commits);
    }

    [Fact]
    public async Task Send_offer_requires_hr_or_admin()
    {
        var e = Seed(EngagementState.Quote);
        var employee = SendOffer(StubContext.OrgUser(OrgId, Role.Employee));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            employee.ExecuteAsync(e.Id, "Maria", "maria@example.com", CancellationToken.None));

        Assert.Empty(_events.Published);
        Assert.Equal(0, _uow.Commits);
    }

    [Fact]
    public async Task An_engagement_in_another_org_is_not_found()
    {
        var e = Seed(EngagementState.Quote);
        var stranger = SendOffer(StubContext.OrgUser(Guid.NewGuid(), Role.HrManager));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            stranger.ExecuteAsync(e.Id, "Maria", "maria@example.com", CancellationToken.None));
    }

    // ── accept offer ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Accept_offer_transitions_to_contract()
    {
        var e = Seed(EngagementState.Offer);

        await AcceptOffer().ExecuteAsync(e.Id, new DateOnly(2026, 8, 1), CancellationToken.None);

        Assert.Equal(EngagementState.Contract, e.State);
        Assert.Equal("engagement.offer_accepted", _events.Published.Single().Type);
        Assert.Equal(1, _uow.Commits);
    }

    [Fact]
    public async Task An_illegal_transition_throws_and_writes_nothing()
    {
        // Accepting an offer that was never sent (still in Quote) is a 422 — and it
        // must leave NO activity row behind (the exercise in the playbook).
        var e = Seed(EngagementState.Quote);

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            AcceptOffer().ExecuteAsync(e.Id, new DateOnly(2026, 8, 1), CancellationToken.None));

        Assert.Empty(_audit.Entries);
        Assert.Empty(_events.Published);
        Assert.Equal(0, _uow.Commits);
    }

    // ── complete onboarding item (idempotency) ───────────────────────────────────

    [Fact]
    public async Task Completing_the_first_item_starts_onboarding_and_records_it()
    {
        var e = Seed(EngagementState.Contract);
        string item = e.Checklist[0].Name;

        await CompleteItem().ExecuteAsync(e.Id, item, CancellationToken.None);

        Assert.Equal(EngagementState.Onboarding, e.State);
        Assert.Equal("engagement.onboarding_item_completed", _audit.Entries.Single().Action);
        Assert.Single(_events.Published);
        Assert.Equal(1, _uow.Commits);
    }

    [Fact]
    public async Task Retrying_a_completed_item_writes_no_second_activity_row()
    {
        var e = Seed(EngagementState.Contract);
        string item = e.Checklist[0].Name;

        await CompleteItem().ExecuteAsync(e.Id, item, CancellationToken.None);
        await CompleteItem().ExecuteAsync(e.Id, item, CancellationToken.None); // retry

        // Idempotent: still one audit row, one event, one commit — not two.
        Assert.Single(_audit.Entries);
        Assert.Single(_events.Published);
        Assert.Equal(1, _uow.Commits);
    }

    // ── withdraw ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Withdraw_from_contract_transitions_and_records()
    {
        var e = Seed(EngagementState.Contract);

        await Withdraw().ExecuteAsync(e.Id, "Candidate took another role", CancellationToken.None);

        Assert.Equal(EngagementState.Withdrawn, e.State);
        Assert.Equal("engagement.withdrawn", _events.Published.Single().Type);
        Assert.Equal(1, _uow.Commits);
    }

    [Fact]
    public async Task Withdraw_after_onboarding_started_is_rejected()
    {
        var e = Seed(EngagementState.Onboarding);

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            Withdraw().ExecuteAsync(e.Id, "too late", CancellationToken.None));

        Assert.Equal(EngagementState.Onboarding, e.State);
        Assert.Equal(0, _uow.Commits);
    }
}
