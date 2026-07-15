using Atlas.Application.Abstractions;
using Atlas.Domain.Countries;

namespace Atlas.Application.Countries;

/// <summary>Reference data: every country Atlas can employ in.</summary>
// LEARN: one class per use case reads heavier than a "CountryService" grab bag — on
// purpose. A use case's constructor lists exactly what that operation needs, its
// test file tests exactly one behavior, and deleting a feature deletes a file.
public sealed class ListCountries(ICountryRepository countries)
{
    public Task<IReadOnlyList<Country>> ExecuteAsync(CancellationToken cancellationToken) =>
        countries.ListAsync(cancellationToken);
}
