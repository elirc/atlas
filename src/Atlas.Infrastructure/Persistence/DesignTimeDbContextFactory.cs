using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Atlas.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef` at design time to build a context for migration
/// scaffolding. Never runs in the application.
/// </summary>
// WHY: migrations are generated against the Postgres provider because Postgres is
// what production means here; tests run SQLite via EnsureCreated (model-derived
// schema) and never touch these files.
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AtlasDbContext>
{
    public AtlasDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AtlasDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=atlas;Username=atlas;Password=atlas")
            .Options;

        return new AtlasDbContext(options);
    }
}
