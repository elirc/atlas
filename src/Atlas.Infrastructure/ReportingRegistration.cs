using Atlas.Application.Abstractions;
using Atlas.Infrastructure.Caching;
using Atlas.Infrastructure.Persistence.Reporting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Infrastructure;

/// <summary>
/// Wires the reporting read side and the reference-data cache. Its own extension so the composition
/// root adds it with ONE call and the parallel sprints (S12 vendor swap, S13 hardening) integrate
/// without editing <see cref="DependencyInjection.AddAtlasPersistence"/>'s body. See meta/NOTES.md.
/// </summary>
public static class ReportingRegistration
{
    public static IServiceCollection AddAtlasReporting(this IServiceCollection services)
    {
        // The read model: scoped, DbContext-backed, no tracking. A QUERY adapter, not a repository —
        // it returns projections, never aggregates (ADR-0017).
        services.AddScoped<IReportQueries, ReportQueries>();

        // Reference-data cache, ledger entry #1 (supported countries). The store is a singleton
        // (AddMemoryCache); the decorator is scoped so it can hold the scoped DB-backed reader while
        // the cache itself outlives the request. NEVER register per-org data through this seam.
        services.AddMemoryCache();
        services.AddScoped<SupportedCountriesReader>();
        services.AddScoped<ISupportedCountries>(sp => new CachedSupportedCountries(
            sp.GetRequiredService<SupportedCountriesReader>(),
            sp.GetRequiredService<IMemoryCache>()));

        // Ledger entry #2 (FX rates) is CachingFxRates. It is intentionally NOT decorated here: the FX
        // adapter is being replaced by a live vendor in S12, and its cache belongs with it. The
        // decorator and its invalidation contract ship in this sprint (Caching/CachingFxRates.cs); the
        // one-line wiring is handed to the S12 merge so two sprints do not fight over the IFxRates
        // registration. ADR-0017 records both entries; NOTES.md records the hand-off.
        return services;
    }
}
