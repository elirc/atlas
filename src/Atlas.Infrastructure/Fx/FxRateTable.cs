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

        // 📘 [Author] S13 audit finding AF-7 (harvest of ledger #1, planted S04 in commit
        // d232b1c). This method used to carry a "fast path" that, for amounts under 1,000,
        // did the cross in `double`: `(double)amount * (double)usdFrom / (double)usdTarget`.
        // The claim on the tin was "skip the slow decimal division on the hot path." The
        // claim was never benchmarked, and it was wrong twice over:
        //   1. `decimal` division is not the bottleneck on any workload Atlas has, and
        //   2. `double` is base-2; money is base-10. A `double` cross of a base-10 amount
        //      lands a hair off the exact value, and near a half-cent boundary that hair
        //      rounds to the WRONG cent. The exposing property test converts adversarial
        //      decimals item-by-item and shows the double path drifting from the exact
        //      decimal cross — a drift that ACCUMULATES across a payroll-scale batch.
        // The fix is a deletion: there is one path now, and it is exact. `decimal`, never
        // `double`, for money — the same rule Money itself enforces (see Money's 📘).
        // ⚠️ The lesson worth keeping: a performance claim with no benchmark is a
        // correctness risk wearing a speed costume. We removed an "optimization" that
        // bought nothing and cost cents. See docs/security/sprint-13-audit.md#af-7.
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
