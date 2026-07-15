using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Application.Quotes;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Countries;
using Atlas.Domain.Quotes;

namespace Atlas.Application.Tests.Quotes;

public class CreateQuoteTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrgA = Guid.NewGuid();
    private static readonly CurrencyCode Eur = CurrencyCode.From("EUR");
    private static readonly CurrencyCode Usd = CurrencyCode.From("USD");

    private sealed record Harness(
        CreateQuote UseCase,
        InMemoryQuoteRepository Quotes,
        RecordingAuditTrail Audit,
        CountingUnitOfWork Uow);

    private static Harness Build(ICurrentContext context, IFxRates? fx = null)
    {
        var quotes = new InMemoryQuoteRepository();
        var audit = new RecordingAuditTrail();
        var uow = new CountingUnitOfWork();
        var countries = new InMemoryCountryRepository(
            new Country(CountryCode.From("DEU"), "Germany", "EUR"),
            new Country(CountryCode.From("USA"), "United States", "USD"));

        var useCase = new CreateQuote(
            quotes, countries, fx ?? new IdentityFxRates(), audit, uow, context, new FixedClock(T0));

        return new Harness(useCase, quotes, audit, uow);
    }

    [Fact]
    public async Task Org_admin_creates_a_quote_stores_it_and_audits_in_one_commit()
    {
        Harness h = Build(StubContext.OrgUser(OrgA, Role.OrgAdmin));

        Quote quote = await h.UseCase.ExecuteAsync("DEU", 60_000m, CancellationToken.None);

        Assert.Equal(OrgA, quote.OrgId);
        Assert.Equal(CountryCode.From("DEU"), quote.Country);
        Assert.Equal(Eur, quote.Gross.Currency);
        Assert.Equal(Money.Of(78_960.00m, Eur), quote.TotalEmployerCost);
        Assert.NotEmpty(quote.Lines);

        Assert.Same(quote, Assert.Single(h.Quotes.Rows));

        // The quote row AND its audit row commit exactly once, together.
        Assert.Equal(1, h.Uow.Commits);
        var entry = Assert.Single(h.Audit.Entries);
        Assert.Equal("quote.created", entry.Action);
        Assert.Equal("quote", entry.SubjectType);
        Assert.Equal(OrgA, entry.OrgId);
        Assert.Equal(quote.Id, entry.SubjectId);
    }

    [Fact]
    public async Task The_employer_total_is_converted_into_the_org_display_currency()
    {
        // FixedFxRates(2) doubles on any cross-currency conversion: EUR total → USD × 2.
        Harness h = Build(StubContext.OrgUser(OrgA, Role.OrgAdmin), new FixedFxRates(2m));

        Quote quote = await h.UseCase.ExecuteAsync("DEU", 60_000m, CancellationToken.None);

        Assert.Equal(Money.Of(78_960.00m, Eur), quote.TotalEmployerCost); // local, unchanged
        Assert.Equal(Usd, quote.DisplayTotal.Currency);                   // converted to display ccy
        Assert.Equal(157_920.00m, quote.DisplayTotal.Amount);            // 78,960 × 2
    }

    [Theory]
    [InlineData(Role.OrgAdmin)]
    [InlineData(Role.HrManager)]
    public async Task Org_admins_and_hr_managers_may_quote(Role role)
    {
        Harness h = Build(StubContext.OrgUser(OrgA, role));

        Quote quote = await h.UseCase.ExecuteAsync("USA", 120_000m, CancellationToken.None);

        Assert.Equal(1, h.Uow.Commits);
        Assert.Equal(Usd, quote.Gross.Currency);
    }

    [Theory]
    [InlineData(Role.Employee)]
    [InlineData(Role.Contractor)]
    public async Task Employees_and_contractors_are_forbidden(Role role)
    {
        Harness h = Build(StubContext.OrgUser(OrgA, role));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => h.UseCase.ExecuteAsync("DEU", 60_000m, CancellationToken.None));

        Assert.Equal(0, h.Uow.Commits);
        Assert.Empty(h.Quotes.Rows);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Anonymous_callers_are_unauthenticated()
    {
        Harness h = Build(StubContext.Anonymous());

        await Assert.ThrowsAsync<UnauthenticatedException>(
            () => h.UseCase.ExecuteAsync("DEU", 60_000m, CancellationToken.None));

        Assert.Equal(0, h.Uow.Commits);
    }

    [Fact]
    public async Task Platform_ops_without_an_org_are_forbidden()
    {
        // Ops passes the role gate (staff bypass it) but has no org to own the quote.
        Harness h = Build(StubContext.Ops());

        await Assert.ThrowsAsync<ForbiddenException>(
            () => h.UseCase.ExecuteAsync("DEU", 60_000m, CancellationToken.None));

        Assert.Equal(0, h.Uow.Commits);
    }

    [Fact]
    public async Task An_unsupported_country_is_rejected_and_nothing_is_written()
    {
        Harness h = Build(StubContext.OrgUser(OrgA, Role.OrgAdmin));

        // FRA is well-formed but not a country Atlas employs in yet.
        await Assert.ThrowsAsync<DomainRuleException>(
            () => h.UseCase.ExecuteAsync("FRA", 60_000m, CancellationToken.None));

        Assert.Equal(0, h.Uow.Commits);
        Assert.Empty(h.Quotes.Rows);
        Assert.Empty(h.Audit.Entries);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public async Task A_non_positive_gross_is_rejected(decimal gross)
    {
        Harness h = Build(StubContext.OrgUser(OrgA, Role.OrgAdmin));

        await Assert.ThrowsAsync<DomainRuleException>(
            () => h.UseCase.ExecuteAsync("DEU", gross, CancellationToken.None));

        Assert.Equal(0, h.Uow.Commits);
    }

    [Fact]
    public async Task A_malformed_country_code_is_rejected()
    {
        Harness h = Build(StubContext.OrgUser(OrgA, Role.OrgAdmin));

        await Assert.ThrowsAsync<DomainRuleException>(
            () => h.UseCase.ExecuteAsync("GERMANY", 60_000m, CancellationToken.None));
    }
}
