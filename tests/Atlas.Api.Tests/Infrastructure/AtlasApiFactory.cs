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

            // Schema + reference data, once per factory.
            using ServiceProvider provider = services.BuildServiceProvider();
            using IServiceScope scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AtlasDbContext>();
            db.Database.EnsureCreated();
            db.Countries.AddRange(
                new Country(CountryCode.From("DEU"), "Germany", "EUR"),
                new Country(CountryCode.From("PHL"), "Philippines", "PHP"),
                new Country(CountryCode.From("USA"), "United States", "USD"));
            db.SaveChanges();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _connection.Dispose();
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
