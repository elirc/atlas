using Atlas.Domain;

namespace Atlas.Application.Abstractions;

/// <summary>
/// Converts an amount from its currency into another. A PORT: the domain and use cases
/// know only this shape, never where the rate came from.
/// </summary>
// 🔗 This is ADR-0001's "ports for volatility" thesis made concrete. Today the only
// adapter is a fixed in-code table (Infrastructure.Fx.FxRateTable); tomorrow it is a
// live vendor with an HTTP call, a cache, and a timeout. Neither the quote engine nor
// CreateQuote changes when that swap happens — S12 is scheduled to prove it. Ports earn
// their rent later, and this is the rent contract.
public interface IFxRates
{
    /// <summary>
    /// Convert <paramref name="amount"/> into <paramref name="target"/>. Same currency in
    /// and out is the identity (return the amount unchanged). Unknown currency → the
    /// adapter throws a DomainRuleException — an FX table that can't price a currency
    /// should fail loudly, not guess a rate of 1.
    /// </summary>
    Money Convert(Money amount, CurrencyCode target);
}
