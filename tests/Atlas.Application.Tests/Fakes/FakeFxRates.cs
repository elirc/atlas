using Atlas.Application.Abstractions;
using Atlas.Domain;

namespace Atlas.Application.Tests.Fakes;

/// <summary>Converts nothing — keeps the amount, retags the currency. Lets a test assert
/// "the conversion path ran and produced the display currency" without arithmetic.</summary>
public sealed class IdentityFxRates : IFxRates
{
    public Money Convert(Money amount, CurrencyCode target) => Money.Of(amount.Amount, target);
}

/// <summary>A single fixed multiplier for any cross-currency conversion — deterministic,
/// so an expected display total is a hand-checkable number.</summary>
public sealed class FixedFxRates(decimal factor) : IFxRates
{
    public Money Convert(Money amount, CurrencyCode target) =>
        amount.Currency == target
            ? amount
            : Money.Of(amount.Amount * factor, target).Rounded();
}
