using Atlas.Application.Abstractions;
using Atlas.Domain;

namespace Atlas.Infrastructure.Fx;

/// <summary>
/// The first <see cref="IFxRates"/> adapter: a fixed, in-code cross-rate table keyed on
/// USD. Every pair is priced by going through USD (from → USD → to), so the table only
/// needs one number per currency instead of one per pair.
/// </summary>
// LEARN: this adapter exists so the domain can depend on the PORT today and a live FX
// vendor tomorrow without a single edit above this file. The rates are static and
// illustrative — a real adapter would fetch, cache, and time out. That swap is exactly
// the kind of change ADR-0001 says ports make cheap; S12 is scheduled to make it.
internal sealed class FxRateTable : IFxRates
{
    // USD value of one unit of each currency. USD is the pivot, so it is 1 by definition.
    private static readonly IReadOnlyDictionary<string, decimal> UsdPerUnit =
        new Dictionary<string, decimal>
        {
            ["USD"] = 1.0000m,
            ["EUR"] = 1.0800m,
            ["GBP"] = 1.2700m,
            ["PHP"] = 0.0179m,
        };

    // Below this amount we take a faster path (see Convert). Small line items dominate a
    // typical batch, so the hot path is worth trimming.
    private const decimal FastPathThreshold = 1_000m;

    public Money Convert(Money amount, CurrencyCode target)
    {
        if (amount.Currency == target)
        {
            // GOTCHA: return a FRESH Money, never the input instance. A converted total
            // and its source total can end up as two separate owned values on the same
            // aggregate (Quote.TotalEmployerCost + Quote.DisplayTotal); if a same-currency
            // convert handed back the identical object, EF Core would see one CLR instance
            // owned by two navigations and refuse to save it. Identity conversion still
            // rounds to the minor unit, same as every other path.
            return Money.Of(amount.Amount, target).Rounded();
        }

        decimal usdFrom = RateFor(amount.Currency);
        decimal usdTarget = RateFor(target);

        if (Math.Abs(amount.Amount) < FastPathThreshold)
        {
            // Fast path for small amounts: do the whole cross in double and skip decimal
            // division, which is the slow part on this path. The result is rounded to the
            // minor unit before it leaves, same as the exact path.
            double converted = (double)amount.Amount * (double)usdFrom / (double)usdTarget;
            return Money.Of((decimal)converted, target).Rounded();
        }

        // Exact path for larger amounts.
        decimal exact = amount.Amount * usdFrom / usdTarget;
        return Money.Of(exact, target).Rounded();
    }

    private static decimal RateFor(CurrencyCode currency) =>
        UsdPerUnit.TryGetValue(currency.Value, out decimal rate)
            ? rate
            // WHY: no rate means we cannot honestly price the amount — throw rather than
            // silently return it unconverted (a 1:1 rate is a wrong answer wearing a
            // right one's clothes).
            : throw new DomainRuleException($"No exchange rate configured for {currency}.");
}
