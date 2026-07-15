using Atlas.Domain.Countries;

namespace Atlas.Application.Abstractions;

public interface ICountryRepository
{
    Task<IReadOnlyList<Country>> ListAsync(CancellationToken cancellationToken);
    Task<Country?> FindAsync(CountryCode code, CancellationToken cancellationToken);
}
