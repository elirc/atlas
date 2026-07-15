using Atlas.Api.Errors;
using Atlas.Application.Countries;
using Atlas.Application.Orgs;
using Atlas.Infrastructure;

namespace Atlas.Api.Composition;

/// <summary>
/// The composition root — the ONLY place in the Api allowed to see Infrastructure.
/// The architecture tests hold this line (Atlas.ArchitectureTests).
/// </summary>
public static class ServiceRegistration
{
    public static WebApplicationBuilder AddAtlas(this WebApplicationBuilder builder)
    {
        // Persistence (EF Core + port implementations) — Infrastructure's one entry point.
        builder.Services.AddAtlasPersistence(builder.Configuration);

        // WHY: TimeProvider.System is registered ONCE, here. Everything downstream
        // takes TimeProvider by constructor — which is why tests can freeze time.
        builder.Services.AddSingleton(TimeProvider.System);

        // Use cases: scoped, cheap, no interfaces — they ARE the application's API.
        // LEARN: no ICreateClientOrg. An interface with exactly one implementation
        // and no seam-consumer is ceremony; ports earn interfaces, use cases don't.
        builder.Services.AddScoped<ListCountries>();
        builder.Services.AddScoped<GetCountry>();
        builder.Services.AddScoped<CreateClientOrg>();
        builder.Services.AddScoped<GetClientOrg>();
        builder.Services.AddScoped<ListClientOrgs>();

        // Errors → RFC 7807, one translation table for the whole API.
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<AtlasExceptionHandler>();

        // OpenAPI: generated from code, committed to the repo, drift-gated in CI.
        builder.Services.AddOpenApi();

        return builder;
    }
}
