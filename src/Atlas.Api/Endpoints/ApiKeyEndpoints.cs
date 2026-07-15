using Atlas.Api.Mapping;
using Atlas.Application.ApiKeys;
using Atlas.Contracts.V1;

namespace Atlas.Api.Endpoints;

public static class ApiKeyEndpoints
{
    public static RouteGroupBuilder MapApiKeys(this RouteGroupBuilder api)
    {
        // WHY the route is /api-keys, not /keys: a partner using a key to MANAGE keys is a real
        // bootstrapping-vs-rotation scenario, and the resource name should be unambiguous in an
        // integrator's code.
        RouteGroupBuilder group = api.MapGroup("/api-keys").WithTags("ApiKeys");

        group.MapPost("/", async (
            CreateApiKeyRequest request, CreateApiKey useCase, CancellationToken ct) =>
        {
            ApiKeyIssue issue = await useCase.ExecuteAsync(request.Name, request.Scopes, ct);
            return Results.Created($"/api/v1/api-keys/{issue.ApiKey.Id}", issue.ToDto());
        })
        .Produces<ApiKeyCreatedDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/", async (ListApiKeys useCase, CancellationToken ct) =>
            Results.Ok((await useCase.ExecuteAsync(ct)).Select(k => k.ToDto())))
        .Produces<IEnumerable<ApiKeyDto>>();

        group.MapDelete("/{id:guid}", async (Guid id, RevokeApiKey useCase, CancellationToken ct) =>
        {
            await useCase.ExecuteAsync(id, ct);
            return Results.NoContent();
        })
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return api;
    }
}
