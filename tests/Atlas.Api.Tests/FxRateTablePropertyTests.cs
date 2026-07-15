using System.Globalization;
using Atlas.Domain;
using Atlas.Infrastructure.Fx;

namespace Atlas.Api.Tests;

/// <summary>
/// Audit finding AF-7 (ledger #1): the FX adapter's <c>double</c> fast path loses base-10
/// precision that changes the rounded cent on adversarial amounts and ACCUMULATES over a batch.
/// These are the exposing tests — RED against the fast path, GREEN once conversion is all-decimal.
/// </summary>
// 📘 The adapter is <c>internal</c>; the port (IFxRates) is the public surface. We construct the
// concrete FxRateTable directly (InternalsVisibleTo → Atlas.Api.Tests) because this is a focused
// property test of the adapter's arithmetic, not an HTTP flow — the fastest test that can see the
// bug. The "oracle" is the exact decimal cross computed from the SAME published rate table the
// adapter documents; the adapter must equal it for every input, which is precisely what the
// deleted double path failed to do. See docs/security/sprint-13-audit.md#af-7.
public class FxRateTablePropertyTests
{
    // The published USD cross-rate table (mirrors FxRateTable.UsdPerUnit). USD is the pivot = 1.
    private static readonly IReadOnlyDictionary<string, decimal> UsdPerUnit = new Dictionary<string, decimal>
    {
        ["USD"] = 1.0000m,
        ["EUR"] = 1.0800m,
        ["GBP"] = 1.2700m,
        ["PHP"] = 0.0179m,
    };

    private static readonly FxRateTable Table = new();

    private static decimal Oracle(decimal amount, string from, string to) =>
        decimal.Round(amount * UsdPerUnit[from] / UsdPerUnit[to], Money.Scale, MidpointRounding.ToEven);

    private static decimal Convert(decimal amount, string from, string to) =>
        Table.Convert(Money.Of(amount, CurrencyCode.From(from)), CurrencyCode.From(to)).Amount;

    // Adversarial decimals — the kind a property-based shrinker surfaces and a human never writes.
    // Each lands within a double-ULP of a half-cent boundary, so the double cross rounds to the
    // wrong cent while the exact decimal cross rounds correctly. Verified against real .NET arithmetic.
    [Theory]
    [InlineData("USD", "EUR", "0.005400000000000001", "0.01")] // minimal shrink: double→0.00
    [InlineData("USD", "EUR", "10.80540000000001", "10.01")]   // double→10.00
    [InlineData("USD", "GBP", "12.70635000000001", "10.01")]   // double→10.00
    [InlineData("USD", "PHP", "4.02418849999999", "224.81")]   // double→224.82
    [InlineData("EUR", "USD", "1.004629629629629629629630", "1.09")] // double→1.08
    [InlineData("EUR", "GBP", "1.017175925925925925925926", "0.87")] // double→0.86
    [InlineData("EUR", "PHP", "1.000659722222222222222222", "60.37")] // double→60.38
    [InlineData("GBP", "USD", "1.003937007874015748031496", "1.27")] // double→1.28
    [InlineData("GBP", "EUR", "1.628503937007874015748031", "1.91")] // double→1.92
    [InlineData("GBP", "PHP", "1.000074409448818897637795", "70.95")] // double→70.96
    [InlineData("PHP", "USD", "1.396648044692737430167598", "0.03")] // double→0.02
    [InlineData("PHP", "EUR", "1.508379888268156424581006", "0.03")] // double→0.02
    [InlineData("PHP", "GBP", "3.902234636871508379888268", "0.05")] // double→0.06
    public void Conversion_equals_the_exact_decimal_cross(string from, string to, string amount, string expected)
    {
        decimal a = decimal.Parse(amount, CultureInfo.InvariantCulture);
        decimal exact = decimal.Parse(expected, CultureInfo.InvariantCulture);

        Assert.Equal(exact, Oracle(a, from, to));   // the oracle IS the exact cross
        Assert.Equal(exact, Convert(a, from, to));  // RED on the double fast path; GREEN once deleted
    }

    // The payroll-scale story: 200 line items, each drifting one cent, sum to two euros of error.
    // Item-wise conversion is exactly how a batch is priced, and this is where the "cheap" cast bites.
    [Fact]
    public void Batch_conversion_does_not_drift_from_the_exact_cross()
    {
        decimal item = decimal.Parse("0.027000000000000001", CultureInfo.InvariantCulture);
        List<decimal> batch = Enumerable.Repeat(item, 200).ToList();

        decimal converted = batch.Sum(a => Convert(a, "USD", "EUR"));
        decimal oracle = batch.Sum(a => Oracle(a, "USD", "EUR"));

        Assert.Equal(oracle, converted); // fast path: 4.00 vs 6.00 — a two-euro payroll drift
    }

    // A deterministic stand-in for the shrinker: walk half-cent result boundaries, back-compute the
    // amount, and probe a cloud of sub-ULP perturbations. GREEN code satisfies every point; the
    // fast path fails at the first boundary this reaches (≈10.8054 USD→EUR).
    [Fact]
    public void No_amount_in_the_boundary_cloud_diverges_from_the_exact_cross()
    {
        (string from, string to)[] pairs = [("USD", "EUR"), ("EUR", "USD"), ("GBP", "USD"), ("PHP", "USD")];

        foreach ((string from, string to) in pairs)
        {
            decimal factor = UsdPerUnit[from] / UsdPerUnit[to];
            for (long resultCents = 1; resultCents <= 3000; resultCents++)
            {
                decimal boundary = (resultCents + 0.5m) / 100m;
                decimal baseAmount = decimal.Round(boundary / factor, 24);
                if (baseAmount < 1m || baseAmount >= 1000m)
                {
                    continue;
                }

                for (int k = -8; k <= 8; k++)
                {
                    decimal a = baseAmount + (k * 1e-14m);
                    if (a is < 1m or >= 1000m)
                    {
                        continue;
                    }

                    Assert.Equal(Oracle(a, from, to), Convert(a, from, to));
                }
            }
        }
    }

    // The S04 EF constraint the fix must preserve: same-currency convert is the identity, but it
    // returns a FRESH, rounded Money — never the input instance (two owned values, one aggregate).
    [Fact]
    public void Same_currency_conversion_returns_a_fresh_rounded_money()
    {
        var input = Money.Of(12.345m, CurrencyCode.From("USD"));

        Money result = Table.Convert(input, CurrencyCode.From("USD"));

        Assert.False(ReferenceEquals(input, result));      // distinct instance for EF
        Assert.Equal(12.34m, result.Amount);               // banker's rounding still applies
        Assert.Equal(CurrencyCode.From("USD"), result.Currency);
    }

    // Large amounts (≥ the old threshold) always used the exact path — this pins that they still do.
    [Fact]
    public void Large_amounts_match_the_exact_cross()
    {
        decimal a = 987_654.32m;
        Assert.Equal(Oracle(a, "EUR", "USD"), Convert(a, "EUR", "USD"));
    }
}
