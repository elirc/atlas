using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Auditing;
using Atlas.Domain.Countries;
using Atlas.Domain.Quotes;

namespace Atlas.Application.Quotes;

/// <summary>
/// Price a country + gross salary into a stored, immutable quote — and record that it
/// happened. Burden and fee come from the domain calculator; the employer total is also
/// converted into the org's display currency via the FX port; a quote row and an audit
/// row are written in ONE commit.
/// </summary>
public sealed class CreateQuote(
    IQuoteRepository quotes,
    ICountryRepository countries,
    IFxRates fxRates,
    IAuditTrail audit,
    IUnitOfWork unitOfWork,
    ICurrentContext context,
    TimeProvider clock)
{
    // WHY: OrgCurrency is a hard-coded "USD" for now — the simplest thing that lets us
    // show the FX port doing real work. A per-org display currency (stored on ClientOrg,
    // chosen at signup) is deferred; it is a column and a settings screen, not a new
    // idea. Recorded as future work in NOTES/ADR-0005 so it doesn't silently masquerade
    // as a finished feature.
    private static readonly CurrencyCode DisplayCurrency = CurrencyCode.From("USD");

    public async Task<Quote> ExecuteAsync(
        string? countryCode,
        decimal grossAnnualSalary,
        CancellationToken cancellationToken)
    {
        context.RequireRole(Role.OrgAdmin, Role.HrManager);
        Guid orgId = context.RequireOrg();

        CountryCode code = CountryCode.From(countryCode);

        // WHY: the country must be one Atlas actually employs in (a seeded reference
        // row) — a well-formed but unsupported code is a business "no" (422), not a 404.
        // Matches HireWorker's stance exactly.
        Country country = await countries.FindAsync(code, cancellationToken)
            ?? throw new DomainRuleException($"Atlas does not support employment in '{code}' yet.");

        CountryRulePack pack = CountryRulePacks.For(code);

        // The gross is priced in the COUNTRY's currency — a German quote is in EUR,
        // period. The client's display currency is a separate, later concern.
        var gross = Money.Of(grossAnnualSalary, CurrencyCode.From(country.Currency));

        QuoteBreakdown breakdown = QuoteCalculator.Calculate(pack, gross);
        Money displayTotal = fxRates.Convert(breakdown.TotalEmployerCost, DisplayCurrency);

        DateTimeOffset now = clock.GetUtcNow();
        var quote = Quote.FromBreakdown(orgId, code, breakdown, displayTotal, context.UserId, now);

        // LEARN: the audit row and the quote are staged into the SAME unit of work — a
        // quote nobody can prove was created is a compliance gap, so the write of the
        // fact and the write of the record-of-the-fact are one atomic act.
        audit.Append(AuditEntry.Record(
            orgId, context.UserId, "quote.created", "quote", quote.Id,
            JsonSerializer.Serialize(new
            {
                Country = code.Value,
                Gross = new { breakdown.Gross.Amount, Currency = breakdown.Gross.Currency.Value },
                TotalEmployerCost = new
                {
                    breakdown.TotalEmployerCost.Amount,
                    Currency = breakdown.TotalEmployerCost.Currency.Value,
                },
                DisplayTotal = new { displayTotal.Amount, Currency = displayTotal.Currency.Value },
                LineCount = breakdown.Lines.Count,
            }),
            now));

        await quotes.AddAsync(quote, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return quote;
    }
}
