namespace Atlas.Domain;

/// <summary>
/// An amount of money in a single currency. A value object: two Moneys are equal iff
/// their amount AND currency are equal, arithmetic is closed within one currency, and
/// rounding is explicit and banker's (see <see cref="Rounded"/>).
/// </summary>
// 📘 Three decisions are baked into this type, and each kills a class of bug:
//   1. `decimal`, never `double`. Money is base-10; 0.1 + 0.2 must be 0.3, not
//      0.30000000000000004. `double` is for physics, `decimal` is for ledgers.
//   2. Currency is part of IDENTITY. `Money.Of(10, EUR) + Money.Of(10, USD)` throws —
//      there is no meaningful sum without a rate, so the type refuses to invent one.
//   3. No implicit conversion to/from decimal. A bare `decimal` is a quantity with no
//      unit; forcing `Money.Of(x, ccy)` at every boundary is where the currency gets
//      attached and checked, once.
// This is a reference type (record class) so EF Core can own it as a value object
// (OwnsOne) — see QuoteConfiguration. Value equality still holds (records compare by
// field).
public sealed record Money
{
    /// <summary>Minor-unit scale used for rounding and allocation.</summary>
    // GOTCHA: fixed at 2 because every currency Atlas models today (EUR/GBP/PHP/USD)
    // has 2 minor digits. JPY (0) and BHD (3) would need a per-currency scale table;
    // that is deliberately deferred (LEARN: don't build the table until a 0- or
    // 3-digit currency actually arrives — see ADR-0005 "recorded bets").
    public const int Scale = 2;

    private const decimal MinorUnitsPerUnit = 100m;

    public decimal Amount { get; }
    public CurrencyCode Currency { get; }

    private Money(decimal amount, CurrencyCode currency)
    {
        Amount = amount;
        Currency = currency;
    }

    /// <summary>The single public constructor. There is no <c>Money(decimal)</c> — a
    /// currency is not optional.</summary>
    public static Money Of(decimal amount, CurrencyCode currency) => new(amount, currency);

    public static Money Zero(CurrencyCode currency) => new(0m, currency);

    // WHY: addition and subtraction are closed WITHIN a currency and undefined across
    // currencies. The cross-currency case is not a runtime check bolted on at one
    // endpoint — it is the type saying "I don't know what you mean," everywhere, for free.
    public static Money operator +(Money left, Money right)
    {
        left.MustMatch(right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        left.MustMatch(right);
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    public static Money operator -(Money value) => new(-value.Amount, value.Currency);

    // WHY: scalar multiplication is meaningful (a rate times an amount) and stays in the
    // same currency. It is deliberately NOT rounded here — see the note on Rounded().
    public static Money operator *(Money value, decimal factor) =>
        new(value.Amount * factor, value.Currency);

    public static Money operator *(decimal factor, Money value) => value * factor;

    /// <summary>
    /// The ONE place rounding happens. Banker's rounding (round-half-to-even) at the
    /// currency's minor-unit scale.
    /// </summary>
    // ⚠️ Rounding policy is a DECISION, not a default. We picked MidpointRounding.ToEven
    // (banker's) because it removes the upward bias that away-from-zero rounding
    // accumulates across thousands of line items — the classic "our totals drift a few
    // cents high every month" bug. Arithmetic (+, -, *) intentionally does NOT round;
    // if it did, rounding would happen at every intermediate step and the error would
    // depend on evaluation order. Compute in full precision, round once, on the way out.
    public Money Rounded() =>
        new(decimal.Round(Amount, Scale, MidpointRounding.ToEven), Currency);

    /// <summary>
    /// Split this (rounded) amount into <paramref name="parts"/> shares whose sum is
    /// EXACTLY the total — no lost or conjured minor units. Largest-remainder: the
    /// leftover units are handed out one per share, so no two shares differ by more
    /// than one minor unit.
    /// </summary>
    // 📘 The problem allocation solves: 10.00 / 3 = 3.333… There is no way to write
    // three equal 2-dp numbers that sum to 10.00. Naive rounding gives 3.33 * 3 = 9.99
    // and a cent evaporates. Allocation refuses to lose it: [3.34, 3.33, 3.33].
    public IReadOnlyList<Money> Allocate(int parts)
    {
        if (parts <= 0)
        {
            throw new DomainRuleException("Money can only be allocated into a positive number of parts.");
        }

        long total = ToMinorUnits();
        long each = total / parts;
        long remainder = total - (each * parts); // sign matches total, |remainder| < parts
        long step = Math.Sign(remainder);
        int extra = (int)Math.Abs(remainder);

        var shares = new Money[parts];
        for (int i = 0; i < parts; i++)
        {
            long minor = each + (i < extra ? step : 0);
            shares[i] = FromMinorUnits(minor, Currency);
        }

        return shares;
    }

    /// <summary>
    /// Distribute this (rounded) amount across <paramref name="weights"/> so the shares
    /// sum EXACTLY to the total, proportional to the weights. Largest-remainder again:
    /// fractional units go to the shares with the biggest leftover fraction first.
    /// </summary>
    public IReadOnlyList<Money> AllocateByWeights(IReadOnlyList<decimal> weights)
    {
        if (weights is null || weights.Count == 0)
        {
            throw new DomainRuleException("Allocation needs at least one weight.");
        }

        if (weights.Any(w => w < 0m))
        {
            throw new DomainRuleException("Allocation weights cannot be negative.");
        }

        decimal weightSum = weights.Sum();
        if (weightSum <= 0m)
        {
            throw new DomainRuleException("Allocation weights must sum to a positive value.");
        }

        long total = ToMinorUnits();
        if (total < 0)
        {
            // WHY: weighted allocation of a negative amount has ambiguous "largest
            // remainder" semantics; the callers that need it (fees, splits) are always
            // non-negative. Fail loudly rather than guess.
            throw new DomainRuleException("Weighted allocation requires a non-negative amount.");
        }

        var floors = new long[weights.Count];
        var remainders = new decimal[weights.Count];
        long allocated = 0;

        for (int i = 0; i < weights.Count; i++)
        {
            decimal ideal = total * weights[i] / weightSum;
            long floor = (long)Math.Floor(ideal);
            floors[i] = floor;
            remainders[i] = ideal - floor;
            allocated += floor;
        }

        long leftover = total - allocated;

        // Hand each leftover minor unit to the largest fractional remainder; ties break
        // toward the lower index so the result is deterministic.
        foreach (int index in Enumerable.Range(0, weights.Count)
                     .OrderByDescending(i => remainders[i])
                     .ThenBy(i => i)
                     .Take((int)leftover))
        {
            floors[index]++;
        }

        return [.. floors.Select(minor => FromMinorUnits(minor, Currency))];
    }

    public override string ToString() => $"{Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} {Currency}";

    private void MustMatch(Money other)
    {
        if (Currency != other.Currency)
        {
            throw new DomainRuleException(
                $"Cannot combine {Currency} and {other.Currency} without an explicit exchange rate.");
        }
    }

    private long ToMinorUnits() =>
        (long)decimal.Round(Amount * MinorUnitsPerUnit, 0, MidpointRounding.ToEven);

    private static Money FromMinorUnits(long minor, CurrencyCode currency) =>
        new(minor / MinorUnitsPerUnit, currency);
}
