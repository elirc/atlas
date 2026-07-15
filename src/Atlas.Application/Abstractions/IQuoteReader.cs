using Atlas.Domain.Countries;

namespace Atlas.Application.Abstractions;

/// <summary>
/// A narrow, read-only view of an S4 quote — exactly what starting a hire needs
/// (which country, what gross pay) and nothing of the quote's cost-breakdown guts.
/// </summary>
// LEARN: the engagement module does not need the Quote AGGREGATE, only a couple of
// its numbers. So Application declares the shape it wants (this port + snapshot) and
// Infrastructure adapts the S4 IQuoteRepository/Quote to it. The payoff: the whole
// engagement lifecycle — and every test of it — is decoupled from how quotes are
// built. When S12 pivots the quote engine, nothing here notices. (If you'd rather
// use IQuoteRepository directly, this port is a two-line adapter to delete; the
// trade is recorded in NOTES.md.)
public interface IQuoteReader
{
    /// <summary>The quote's hire-relevant fields, org-scoped; null if it isn't the caller's.</summary>
    Task<QuoteSnapshot?> FindInOrgAsync(Guid quoteId, Guid orgId, CancellationToken cancellationToken);
}

/// <summary>The slice of a quote an engagement is created from.</summary>
public sealed record QuoteSnapshot(
    CountryCode Country,
    decimal GrossAnnualSalary,
    string Currency);
