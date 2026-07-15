using Atlas.Application.Abstractions;
using Atlas.Domain.Countries;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Persistence.Reporting;

/// <summary>
/// The uncached reference read: the supported countries straight from the <c>countries</c> table.
/// The whole set is a handful of rows, so it loads as entities and maps in memory — no projection
/// gymnastics for six rows. The decorator (<c>CachedSupportedCountries</c>) is what keeps this off
/// the database on the hot path.
/// </summary>
internal sealed class SupportedCountriesReader(AtlasDbContext db) : ISupportedCountries
{
    public async Task<IReadOnlyList<SupportedCountry>> AllAsync(CancellationToken cancellationToken)
    {
        List<Country> countries = await db.Set<Country>().AsNoTracking()
            .OrderBy(c => c.Code)
            .ToListAsync(cancellationToken);

        return [.. countries.Select(c => new SupportedCountry(c.Code.Value, c.Name, c.Currency))];
    }
}
