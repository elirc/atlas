using Atlas.Application.Errors;
using Atlas.Application.Pagination;
using Atlas.Application.Quotes;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Countries;
using Atlas.Domain.Quotes;

namespace Atlas.Application.Tests.Quotes;

public class QuoteQueriesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);

    private static Quote MakeQuote(Guid orgId, DateTimeOffset at)
    {
        ICountryRulePack pack = CountryRulePacks.For(CountryCode.From("DEU"));
        QuoteBreakdown breakdown = QuoteCalculator.Calculate(pack, Money.Of(50_000m, CurrencyCode.From("EUR")));
        Money display = Money.Of(breakdown.TotalEmployerCost.Amount, CurrencyCode.From("USD"));
        return Quote.FromBreakdown(orgId, CountryCode.From("DEU"), breakdown, display, Guid.NewGuid(), at);
    }

    private static InMemoryQuoteRepository Seeded(params Quote[] quotes)
    {
        var repo = new InMemoryQuoteRepository();
        foreach (Quote q in quotes)
        {
            repo.AddAsync(q, CancellationToken.None).GetAwaiter().GetResult();
        }

        return repo;
    }

    // ── GetQuote ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Owner_reads_their_own_quote()
    {
        Guid orgA = Guid.NewGuid();
        Quote quote = MakeQuote(orgA, T0);
        var useCase = new GetQuote(Seeded(quote), StubContext.OrgUser(orgA, Role.Employee));

        Quote found = await useCase.ExecuteAsync(quote.Id, CancellationToken.None);

        Assert.Same(quote, found);
    }

    [Fact]
    public async Task A_quote_in_another_org_is_not_found()
    {
        Guid orgA = Guid.NewGuid();
        Guid orgB = Guid.NewGuid();
        Quote foreign = MakeQuote(orgB, T0);
        var useCase = new GetQuote(Seeded(foreign), StubContext.OrgUser(orgA, Role.OrgAdmin));

        // Cross-tenant → 404, never 403: existence is need-to-know (ADR-0003).
        await Assert.ThrowsAsync<NotFoundException>(
            () => useCase.ExecuteAsync(foreign.Id, CancellationToken.None));
    }

    [Fact]
    public async Task A_missing_quote_is_not_found()
    {
        Guid orgA = Guid.NewGuid();
        var useCase = new GetQuote(Seeded(), StubContext.OrgUser(orgA, Role.OrgAdmin));

        await Assert.ThrowsAsync<NotFoundException>(
            () => useCase.ExecuteAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Anonymous_callers_cannot_read_a_quote()
    {
        var useCase = new GetQuote(Seeded(), StubContext.Anonymous());

        await Assert.ThrowsAsync<UnauthenticatedException>(
            () => useCase.ExecuteAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Platform_ops_without_an_org_cannot_read_a_quote()
    {
        var useCase = new GetQuote(Seeded(), StubContext.Ops());

        await Assert.ThrowsAsync<ForbiddenException>(
            () => useCase.ExecuteAsync(Guid.NewGuid(), CancellationToken.None));
    }

    // ── ListQuotes ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Lists_newest_first_within_the_org_and_walks_to_exhaustion()
    {
        Guid orgA = Guid.NewGuid();
        Guid orgB = Guid.NewGuid();

        Quote oldest = MakeQuote(orgA, T0);
        Quote middle = MakeQuote(orgA, T0.AddMinutes(1));
        Quote newest = MakeQuote(orgA, T0.AddMinutes(2));
        Quote otherOrg = MakeQuote(orgB, T0.AddMinutes(3)); // newest overall, but not orgA's

        var useCase = new ListQuotes(
            Seeded(oldest, middle, newest, otherOrg), StubContext.OrgUser(orgA, Role.OrgAdmin));

        Page<Quote> first = await useCase.ExecuteAsync(null, 2, CancellationToken.None);

        Assert.Equal([newest.Id, middle.Id], first.Items.Select(q => q.Id));
        Assert.NotNull(first.NextCursor);

        Page<Quote> second = await useCase.ExecuteAsync(first.NextCursor, 2, CancellationToken.None);

        Assert.Equal([oldest.Id], second.Items.Select(q => q.Id));
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task Another_orgs_quotes_never_appear()
    {
        Guid orgA = Guid.NewGuid();
        Guid orgB = Guid.NewGuid();
        var useCase = new ListQuotes(
            Seeded(MakeQuote(orgB, T0), MakeQuote(orgB, T0.AddMinutes(1))),
            StubContext.OrgUser(orgA, Role.OrgAdmin));

        Page<Quote> page = await useCase.ExecuteAsync(null, 25, CancellationToken.None);

        Assert.Empty(page.Items);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task An_out_of_range_limit_is_rejected(int limit)
    {
        Guid orgA = Guid.NewGuid();
        var useCase = new ListQuotes(Seeded(MakeQuote(orgA, T0)), StubContext.OrgUser(orgA, Role.OrgAdmin));

        await Assert.ThrowsAsync<RequestValidationException>(
            () => useCase.ExecuteAsync(null, limit, CancellationToken.None));
    }

    [Fact]
    public async Task A_garbage_cursor_is_rejected()
    {
        Guid orgA = Guid.NewGuid();
        var useCase = new ListQuotes(Seeded(MakeQuote(orgA, T0)), StubContext.OrgUser(orgA, Role.OrgAdmin));

        await Assert.ThrowsAsync<RequestValidationException>(
            () => useCase.ExecuteAsync("???", 10, CancellationToken.None));
    }
}
