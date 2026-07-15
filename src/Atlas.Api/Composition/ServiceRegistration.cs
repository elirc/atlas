using Atlas.Api.Auth;
using Atlas.Api.Errors;
using Atlas.Application.Abstractions;
using Atlas.Application.Auth;
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
        builder.Services.AddScoped<RegisterOrg>();
        builder.Services.AddScoped<Login>();
        builder.Services.AddScoped<Logout>();
        builder.Services.AddScoped<GetMe>();

        // The caller's identity: one RequestContext per request, exposed to the
        // Application layer as the ICurrentContext port.
        builder.Services.AddScoped<RequestContext>();
        builder.Services.AddScoped<ICurrentContext>(sp => sp.GetRequiredService<RequestContext>());

        // Errors → RFC 7807, one translation table for the whole API.
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<AtlasExceptionHandler>();

        // OpenAPI: generated from code, committed to the repo, drift-gated in CI.
        builder.Services.AddOpenApi();

        return builder;
    }
}
