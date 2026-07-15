// NOTE: namespace is Atlas.Domain.Tests (see CurrencyCodeTests) — a `Money` namespace here
// would shadow the Money type. Money/CurrencyCode resolve via the ancestor Atlas.Domain scope.
namespace Atlas.Domain.Tests;

public class MoneyTests
{
    private static readonly CurrencyCode Usd = CurrencyCode.From("USD");
    private static readonly CurrencyCode Eur = CurrencyCode.From("EUR");

    [Fact]
    public void Of_carries_amount_and_currency()
    {
        Money m = Money.Of(19.99m, Usd);

        Assert.Equal(19.99m, m.Amount);
        Assert.Equal(Usd, m.Currency);
    }

    [Fact]
    public void Value_equality_is_by_amount_and_currency()
    {
        Assert.Equal(Money.Of(10m, Usd), Money.Of(10m, Usd));
        Assert.NotEqual(Money.Of(10m, Usd), Money.Of(10m, Eur));
        Assert.NotEqual(Money.Of(10m, Usd), Money.Of(11m, Usd));
    }

    [Fact]
    public void Same_currency_arithmetic_stays_in_that_currency()
    {
        Assert.Equal(Money.Of(30m, Usd), Money.Of(10m, Usd) + Money.Of(20m, Usd));
        Assert.Equal(Money.Of(-10m, Usd), Money.Of(10m, Usd) - Money.Of(20m, Usd));
        Assert.Equal(Money.Of(30m, Usd), Money.Of(10m, Usd) * 3m);
        Assert.Equal(Money.Of(30m, Usd), 3m * Money.Of(10m, Usd));
        Assert.Equal(Money.Of(-10m, Usd), -Money.Of(10m, Usd));
    }

    [Fact]
    public void Mixed_currency_addition_and_subtraction_throw()
    {
        // The whole point of the type: there is no meaningful €10 + $10 without a rate,
        // so it does not compile into a silent wrong number — it throws.
        Assert.Throws<DomainRuleException>(() => Money.Of(10m, Usd) + Money.Of(10m, Eur));
        Assert.Throws<DomainRuleException>(() => Money.Of(10m, Usd) - Money.Of(10m, Eur));
    }

    [Theory]
    // Banker's rounding (half-to-even) at 2 dp. These midpoints are exactly representable
    // in decimal, so the rounding direction is fully determined — hand-checked:
    [InlineData("2.005", "2.00")] // → 2.00 (0 is even)
    [InlineData("2.015", "2.02")] // → 2.02 (2 is even)
    [InlineData("2.025", "2.02")] // → 2.02 (2 is even)
    [InlineData("2.035", "2.04")] // → 2.04 (4 is even)
    [InlineData("0.125", "0.12")] // → 0.12 (2 is even)
    public void Rounded_uses_bankers_rounding(string raw, string expected)
    {
        decimal amount = decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        decimal want = decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(Money.Of(want, Usd), Money.Of(amount, Usd).Rounded());
    }

    [Fact]
    public void Allocate_splits_the_indivisible_cent_by_largest_remainder()
    {
        // $10.00 into 3 shares: 1000¢ / 3 = 333 each, 1¢ left → first share gets it.
        IReadOnlyList<Money> shares = Money.Of(10.00m, Usd).Allocate(3);

        Assert.Equal(
            [Money.Of(3.34m, Usd), Money.Of(3.33m, Usd), Money.Of(3.33m, Usd)],
            shares);
        Assert.Equal(Money.Of(10.00m, Usd), Sum(shares, Usd));
    }

    [Fact]
    public void Allocate_that_divides_evenly_has_no_remainder()
    {
        IReadOnlyList<Money> shares = Money.Of(10.00m, Usd).Allocate(4);

        Assert.All(shares, s => Assert.Equal(Money.Of(2.50m, Usd), s));
        Assert.Equal(Money.Of(10.00m, Usd), Sum(shares, Usd));
    }

    [Fact]
    public void AllocateByWeights_is_proportional_and_exact()
    {
        IReadOnlyList<Money> shares = Money.Of(100.00m, Usd).AllocateByWeights([70m, 30m]);

        Assert.Equal([Money.Of(70.00m, Usd), Money.Of(30.00m, Usd)], shares);

        // Equal weights on an indivisible total still sum exactly — the odd cent lands
        // on the first share (largest-remainder, ties to the lower index).
        IReadOnlyList<Money> thirds = Money.Of(100.00m, Usd).AllocateByWeights([1m, 1m, 1m]);
        Assert.Equal(
            [Money.Of(33.34m, Usd), Money.Of(33.33m, Usd), Money.Of(33.33m, Usd)],
            thirds);
        Assert.Equal(Money.Of(100.00m, Usd), Sum(thirds, Usd));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Allocate_requires_a_positive_number_of_parts(int parts)
    {
        Assert.Throws<DomainRuleException>(() => Money.Of(10m, Usd).Allocate(parts));
    }

    private static Money Sum(IEnumerable<Money> parts, CurrencyCode currency)
    {
        Money total = Money.Zero(currency);
        foreach (Money part in parts)
        {
            total += part;
        }

        return total;
    }
}
