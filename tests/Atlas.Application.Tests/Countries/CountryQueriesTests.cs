using Atlas.Application.Countries;
using Atlas.Application.Errors;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain;
using Atlas.Domain.Countries;

namespace Atlas.Application.Tests.Countries;

public class CountryQueriesTests
{
    private static InMemoryCountryRepository Repo() => new(
        new Country(CountryCode.From("PHL"), "Philippines", "PHP"),
        new Country(CountryCode.From("DEU"), "Germany", "EUR"));

    [Fact]
    public async Task List_returns_countries_ordered_by_code()
    {
        var countries = await new ListCountries(Repo()).ExecuteAsync(CancellationToken.None);

        Assert.Equal(["DEU", "PHL"], countries.Select(c => c.Code.Value));
    }

    [Fact]
    public async Task Get_finds_by_code_case_insensitively()
    {
        var country = await new GetCountry(Repo()).ExecuteAsync("deu", CancellationToken.None);

        Assert.Equal("Germany", country.Name);
    }

    [Fact]
    public async Task Get_unknown_country_raises_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => new GetCountry(Repo()).ExecuteAsync("FRA", CancellationToken.None));
    }

    [Fact]
    public async Task Get_malformed_code_is_a_domain_rule_violation_not_a_404()
    {
        // WHY: "FRANCE" is not a country that's missing — it's a request that's wrong.
        await Assert.ThrowsAsync<DomainRuleException>(
            () => new GetCountry(Repo()).ExecuteAsync("FRANCE", CancellationToken.None));
    }
}
