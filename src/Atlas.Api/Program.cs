using Atlas.Api.Composition;
using Atlas.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddAtlas();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// S15: request correlation + timing, FIRST so every log line — including a 401 from the bearer
// middleware or a 500 from the exception handler — carries the same correlation id.
app.UseMiddleware<Atlas.Api.Observability.RequestTelemetryMiddleware>();

// Bearer token → RequestContext, for every request. Runs AFTER the exception
// handler wraps the pipeline so a bad token becomes a problem+json 401.
app.UseMiddleware<Atlas.Api.Auth.BearerAuthenticationMiddleware>();

// Idempotency-Key honoring runs AFTER auth (it needs the caller's org to scope keys) and still
// inside the exception handler (a key conflict becomes a problem+json 422). It replays the original
// response for a repeated key, so a client retry never executes a money-moving POST twice.
app.UseMiddleware<Atlas.Api.Idempotency.IdempotencyMiddleware>();

// OpenAPI is served in every environment: the drift test (Api.Tests) and CI diff
// the committed document against exactly what this endpoint returns.
app.MapOpenApi();

// S15: liveness + readiness probes (root-mapped, excluded from the OpenAPI contract).
app.MapHealth();

RouteGroupBuilder api = app.MapGroup("/api/v1");
api.MapCountries();
api.MapClientOrgs();
api.MapAuth();
api.MapWorkers();
api.MapAuditLog();
api.MapQuotes();
api.MapEngagements();
api.MapLeave();
api.MapPayroll();
api.MapPayrollVendors();
api.MapExpenses();
api.MapApprovals();
api.MapContractors();
api.MapInvoices();
api.MapWebhooks();
api.MapApiKeys();
api.MapCompliance();
api.MapReports();

// S12: /api/v2 is a SIBLING base path, not a rewrite — a new version is a new shape over the same
// domain. Today it re-issues exactly ONE resource (the payslip, with grouped lines); v1 keeps serving
// everything, the payslip included, from the same use cases (ADR-0015).
RouteGroupBuilder apiV2 = app.MapGroup("/api/v2");
apiV2.MapPayslipsV2();

// `dotnet run -- migrate seed` performs database chores and exits.
if (await StartupTasks.HandleDatabaseCommandsAsync(app, args))
{
    return;
}

// S15: fail fast on a broken deploy — a startup self-check probes the database before we accept
// traffic, so a misconfigured connection string is a loud boot failure, not a flood of 500s. Skipped
// under the test host (the in-memory SQLite factory is always ready).
if (!app.Environment.IsEnvironment("Testing"))
{
    await StartupSelfCheck.RunAsync(app);
}

app.Run();

// WHY: WebApplicationFactory (Api.Tests) and the architecture tests need a
// compile-safe handle to this assembly's entry point.
public partial class Program;
