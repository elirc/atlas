using Atlas.Domain.Countries;

namespace Atlas.Domain.Quotes;

/// <summary>
/// The cost engine: given a country's rule pack and a gross annual salary, produce the
/// employer's total-cost-of-employment breakdown. Pure, deterministic, side-effect-free
/// — the same inputs always yield the same numbers, which is exactly why a quote can be
/// a frozen snapshot (see <see cref="Quote"/>).
/// </summary>
public static class QuoteCalculator
{
    // WHY: Atlas's fee is a flat 12% of gross, ceilinged at 12,000 of the local currency
    // — illustrative. Two teaching points hide here: (1) the ceiling is a numeric 12,000
    // regardless of currency, which is WRONG in the real world (12,000 JPY ≠ 12,000 USD)
    // and is flagged as future work in ADR-0005; (2) the ceiling is where "trace one
    // euro" gets interesting, because above ~100k gross the fee stops tracking gross.
    public const decimal ManagementFeeRate = 0.12m;
    public const decimal ManagementFeeCeiling = 12_000m;
    public const string ManagementFeeLabel = "Atlas management fee";

    public static QuoteBreakdown Calculate(CountryRulePack pack, Money gross)
    {
        // WHY: the gross must already be in the pack's currency. The use case builds it
        // from the country's own currency, so a mismatch here is a programmer error, not
        // user input — but the domain still refuses it rather than pricing nonsense.
        if (gross.Currency != pack.Currency)
        {
            throw new DomainRuleException(
                $"Gross salary is in {gross.Currency} but {pack.Country} is priced in {pack.Currency}.");
        }

        if (gross.Amount <= 0m)
        {
            throw new DomainRuleException("Gross salary must be positive to quote.");
        }

        var lines = new List<QuoteLineItem>();

        // Statutory employer burden, one line per component, each capped by its own pack
        // rule. Rounding happens exactly once per line, via Money.Rounded().
        foreach (BurdenComponent component in pack.EmployerBurden)
        {
            Money amount = Money.Of(component.Rate * component.AssessableBase(gross.Amount), gross.Currency)
                .Rounded();
            lines.Add(new QuoteLineItem(component.Name, QuoteLineKind.EmployerBurden, amount));
        }

        // Atlas management fee: min(rate * gross, ceiling).
        decimal fee = Math.Min(gross.Amount * ManagementFeeRate, ManagementFeeCeiling);
        lines.Add(new QuoteLineItem(
            ManagementFeeLabel, QuoteLineKind.ManagementFee, Money.Of(fee, gross.Currency).Rounded()));

        // Total = gross + every line. Each addend is already rounded to the minor unit,
        // so the sum is exact; Rounded() at the end is belt-and-suspenders, not a fudge.
        Money total = gross;
        foreach (QuoteLineItem line in lines)
        {
            total += line.Amount;
        }

        return new QuoteBreakdown(gross.Rounded(), lines, total.Rounded());
    }
}
