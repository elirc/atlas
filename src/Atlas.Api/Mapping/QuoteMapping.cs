using Atlas.Contracts.V1;
using Atlas.Domain;
using Atlas.Domain.Quotes;

namespace Atlas.Api.Mapping;

// WHY: quote → contract mapping, kept beside DtoMapping and just as boring. Extension
// methods can live in any static class; a focused file keeps the quote wire-shape in one
// readable place without touching the S03 mapping surface.
internal static class QuoteMapping
{
    public static MoneyDto ToDto(this Money money) =>
        new(money.Amount, money.Currency.Value);

    public static QuoteLineDto ToDto(this QuoteLine line) =>
        new(line.Label, line.Kind.ToString(), line.Amount.ToDto());

    public static QuoteDto ToDto(this Quote quote) =>
        new(
            quote.Id,
            quote.Country.Value,
            quote.Gross.ToDto(),
            [.. quote.Lines.Select(l => l.ToDto())],
            quote.TotalEmployerCost.ToDto(),
            quote.DisplayTotal.ToDto(),
            quote.CreatedByUserId,
            quote.CreatedAtUtc);
}
