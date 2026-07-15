using CsCheck;

// NOTE: namespace is Atlas.Domain.Tests (see CurrencyCodeTests) — a `Money` namespace here
// would shadow the Money type. Money/CurrencyCode resolve via the ancestor Atlas.Domain scope.
namespace Atlas.Domain.Tests;

/// <summary>
/// Property-based tests for Money's algebra. Example tests (MoneyTests) pin specific
/// numbers; these state the LAWS Money must obey for every input CsCheck can invent, then
/// shrink any counterexample to its minimal form.
/// </summary>
// 📘 The difference: an example test says "10.00 into 3 is [3.34, 3.33, 3.33]." A
// property says "for ANY amount and ANY part count, the shares sum EXACTLY to the total."
// The second is the thing we actually believe — and the one that catches the input we
// didn't think to write down.
public class MoneyPropertyTests
{
    private static readonly CurrencyCode[] Currencies =
        [CurrencyCode.From("USD"), CurrencyCode.From("EUR"),
         CurrencyCode.From("GBP"), CurrencyCode.From("PHP")];

    private static readonly Gen<CurrencyCode> GenCurrency =
        Gen.Int[0, Currencies.Length - 1].Select(i => Currencies[i]);

    // Amounts at 2-dp scale in [-1,000,000.00 .. 1,000,000.00] — already minor-unit
    // aligned, so Rounded() is the identity and equality checks are exact.
    private static readonly Gen<decimal> GenAmount =
        Gen.Long[-100_000_000L, 100_000_000L].Select(cents => cents / 100m);

    private static readonly Gen<decimal> GenNonNegativeAmount =
        Gen.Long[0L, 100_000_000L].Select(cents => cents / 100m);

    private static readonly Gen<Money> GenMoney =
        from amount in GenAmount
        from currency in GenCurrency
        select Money.Of(amount, currency);

    [Fact]
    public void Addition_is_closed_and_commutative_within_a_currency()
    {
        (from a in GenMoney
         from otherAmount in GenAmount
         select (a, b: Money.Of(otherAmount, a.Currency)))
            .Sample(t =>
            {
                Money sum = t.a + t.b;

                Assert.Equal(t.a.Currency, sum.Currency);
                Assert.Equal(t.b + t.a, sum);
                Assert.Equal(Money.Of(t.a.Amount + t.b.Amount, t.a.Currency), sum);
            });
    }

    [Fact]
    public void Scalar_multiplication_scales_the_amount_and_keeps_the_currency()
    {
        (from m in GenMoney from k in Gen.Int[-1000, 1000] select (m, k))
            .Sample(t =>
            {
                Money product = t.m * t.k;

                Assert.Equal(t.m.Currency, product.Currency);
                Assert.Equal(t.m.Amount * t.k, product.Amount);
            });
    }

    [Fact]
    public void Allocate_sums_to_the_total_and_no_share_differs_by_more_than_one_minor_unit()
    {
        (from m in GenMoney from parts in Gen.Int[1, 50] select (m, parts))
            .Sample(t =>
            {
                IReadOnlyList<Money> shares = t.m.Allocate(t.parts);

                Assert.Equal(t.parts, shares.Count);

                Money sum = Money.Zero(t.m.Currency);
                foreach (Money share in shares)
                {
                    sum += share;
                }

                // The whole promise of allocation: not one minor unit lost or invented.
                Assert.Equal(t.m.Rounded(), sum);

                long[] minor = [.. shares.Select(ToMinorUnits)];
                Assert.True(minor.Max() - minor.Min() <= 1,
                    $"shares differ by more than one minor unit: [{string.Join(", ", minor)}]");
            });
    }

    [Fact]
    public void AllocateByWeights_sums_exactly_to_the_total()
    {
        (from amount in GenNonNegativeAmount
         from currency in GenCurrency
         from w0 in Gen.Int[0, 100]
         from w1 in Gen.Int[0, 100]
         from w2 in Gen.Int[0, 100]
         where w0 + w1 + w2 > 0
         select (money: Money.Of(amount, currency), weights: new decimal[] { w0, w1, w2 }))
            .Sample(t =>
            {
                IReadOnlyList<Money> shares = t.money.AllocateByWeights(t.weights);

                Money sum = Money.Zero(t.money.Currency);
                foreach (Money share in shares)
                {
                    sum += share;
                }

                Assert.Equal(t.money.Rounded(), sum);
            });
    }

    [Fact]
    public void Cross_currency_addition_always_throws()
    {
        (from a in GenAmount from b in GenAmount select (a, b))
            .Sample(t =>
            {
                Money usd = Money.Of(t.a, CurrencyCode.From("USD"));
                Money eur = Money.Of(t.b, CurrencyCode.From("EUR"));

                Assert.Throws<DomainRuleException>(() => usd + eur);
                Assert.Throws<DomainRuleException>(() => usd - eur);
            });
    }

    private static long ToMinorUnits(Money money) =>
        (long)decimal.Round(money.Amount * 100m, 0, MidpointRounding.ToEven);
}
