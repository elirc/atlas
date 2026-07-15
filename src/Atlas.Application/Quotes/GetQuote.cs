using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Quotes;

namespace Atlas.Application.Quotes;

public sealed class GetQuote(IQuoteRepository quotes, ICurrentContext context)
{
    public async Task<Quote> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        context.RequireUser();

        // LEARN: tenancy is enforced in the QUERY, not after it. FindInOrgAsync filters
        // by (id AND orgId), so another org's quote comes back null — indistinguishable
        // from one that never existed. Cross-tenant reads are 404, never 403 (ADR-0003).
        Quote? quote = await quotes.FindInOrgAsync(id, context.RequireOrg(), cancellationToken);

        return quote ?? throw new NotFoundException("quote", id);
    }
}
