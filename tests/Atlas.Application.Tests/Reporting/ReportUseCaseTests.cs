using Atlas.Application.Errors;
using Atlas.Application.Reporting;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain.Access;
using Atlas.Domain.Compliance;
using Atlas.Domain.Countries;

namespace Atlas.Application.Tests.Reporting;

/// <summary>
/// The report use cases ABOVE the port: they gate on role, validate their params, default their
/// dates, and — for the roster — fold the projected inputs through the same domain evaluator the
/// single-worker view uses. The port is faked; the behaviour under test is the use case's.
/// </summary>
public class ReportUseCaseTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);

    private readonly InMemoryReportQueries _reports = new();
    private readonly FixedClock _clock = new(Now);

    private StubContext Admin => StubContext.OrgUser(Org, Role.OrgAdmin);

    [Fact]
    public async Task Employees_cannot_read_reports()
    {
        var useCase = new GetHeadcountReport(_reports, StubContext.OrgUser(Org, Role.Employee), _clock);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            useCase.ExecuteAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task An_anonymous_caller_is_unauthenticated()
    {
        var useCase = new GetHeadcountReport(_reports, StubContext.Anonymous(), _clock);

        await Assert.ThrowsAsync<UnauthenticatedException>(() =>
            useCase.ExecuteAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task Employment_cost_rejects_a_month_out_of_range()
    {
        var useCase = new GetEmploymentCostReport(_reports, Admin);

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            useCase.ExecuteAsync(2026, 13, CancellationToken.None));
    }

    [Fact]
    public async Task Employment_cost_passes_the_period_through_to_the_read_model()
    {
        var useCase = new GetEmploymentCostReport(_reports, Admin);

        await useCase.ExecuteAsync(2026, 3, CancellationToken.None);

        Assert.Equal((2026, 3), _reports.LastCostRequest);
    }

    [Fact]
    public async Task Renewals_rejects_a_window_out_of_range()
    {
        var useCase = new GetUpcomingRenewals(_reports, Admin, _clock);

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            useCase.ExecuteAsync(0, CancellationToken.None));

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            useCase.ExecuteAsync(GetUpcomingRenewals.MaxWindowDays + 1, CancellationToken.None));
    }

    [Fact]
    public async Task Roster_folds_each_worker_through_the_compliance_evaluator()
    {
        // One fully-documented DEU worker (compliant) and one with nothing (four outstanding).
        _reports.RosterInputs.Add(new WorkerComplianceInput(
            Guid.NewGuid(), "Compliant Cara", CountryCode.From("DEU"),
            [
                WorkerDocument.Record(Org, Guid.NewGuid(), DocumentKind.IdentityProof, "id", new DateOnly(2035, 1, 1), Now),
                WorkerDocument.Record(Org, Guid.NewGuid(), DocumentKind.TaxRegistration, "tax", null, Now),
                WorkerDocument.Record(Org, Guid.NewGuid(), DocumentKind.SocialSecurityRegistration, "ssn", null, Now),
                WorkerDocument.Record(Org, Guid.NewGuid(), DocumentKind.HealthInsuranceProof, "health", null, Now),
            ]));
        _reports.RosterInputs.Add(new WorkerComplianceInput(
            Guid.NewGuid(), "Empty Evan", CountryCode.From("DEU"), []));

        var useCase = new GetComplianceRoster(_reports, Admin, _clock);
        IReadOnlyList<ComplianceRosterRow> roster = await useCase.ExecuteAsync(null, CancellationToken.None);

        // Non-compliant first (the worklist order), then the compliant one.
        Assert.Equal("Empty Evan", roster[0].WorkerName);
        Assert.False(roster[0].IsCompliant);
        Assert.Equal(4, roster[0].Outstanding);

        Assert.Equal("Compliant Cara", roster[1].WorkerName);
        Assert.True(roster[1].IsCompliant);
        Assert.Equal(0, roster[1].Outstanding);
    }
}
