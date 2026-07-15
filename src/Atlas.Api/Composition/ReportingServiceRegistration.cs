using Atlas.Application.Reporting;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Api.Composition;

/// <summary>
/// Registers the report use cases. A separate extension (not lines in <see cref="ServiceRegistration"/>)
/// so this sprint's DI additions land in one new file and merge clean beside S12/S13. See meta/NOTES.md.
/// </summary>
public static class ReportingServiceRegistration
{
    // Use cases: scoped, no interfaces — the read half of the application's API, same stance as every
    // other use case (ports earn interfaces; use cases don't).
    public static IServiceCollection AddAtlasReportUseCases(this IServiceCollection services)
    {
        services.AddScoped<GetHeadcountReport>();
        services.AddScoped<GetEmploymentCostReport>();
        services.AddScoped<GetUpcomingRenewals>();
        services.AddScoped<GetComplianceRoster>();
        return services;
    }
}
