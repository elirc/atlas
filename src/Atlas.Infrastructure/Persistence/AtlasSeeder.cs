using Atlas.Application.Abstractions;
using Atlas.Domain.Access;
using Atlas.Domain.Countries;
using Atlas.Domain.Orgs;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Persistence;

/// <summary>Idempotent dev/demo seed: reference countries, a platform-ops user, one demo org.</summary>
public static class AtlasSeeder
{
    public static async Task SeedAsync(
        AtlasDbContext db,
        TimeProvider clock,
        IPasswordHasher hasher,
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

        if (!await db.Set<User>().AnyAsync(cancellationToken))
        {
            // Dev-only credentials, documented in the README. GOTCHA: never seed a
            // production database with a known password — S15's deploy runbook
            // makes the seed command refuse outside Development.
            db.Set<User>().Add(User.Create(
                EmailAddress.From("ops@atlas.example"),
                "Atlas Ops (dev)",
                hasher.Hash("atlas-ops-dev-password"),
                clock.GetUtcNow(),
                isAtlasOps: true));
        }

        if (!await db.ClientOrgs.AnyAsync(cancellationToken))
        {
            db.ClientOrgs.Add(ClientOrg.Create("Demo Client Co", clock.GetUtcNow()));
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
