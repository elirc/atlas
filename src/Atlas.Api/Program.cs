using Atlas.Api.Composition;
using Atlas.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddAtlas();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// OpenAPI is served in every environment: the drift test (Api.Tests) and CI diff
// the committed document against exactly what this endpoint returns.
app.MapOpenApi();

RouteGroupBuilder api = app.MapGroup("/api/v1");
api.MapCountries();
api.MapClientOrgs();

// `dotnet run -- migrate seed` performs database chores and exits.
if (await StartupTasks.HandleDatabaseCommandsAsync(app, args))
{
    return;
}

app.Run();

// WHY: WebApplicationFactory (Api.Tests) and the architecture tests need a
// compile-safe handle to this assembly's entry point.
public partial class Program;
