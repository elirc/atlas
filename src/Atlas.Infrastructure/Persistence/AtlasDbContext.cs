using Atlas.Domain.Countries;
using Atlas.Domain.Orgs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Atlas.Infrastructure.Persistence;

public sealed class AtlasDbContext(DbContextOptions<AtlasDbContext> options)
    : DbContext(options)
{
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<ClientOrg> ClientOrgs => Set<ClientOrg>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AtlasDbContext).Assembly);

        if (Database.IsSqlite())
        {
            ApplySqliteTimestampWorkaround(modelBuilder);
        }
    }

    // GOTCHA: SQLite has no real timestamp type; the provider stores DateTimeOffset
    // as TEXT and then refuses to ORDER BY or compare it — our keyset pagination
    // would 500. On SQLite only, store every DateTimeOffset as UTC ticks (INTEGER):
    // ordering and comparison become integer ops, and semantics stay identical
    // because Atlas only ever stores UTC instants.
    // LEARN: this is what "the same model, different providers" actually costs —
    // and why the Api.Tests run on SQLite is not a perfect proxy for Postgres.
    // Anything provider-sensitive gets an explicit note (see ADR-0001 consequences).
    private static void ApplySqliteTimestampWorkaround(ModelBuilder modelBuilder)
    {
        var utcTicks = new ValueConverter<DateTimeOffset, long>(
            value => value.UtcTicks,
            ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties()
                         .Where(p => p.ClrType == typeof(DateTimeOffset)
                                     || p.ClrType == typeof(DateTimeOffset?)))
            {
                property.SetValueConverter(utcTicks);
            }
        }
    }
}
