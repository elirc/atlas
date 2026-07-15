using Atlas.Application.Abstractions;
using Atlas.Domain.Countries;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Persistence.Repositories;

internal sealed class CountryRepository(AtlasDbContext db) : ICountryRepository
{
    public async Task<IReadOnlyList<Country>> ListAsync(CancellationToken cancellationToken) =>
        await db.Countries.AsNoTracking()
            .OrderBy(c => c.Code)
            .ToListAsync(cancellationToken);

    public Task<Country?> FindAsync(CountryCode code, CancellationToken cancellationToken) =>
        db.Countries.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Code == code, cancellationToken);
}
