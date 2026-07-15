using Atlas.Application.Abstractions;
using Atlas.Domain.Countries;
using Atlas.Domain.Quotes; // S4: the Quote aggregate lands here (see NOTES.md).
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Adapters;

/// <summary>
/// Adapts the S4 quote surface (<see cref="IQuoteRepository"/> + <c>Quote</c>) to the
/// narrow <see cref="IQuoteReader"/> the engagement module asks for. This is the ONE
/// place the two modules touch — the anti-corruption seam.
/// </summary>
// INTEGRATION SEAM (S4): this is the single file in Sprint 05 that depends on S4
// domain types. It reads exactly two things off a quote — the country and the gross
// annual pay — and takes the currency from Atlas's own Country reference table so it
// needn't guess how Money exposes its currency. If S4's names differ (e.g. Gross vs
// GrossPay, Amount vs Value), the two marked reads below are the only lines to touch.
internal sealed class QuoteReader(IQuoteRepository quotes, AtlasDbContext db) : IQuoteReader
{
    public async Task<QuoteSnapshot?> FindInOrgAsync(
        Guid quoteId, Guid orgId, CancellationToken cancellationToken)
    {
        Quote? quote = await quotes.FindInOrgAsync(quoteId, orgId, cancellationToken);
        if (quote is null)
        {
            return null;
        }

        // S4 reads — the reconciliation points:
        CountryCode country = quote.Country;      // (1) quote's country
        decimal grossAnnual = quote.Gross.Amount; // (2) gross annual pay as a decimal

        // Currency comes from Atlas reference data for that country, so a worker in
        // Germany is priced in EUR regardless of how the quote stored its Money.
        string currency = await db.Set<Country>().AsNoTracking()
            .Where(c => c.Code == country)
            .Select(c => c.Currency)
            .SingleAsync(cancellationToken);

        return new QuoteSnapshot(country, grossAnnual, currency);
    }
}
