using Atlas.Application.Abstractions;
using Atlas.Domain.Access;
using Atlas.Domain.Countries;
using Atlas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Api.Tests.Infrastructure;

/// <summary>
/// Boots the real API over an in-memory SQLite database. No Docker, no network —
/// the whole HTTP stack, real EF, real DI, in milliseconds.
/// </summary>
// LEARN: the connection is held open for the factory's lifetime because an
// in-memory SQLite database lives exactly as long as its (only) open connection.
// Close it and the schema evaporates mid-test.
public sealed class AtlasApiFactory : WebApplicationFactory<Program>
{
    public const string OpsEmail = "ops@atlas.test";
    public const string OpsPassword = "ops-password-for-tests";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Swap the configured provider for the test connection.
            services.RemoveAll<DbContextOptions<AtlasDbContext>>();

            _connection.Open();
            services.AddDbContext<AtlasDbContext>(options => options.UseSqlite(_connection));

            // WHY: PBKDF2 at 600k iterations is ~200ms per hash BY DESIGN — perfect
            // against attackers, awful in a test suite that logs in dozens of times.
            // The real hasher has its own focused unit tests (Pbkdf2PasswordHasherTests);
            // HTTP-level tests only need "some working hasher".
            services.RemoveAll<IPasswordHasher>();
            services.AddSingleton<IPasswordHasher, FastTestHasher>();

            // Schema + reference data + a platform-ops account, once per factory.
            using ServiceProvider provider = services.BuildServiceProvider();
            using IServiceScope scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AtlasDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            db.Database.EnsureCreated();

            // WHY the guard: WithWebHostBuilder (used by tests that swap an adapter, e.g. a fake
            // webhook transport) re-runs this ConfigureWebHost over the SAME in-memory connection, so
            // seeding must be idempotent — a second unguarded insert trips the countries unique index.
            if (!db.Countries.Any())
            {
                db.Countries.AddRange(
                    new Country(CountryCode.From("DEU"), "Germany", "EUR"),
                    new Country(CountryCode.From("PHL"), "Philippines", "PHP"),
                    new Country(CountryCode.From("USA"), "United States", "USD"));
                db.Set<User>().Add(User.Create(
                    EmailAddress.From(OpsEmail), "Test Ops",
                    hasher.Hash(OpsPassword), DateTimeOffset.UnixEpoch, isAtlasOps: true));
                db.SaveChanges();
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _connection.Dispose();
    }

    private sealed class FastTestHasher : IPasswordHasher
    {
        public string Hash(string password) => $"fast:{password}";

        public bool Verify(string password, string storedHash) =>
            storedHash == $"fast:{password}";
    }
}

file static class ServiceCollectionExtensions
{
    public static void RemoveAll<T>(this IServiceCollection services)
    {
        foreach (ServiceDescriptor descriptor in
                 services.Where(d => d.ServiceType == typeof(T)).ToList())
        {
            services.Remove(descriptor);
        }
    }
}
