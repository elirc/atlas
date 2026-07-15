using Atlas.Domain;
using Atlas.Domain.Countries;

namespace Atlas.Domain.Tests.Countries;

public class CountryRulePackTests
{
    [Theory]
    [InlineData("DEU", "EUR", 6)]
    [InlineData("GBR", "GBP", 3)]
    [InlineData("PHL", "PHP", 6)]
    [InlineData("USA", "USD", 3)]
    public void Ships_a_pack_for_each_supported_country(string code, string currency, int probationMonths)
    {
        ICountryRulePack pack = CountryRulePacks.For(CountryCode.From(code));

        Assert.Equal(CurrencyCode.From(currency), pack.Currency);
        Assert.Equal(probationMonths, pack.ProbationMonths);
        Assert.NotEmpty(pack.EmployerBurden);
    }

    [Fact]
    public void For_an_unsupported_country_throws()
    {
        // FRA is a well-formed code with no pack yet — a business "no" (422), not a 404.
        Assert.Throws<DomainRuleException>(() => CountryRulePacks.For(CountryCode.From("FRA")));
        Assert.False(CountryRulePacks.TryFor(CountryCode.From("FRA"), out _));
    }

    [Fact]
    public void AssessableBase_applies_the_wage_cap()
    {
        var capped = new BurdenComponent("cap", 0.1m, AnnualWageCap: 100m);

        Assert.Equal(100m, capped.AssessableBase(150m)); // above the ceiling → capped
        Assert.Equal(80m, capped.AssessableBase(80m));   // below the ceiling → full gross
    }

    [Fact]
    public void AssessableBase_applies_the_allowance_and_never_goes_negative()
    {
        var withAllowance = new BurdenComponent("allow", 0.1m, AnnualAllowance: 20m);

        Assert.Equal(130m, withAllowance.AssessableBase(150m)); // gross minus allowance
        Assert.Equal(0m, withAllowance.AssessableBase(10m));    // below allowance → zero
    }

    [Fact]
    public void AssessableBase_combines_cap_then_allowance()
    {
        var both = new BurdenComponent("both", 0.1m, AnnualAllowance: 20m, AnnualWageCap: 100m);

        // min(150, 100) = 100, then − 20 = 80.
        Assert.Equal(80m, both.AssessableBase(150m));
    }

    [Theory]
    // Hand-checked component math (base × rate), the numbers a reviewer can verify by hand:
    [InlineData("DEU", 0, 100_000, 8_425.80)] // pension 9.3% of min(100k, 90,600) = 90,600
    [InlineData("USA", 0, 200_000, 10_453.20)] // social security 6.2% of min(200k, 168,600)
    [InlineData("GBR", 0, 50_000, 5_644.20)]   // employer NI 13.8% of (50,000 − 9,100)
    public void First_burden_component_prices_as_hand_checked(
        string country, int componentIndex, decimal gross, decimal expected)
    {
        ICountryRulePack pack = CountryRulePacks.For(CountryCode.From(country));
        BurdenComponent component = pack.EmployerBurden[componentIndex];

        decimal amount = component.Rate * component.AssessableBase(gross);

        Assert.Equal(expected, amount);
    }
}
