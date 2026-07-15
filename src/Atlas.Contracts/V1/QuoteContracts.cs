namespace Atlas.Contracts.V1;

/// <summary>Body of POST /api/v1/quotes.</summary>
// WHY: CountryCode is nullable and Gross is a bare decimal — both absences are VALID
// request shapes the domain rejects with a useful 422 (missing country, non-positive
// gross), not deserialization 500s. The currency is NOT in the request: a quote is
// priced in the country's own currency, which the server derives (see CreateQuote).
public sealed record CreateQuoteRequest(string? CountryCode, decimal Gross);

/// <summary>An amount of money as the API speaks it: a decimal and an ISO 4217 code.</summary>
// LEARN: the domain's Money is a rich value object; the wire shape is two primitives.
// The boundary is where identity-bearing types become transport, on purpose — Contracts
// stay logic-free so an internal rename is never an API break (ADR-0002).
public sealed record MoneyDto(decimal Amount, string Currency);

/// <summary>One priced line of a quote's breakdown.</summary>
public sealed record QuoteLineDto(string Label, string Kind, MoneyDto Amount);

/// <summary>A stored quote, as served by /api/v1/quotes.</summary>
public sealed record QuoteDto(
    Guid Id,
    string Country,
    MoneyDto Gross,
    IReadOnlyList<QuoteLineDto> Lines,
    MoneyDto TotalEmployerCost,
    MoneyDto DisplayTotal,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAtUtc);
