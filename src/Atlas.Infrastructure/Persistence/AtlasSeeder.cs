using Atlas.Domain.Countries;
using Atlas.Domain.Orgs;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Persistence;

/// <summary>Idempotent dev/demo seed: reference countries plus one demo client org.</summary>
public static class AtlasSeeder
{
    public static async Task SeedAsync(
        AtlasDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!await db.Countries.AnyAsync(cancellationToken))
        {
            db.Countries.AddRange(
                new Country(CountryCode.From("DEU"), "Germany", "EUR"),
                new Country(CountryCode.From("GBR"), "United Kingdom", "GBP"),
                new Country(CountryCode.From("PHL"), "Philippines", "PHP"),
                new Country(CountryCode.From("USA"), "United States", "USD"));
        }

        if (!await db.ClientOrgs.AnyAsync(cancellationToken))
        {
            db.ClientOrgs.Add(ClientOrg.Create("Demo Client Co", clock.GetUtcNow()));
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
