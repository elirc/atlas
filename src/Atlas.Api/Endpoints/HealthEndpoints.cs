using Atlas.Api.Mapping;
using Atlas.Application.Abstractions;
using Atlas.Application.Operations;
using Atlas.Contracts.V1;

namespace Atlas.Api.Endpoints;

/// <summary>
/// Operational endpoints, mapped at the ROOT (not under <c>/api/v1</c>) and excluded from the
/// OpenAPI document: they are for orchestrators and load balancers, not partners, and versioning
/// them would be a category error. Both are anonymous — the bearer middleware only rejects a
/// <em>presented</em> bad credential; no header is fine here.
/// </summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealth(this IEndpointRouteBuilder app)
    {
        // ⚠️ LIVENESS vs READINESS — getting them backwards causes the outage you were preventing.
        //    /health/live answers "is the process alive?" and checks NOTHING downstream on purpose.
        //    An orchestrator RESTARTS a pod that fails liveness; if liveness pinged the database, one
        //    DB blip would restart every pod at once. Liveness is 200 unless the process is wedged.
        app.MapGet("/health/live", () => Results.Ok(new HealthDto("live")))
            .WithName("HealthLive")
            .ExcludeFromDescription();

        // /health/ready answers "should THIS instance receive traffic?" A failing readiness check
        // makes the load balancer DRAIN this node (no restart). It is gated on the database (nothing
        // to serve without it → 503). The outbox dispatcher's lag is reported in the body too, but by
        // deliberate decision it does NOT gate readiness — see AtlasReadinessProbe and ADR-0018. A
        // lagging dispatcher must page an operator, not make every node report unhealthy and cascade.
        app.MapGet("/health/ready", async (IReadinessProbe probe, CancellationToken cancellationToken) =>
        {
            ReadinessReport report = await probe.CheckAsync(cancellationToken);
            return Results.Json(
                report.ToDto(),
                statusCode: report.IsReady ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        })
            .WithName("HealthReady")
            .ExcludeFromDescription();

        return app;
    }
}
