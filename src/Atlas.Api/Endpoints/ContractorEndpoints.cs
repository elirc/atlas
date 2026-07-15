using Atlas.Api.Mapping;
using Atlas.Application.Contractors;
using Atlas.Contracts.V1;
using Atlas.Domain.Invoicing;

namespace Atlas.Api.Endpoints;

public static class ContractorEndpoints
{
    public static RouteGroupBuilder MapContractors(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/contractors").WithTags("Contractors");

        group.MapPost("/", async (
            CreateContractorRequest request,
            CreateContractor useCase,
            CancellationToken ct) =>
        {
            Contractor contractor = await useCase.ExecuteAsync(
                request.Name, request.Email, request.Country, request.PayoutCurrency, ct);

            return Results.Created($"/api/v1/contractors/{contractor.Id}", contractor.ToDto());
        })
        .Produces<ContractorDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/", async (ListContractors useCase, CancellationToken ct) =>
        {
            IReadOnlyList<Contractor> contractors = await useCase.ExecuteAsync(ct);
            return Results.Ok(contractors.Select(c => c.ToDto()));
        })
        .Produces<IEnumerable<ContractorDto>>();

        return api;
    }
}
