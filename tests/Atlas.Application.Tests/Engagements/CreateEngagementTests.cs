using Atlas.Application.Abstractions;
using Atlas.Application.Engagements;
using Atlas.Application.Errors;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain.Access;
using Atlas.Domain.Countries;
using Atlas.Domain.Engagements;

namespace Atlas.Application.Tests.Engagements;

public class CreateEngagementTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid QuoteId = Guid.NewGuid();

    private readonly InMemoryEngagementRepository _engagements = new();
    private readonly InMemoryQuoteReader _quotes = new();
    private readonly RecordingAuditTrail _audit = new();
    private readonly RecordingEventSink _events = new();
    private readonly CountingUnitOfWork _uow = new();

    private CreateEngagement UseCase(StubContext? ctx = null)
    {
        _quotes.Add(QuoteId, OrgId, new QuoteSnapshot(CountryCode.From("DEU"), 60_000m, "EUR"));
        return new CreateEngagement(
            _engagements, _quotes, _audit, _events, _uow,
            ctx ?? StubContext.OrgUser(OrgId, Role.HrManager), new FixedClock(Now));
    }

    [Fact]
    public async Task Creates_a_quote_state_engagement_with_the_country_checklist()
    {
        Engagement engagement = await UseCase().ExecuteAsync(QuoteId, "Engineer", 40m, CancellationToken.None);

        Assert.Equal(EngagementState.Quote, engagement.State);
        Assert.Equal(QuoteId, engagement.QuoteId);
        Assert.Equal("DEU", engagement.Country.Value);
        Assert.Equal(60_000m, engagement.AnnualSalary);
        Assert.Equal("EUR", engagement.SalaryCurrency);
        Assert.Equal(4, engagement.Checklist.Count); // 3 universal + works-council

        Assert.Single(_engagements.Rows);
        Assert.Single(_audit.Entries);
        Assert.Equal("engagement.created", _audit.Entries[0].Action);
        Assert.Equal(1, _uow.Commits);
    }

    [Fact]
    public async Task Publishes_a_created_event()
    {
        await UseCase().ExecuteAsync(QuoteId, "Engineer", 40m, CancellationToken.None);

        Assert.Single(_events.Published);
        Assert.Equal("engagement.created", _events.Published[0].Type);
        Assert.Equal(OrgId, _events.Published[0].OrgId);
        Assert.Equal("engagement", _events.Published[0].SubjectType);
    }

    [Fact]
    public async Task A_quote_from_another_org_is_not_found()
    {
        // The quote exists — but for a DIFFERENT org, so this caller can't see it.
        var stranger = StubContext.OrgUser(Guid.NewGuid(), Role.HrManager);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            UseCase(stranger).ExecuteAsync(QuoteId, "Engineer", 40m, CancellationToken.None));

        Assert.Empty(_engagements.Rows);
        Assert.Equal(0, _uow.Commits);
    }

    [Fact]
    public async Task Employees_may_not_create_engagements()
    {
        var useCase = UseCase(StubContext.OrgUser(OrgId, Role.Employee));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            useCase.ExecuteAsync(QuoteId, "Engineer", 40m, CancellationToken.None));

        Assert.Empty(_engagements.Rows);
        Assert.Empty(_events.Published);
        Assert.Equal(0, _uow.Commits);
    }
}
