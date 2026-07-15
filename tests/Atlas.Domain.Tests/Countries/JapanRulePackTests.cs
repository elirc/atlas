using Atlas.Domain;
using Atlas.Domain.Countries;
using Atlas.Domain.Quotes;
using Xunit;

namespace Atlas.Domain.Tests.Countries;

/// <summary>
/// The payoff of the code-bearing hook: Japan's flat family levy is a conditional the data shape
/// cannot express, and it flows through the UNCHANGED calculators purely because the pack overrides
/// one method. Canada, added the same sprint, overrides nothing — the counter-proof that most
/// countries stay data.
/// </summary>
public class JapanRulePackTests
{
    private static readonly CountryCode Jpn = CountryCode.From("JPN");
    private static readonly CountryCode Can = CountryCode.From("CAN");

    [Fact]
    public void The_family_levy_applies_only_above_the_earnings_floor()
    {
        ICountryRulePack jpn = CountryRulePacks.For(Jpn);

        Assert.Empty(jpn.AdditionalEmployerBurden(2_000_000m));           // below floor → no levy
        var above = jpn.AdditionalEmployerBurden(3_000_000m);            // above floor → one flat line
        Assert.Single(above);
        Assert.Equal(12_000m, above[0].AnnualAmount);
        Assert.Contains("levy", above[0].Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_quote_prices_the_levy_for_a_high_earner_and_omits_it_for_a_low_earner()
    {
        ICountryRulePack jpn = CountryRulePacks.For(Jpn);
        var jpy = CurrencyCode.From("JPY");

        QuoteBreakdown high = QuoteCalculator.Calculate(jpn, Money.Of(5_000_000m, jpy));
        Assert.Contains(high.Lines, l => l.Label.Contains("levy", StringComparison.OrdinalIgnoreCase));

        QuoteBreakdown low = QuoteCalculator.Calculate(jpn, Money.Of(1_800_000m, jpy));
        Assert.DoesNotContain(low.Lines, l => l.Label.Contains("levy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Canada_is_pure_data_and_overrides_no_hook()
    {
        ICountryRulePack can = CountryRulePacks.For(Can);

        Assert.Empty(can.AdditionalEmployerBurden(10_000_000m));
        Assert.Empty(can.AdditionalEmployeeDeductions(10_000_000m));
        Assert.NotEmpty(can.EmployerBurden); // its rules are all ordinary data components
    }

    [Fact]
    public void The_four_original_countries_remain_pure_data()
    {
        // Behavior pinned: no existing country grew a code hook in the migration to plugins.
        foreach (string code in new[] { "DEU", "GBR", "PHL", "USA" })
        {
            ICountryRulePack pack = CountryRulePacks.For(CountryCode.From(code));
            Assert.Empty(pack.AdditionalEmployerBurden(1_000_000m));
            Assert.Empty(pack.AdditionalEmployeeDeductions(1_000_000m));
        }
    }
}
