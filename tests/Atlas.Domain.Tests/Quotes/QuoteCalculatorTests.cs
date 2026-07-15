using Atlas.Domain;
using Atlas.Domain.Countries;
using Atlas.Domain.Quotes;

namespace Atlas.Domain.Tests.Quotes;

public class QuoteCalculatorTests
{
    private static Money Line(QuoteBreakdown breakdown, string label) =>
        breakdown.Lines.Single(l => l.Label == label).Amount;

    [Fact]
    public void Germany_at_60000_eur_prices_every_line_as_hand_checked()
    {
        CurrencyCode eur = CurrencyCode.From("EUR");
        QuoteBreakdown b = QuoteCalculator.Calculate(
            CountryRulePacks.For(CountryCode.From("DEU")), Money.Of(60_000m, eur));

        // 60,000 is below every DEU ceiling, so each rate applies to the full gross.
        Assert.Equal(Money.Of(5_580.00m, eur), Line(b, "Pension insurance (employer)"));       // 9.3%
        Assert.Equal(Money.Of(4_380.00m, eur), Line(b, "Health insurance (employer)"));        // 7.3%
        Assert.Equal(Money.Of(780.00m, eur), Line(b, "Unemployment insurance (employer)"));    // 1.3%
        Assert.Equal(Money.Of(1_020.00m, eur), Line(b, "Long-term care insurance (employer)")); // 1.7%
        Assert.Equal(Money.Of(7_200.00m, eur), Line(b, QuoteCalculator.ManagementFeeLabel));    // 12% < ceiling

        // gross 60,000 + burden 11,760 + fee 7,200 = 78,960.
        Assert.Equal(Money.Of(78_960.00m, eur), b.TotalEmployerCost);
        Assert.Equal(5, b.Lines.Count);
    }

    [Fact]
    public void Usa_at_200000_usd_caps_social_security_and_pins_the_fee_ceiling()
    {
        CurrencyCode usd = CurrencyCode.From("USD");
        QuoteBreakdown b = QuoteCalculator.Calculate(
            CountryRulePacks.For(CountryCode.From("USA")), Money.Of(200_000m, usd));

        Assert.Equal(Money.Of(10_453.20m, usd), Line(b, "Social Security (employer FICA)")); // 6.2% of 168,600
        Assert.Equal(Money.Of(2_900.00m, usd), Line(b, "Medicare (employer)"));              // 1.45% of 200,000
        Assert.Equal(Money.Of(42.00m, usd), Line(b, "Federal unemployment (FUTA)"));         // 0.6% of 7,000
        // 12% of 200,000 = 24,000, but the fee ceiling of 12,000 binds.
        Assert.Equal(Money.Of(12_000.00m, usd), Line(b, QuoteCalculator.ManagementFeeLabel));

        Assert.Equal(Money.Of(225_395.20m, usd), b.TotalEmployerCost);
    }

    [Fact]
    public void Uk_at_50000_gbp_charges_ni_only_above_the_threshold()
    {
        CurrencyCode gbp = CurrencyCode.From("GBP");
        QuoteBreakdown b = QuoteCalculator.Calculate(
            CountryRulePacks.For(CountryCode.From("GBR")), Money.Of(50_000m, gbp));

        // 13.8% of (50,000 − 9,100 threshold) = 5,644.20.
        Assert.Equal(Money.Of(5_644.20m, gbp), Line(b, "Employer National Insurance"));
        Assert.Equal(Money.Of(6_000.00m, gbp), Line(b, QuoteCalculator.ManagementFeeLabel)); // 12% of 50,000
        Assert.Equal(Money.Of(61_644.20m, gbp), b.TotalEmployerCost);
    }

    [Fact]
    public void Philippines_at_600000_php_caps_each_contribution_independently()
    {
        CurrencyCode php = CurrencyCode.From("PHP");
        QuoteBreakdown b = QuoteCalculator.Calculate(
            CountryRulePacks.For(CountryCode.From("PHL")), Money.Of(600_000m, php));

        Assert.Equal(Money.Of(39_900.00m, php), Line(b, "SSS (employer)"));        // 9.5% of 420,000 cap
        Assert.Equal(Money.Of(15_000.00m, php), Line(b, "PhilHealth (employer)")); // 2.5% of 600,000 (< cap)
        Assert.Equal(Money.Of(2_400.00m, php), Line(b, "Pag-IBIG (employer)"));    // 2% of 120,000 cap
        Assert.Equal(Money.Of(12_000.00m, php), Line(b, QuoteCalculator.ManagementFeeLabel)); // ceiling binds
        Assert.Equal(Money.Of(669_300.00m, php), b.TotalEmployerCost);
    }

    [Fact]
    public void Gross_and_total_are_in_the_country_currency()
    {
        CurrencyCode eur = CurrencyCode.From("EUR");
        QuoteBreakdown b = QuoteCalculator.Calculate(
            CountryRulePacks.For(CountryCode.From("DEU")), Money.Of(1_000m, eur));

        Assert.Equal(eur, b.Gross.Currency);
        Assert.Equal(eur, b.TotalEmployerCost.Currency);
        Assert.All(b.Lines, l => Assert.Equal(eur, l.Amount.Currency));
    }

    [Fact]
    public void A_gross_in_the_wrong_currency_is_refused()
    {
        // The pack prices Germany in EUR; handing it USD is a programmer error, and the
        // engine refuses to invent an answer.
        Assert.Throws<DomainRuleException>(() => QuoteCalculator.Calculate(
            CountryRulePacks.For(CountryCode.From("DEU")), Money.Of(60_000m, CurrencyCode.From("USD"))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_gross_is_refused(decimal gross)
    {
        Assert.Throws<DomainRuleException>(() => QuoteCalculator.Calculate(
            CountryRulePacks.For(CountryCode.From("DEU")), Money.Of(gross, CurrencyCode.From("EUR"))));
    }
}
