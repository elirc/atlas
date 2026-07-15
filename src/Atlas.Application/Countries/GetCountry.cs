using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Countries;

namespace Atlas.Application.Countries;

public sealed class GetCountry(ICountryRepository countries)
{
    public async Task<Country> ExecuteAsync(string rawCode, CancellationToken cancellationToken)
    {
        // WHY: CountryCode.From throws DomainRuleException (422) for garbage input —
        // "DEUX" is a malformed business identifier, not a missing resource.
        CountryCode code = CountryCode.From(rawCode);

        Country? country = await countries.FindAsync(code, cancellationToken);

        return country ?? throw new NotFoundException("country", code);
    }
}
