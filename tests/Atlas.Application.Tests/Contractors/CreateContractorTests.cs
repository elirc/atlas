using Atlas.Application.Contractors;
using Atlas.Application.Errors;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Countries;

namespace Atlas.Application.Tests.Contractors;

public class CreateContractorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly InMemoryContractorRepository _contractors = new();
    private readonly InMemoryCountryRepository _countries = new(
        new Country(CountryCode.From("USA"), "United States", "USD"));
    private readonly RecordingAuditTrail _audit = new();
    private readonly CountingUnitOfWork _uow = new();

    private CreateContractor UseCase(StubContext? ctx = null) => new(
        _contractors, _countries, _audit, _uow,
        ctx ?? StubContext.OrgUser(OrgId, Role.OrgAdmin, UserId),
        new FixedClock(Now));

    [Fact]
    public async Task Creates_a_contractor_and_writes_one_audit_row_in_one_commit()
    {
        var contractor = await UseCase().ExecuteAsync(
            "Dana Devlin", "dana@example.com", "USA", "USD", CancellationToken.None);

        Assert.Equal("Dana Devlin", contractor.Name);
        Assert.Equal("USD", contractor.PayoutCurrency.Value);
        Assert.Single(_contractors.Rows);
        Assert.Equal(1, _uow.Commits);
        Assert.Contains(_audit.Entries, e => e.Action == "contractor.created");
    }

    [Fact]
    public async Task An_unsupported_country_is_a_422_and_nothing_commits()
    {
        await Assert.ThrowsAsync<DomainRuleException>(() => UseCase().ExecuteAsync(
            "Dana Devlin", "dana@example.com", "DEU", "EUR", CancellationToken.None));

        Assert.Empty(_contractors.Rows);
        Assert.Equal(0, _uow.Commits);
    }

    [Fact]
    public async Task Only_hr_or_admin_may_create_a_contractor()
    {
        var useCase = UseCase(StubContext.OrgUser(OrgId, Role.Employee, UserId));

        await Assert.ThrowsAsync<ForbiddenException>(() => useCase.ExecuteAsync(
            "Dana Devlin", "dana@example.com", "USA", "USD", CancellationToken.None));
        Assert.Equal(0, _uow.Commits);
    }

    [Fact]
    public async Task An_anonymous_caller_is_rejected()
    {
        await Assert.ThrowsAsync<UnauthenticatedException>(() => UseCase(StubContext.Anonymous())
            .ExecuteAsync("Dana Devlin", "dana@example.com", "USA", "USD", CancellationToken.None));
    }
}
