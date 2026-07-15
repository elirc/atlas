using Atlas.Api.Mapping;
using Atlas.Application.Orgs;
using Atlas.Application.Pagination;
using Atlas.Contracts.V1;
using Atlas.Domain.Orgs;

namespace Atlas.Api.Endpoints;

public static class ClientOrgEndpoints
{
    public static RouteGroupBuilder MapClientOrgs(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/client-orgs").WithTags("Client orgs");

        group.MapPost("/", async (
            CreateClientOrgRequest request,
            CreateClientOrg useCase,
            CancellationToken ct) =>
        {
            var org = await useCase.ExecuteAsync(request.Name, ct);

            // WHY: 201 + Location is the contract for "you made a thing" — clients
            // shouldn't need to parse the body to learn where the thing lives.
            return Results.Created($"/api/v1/client-orgs/{org.Id}", org.ToDto());
        })
        .Produces<ClientOrgDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{id:guid}", async (Guid id, GetClientOrg useCase, CancellationToken ct) =>
        {
            var org = await useCase.ExecuteAsync(id, ct);
            return Results.Ok(org.ToDto());
        })
        .Produces<ClientOrgDto>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", async (
            string? cursor,
            int? limit,
            HttpResponse response,
            ListClientOrgs useCase,
            CancellationToken ct) =>
        {
            Page<ClientOrg> page = await useCase.ExecuteAsync(cursor, limit, ct);

            // LEARN: the next-page token rides a response HEADER (X-Next-Cursor), so
            // the body stays a plain array of the resource. Envelope-vs-header is a
            // real API-design fork; ADR-0002 records why Atlas chose headers.
            if (page.NextCursor is { } next)
            {
                response.Headers["X-Next-Cursor"] = next;
            }

            return Results.Ok(page.Items.Select(o => o.ToDto()));
        })
        .Produces<IEnumerable<ClientOrgDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest);

        return api;
    }
}
