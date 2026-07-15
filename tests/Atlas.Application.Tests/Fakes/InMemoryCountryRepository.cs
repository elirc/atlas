using Atlas.Application.Abstractions;
using Atlas.Domain.Countries;

namespace Atlas.Application.Tests.Fakes;

public sealed class InMemoryCountryRepository(params Country[] seed) : ICountryRepository
{
    private readonly List<Country> _rows = [.. seed];

    public Task<IReadOnlyList<Country>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Country>>([.. _rows.OrderBy(c => c.Code.Value)]);

    public Task<Country?> FindAsync(CountryCode code, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.FirstOrDefault(c => c.Code == code));
}
